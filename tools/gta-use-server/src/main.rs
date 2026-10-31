mod physical_input;
mod storage;
mod transport;
mod web;

use axum::serve::ListenerExt;
use serde_json::json;
use std::{io, path::PathBuf, sync::Arc, time::Duration};
use windows_sys::Win32::{
    Foundation::{CloseHandle, ERROR_ALREADY_EXISTS, GetLastError, HANDLE, WAIT_TIMEOUT},
    System::Threading::{CreateMutexW, OpenProcess, PROCESS_SYNCHRONIZE, WaitForSingleObject},
};

struct Handle(HANDLE);
impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}

fn main() {
    if let Err(error) = run() {
        use std::io::Write;
        eprintln!("{error}");
        let args: Vec<_> = std::env::args_os().collect();
        if let (Some(root), Some(process)) = (args.get(1), args.get(2)) {
            let path = PathBuf::from(root)
                .join(format!("service-{}-stdio.log", process.to_string_lossy()));
            if let Ok(mut file) = std::fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(path)
            {
                let _ = writeln!(
                    file,
                    "{}",
                    json!({"event":"service.start.failed","error":error.to_string()})
                );
            }
        }
        std::process::exit(1);
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args_os().collect();
    if args.len() != 4 {
        return Err(
            "Usage: gta-use-server <discovery-directory> <game-process-id> <python-executable>"
                .into(),
        );
    }
    let root = PathBuf::from(&args[1]);
    let process: u32 = args[2].to_str().ok_or("Invalid process")?.parse()?;
    let python = PathBuf::from(&args[3]);
    std::fs::create_dir_all(&root)?;
    let name: Vec<u16> = format!("Local\\GTAVMods-Service-{process}\0")
        .encode_utf16()
        .collect();
    let mutex = Handle(unsafe { CreateMutexW(std::ptr::null(), 0, name.as_ptr()) });
    if mutex.0.is_null() {
        return Err(io::Error::last_os_error().into());
    }
    if unsafe { GetLastError() } == ERROR_ALREADY_EXISTS {
        return Ok(());
    }
    let game = Handle(unsafe { OpenProcess(PROCESS_SYNCHRONIZE, 0, process) });
    if game.0.is_null() {
        return Err(io::Error::last_os_error().into());
    }
    let path = root.join(format!("service-{process}.json"));
    let log = Arc::new(storage::Journal::new(
        root.join(format!("service-{process}.jsonl")),
    )?);
    let panic_log = log.clone();
    std::panic::set_hook(Box::new(move |info| {
        let _ = panic_log.record("service.panic", json!({"error":info.to_string()}));
    }));
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .max_blocking_threads(4)
        .enable_all()
        .build()?;
    // Keep Win32 handle ownership in this scope; only its integer value crosses
    // the async boundary, and shutdown is awaited before the handle is closed.
    let game_handle = game.0 as usize;
    let result = runtime.block_on(async {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await?;
        let token = format!("{}{}",uuid::Uuid::new_v4().simple(),uuid::Uuid::new_v4().simple());
        let link = transport::Link::start(root,process,log.clone());
        let (shutdown, receiver) = tokio::sync::watch::channel(false);
        let app = web::app(link,token.clone(),python,receiver);
        storage::save_json(&path,&json!({"url":format!("http://127.0.0.1:{}",listener.local_addr()?.port()),"token":token,"process":process,"serviceProcess":std::process::id(),"backend":"axum"}))?;
        log.record("service.started",json!({"game":process,"backend":"axum","version":env!("CARGO_PKG_VERSION")}))?;
        // Interactive control packets should leave immediately, including
        // small HTTP responses sent alongside larger world-state bodies.
        let socket_log = log.clone();
        let listener = listener.tap_io(move |socket| {
            if let Err(error) = socket.set_nodelay(true) {
                let _ = socket_log.record("socket.configure.failed", json!({"error":error.to_string()}));
            }
        });
        let stop = async move {
            tokio::select! {
                _ = tokio::signal::ctrl_c() => {},
                _ = async { while unsafe {WaitForSingleObject(game_handle as HANDLE,0)} == WAIT_TIMEOUT {tokio::time::sleep(Duration::from_secs(1)).await;} } => {},
            }
            let _ = shutdown.send(true);
        };
        axum::serve(listener,app.into_make_service_with_connect_info::<std::net::SocketAddr>()).with_graceful_shutdown(stop).await
    });
    let _ = log.record(
        "service.stopped",
        json!({"error":result.as_ref().err().map(ToString::to_string)}),
    );
    if storage::read_json(&path)
        .ok()
        .and_then(|v| v["serviceProcess"].as_u64())
        == Some(std::process::id() as u64)
    {
        let _ = std::fs::remove_file(path);
    }
    result?;
    Ok(())
}
