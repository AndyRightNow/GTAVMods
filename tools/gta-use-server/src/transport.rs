use crate::storage::{Journal, read_json};
use serde_json::{Value, json};
use std::{io, path::PathBuf, sync::Arc, time::Duration};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::windows::named_pipe::NamedPipeClient,
    sync::{mpsc, oneshot},
    time::{Instant, sleep, timeout_at},
};
use windows_sys::Win32::{
    Foundation::INVALID_HANDLE_VALUE,
    Storage::FileSystem::{CreateFileW, OPEN_EXISTING},
};

pub const MAX_REQUEST: usize = 1024 * 1024;
pub fn valid_id(value: &str) -> bool {
    value.len() == 32
        && value
            .bytes()
            .all(|c| c.is_ascii_digit() || (b'a'..=b'f').contains(&c))
}

struct Work {
    request: Value,
    reply: oneshot::Sender<Value>,
}
#[derive(Clone)]
pub struct Link {
    pub root: PathBuf,
    pub process: u32,
    sender: mpsc::Sender<Work>,
    pub log: Arc<Journal>,
}
impl Link {
    pub fn start(root: PathBuf, process: u32, log: Arc<Journal>) -> Self {
        let (sender, mut receiver) = mpsc::channel::<Work>(64);
        let link = Self {
            root,
            process,
            sender,
            log,
        };
        let worker_root = link.root.clone();
        let worker_log = link.log.clone();
        tokio::spawn(async move {
            while let Some(work) = receiver.recv().await {
                if work.reply.is_closed() {
                    continue;
                }
                // This actor owns each whole exchange even if its HTTP/WS caller
                // disconnects. Cancellation never overlaps or replays pipe I/O.
                let request = work.request;
                let identifier = request["id"].clone();
                let session = request["session"].clone();
                let result = exchange(&worker_root, process, &request).await;
                let response = match result {
                    Ok(value) => value,
                    Err(error) => {
                        json!({"id":identifier,"session":session,"ok":false,"uncertain":true,
                        "error":format!("{error}; inspect response-{}.json before retrying.", request["id"].as_str().unwrap_or_default())})
                    }
                };
                let _ = worker_log.record(
                    "response",
                    json!({"id":identifier,"ok":response["ok"],"uncertain":response["uncertain"],"error":response["error"]}),
                );
                let _ = work.reply.send(response);
            }
        });
        link
    }
    pub fn discovery(&self) -> io::Result<Value> {
        read_json(&self.root.join(format!("bridge-{}.json", self.process)))
    }
    pub fn directory(&self) -> io::Result<PathBuf> {
        Ok(PathBuf::from(
            self.discovery()?["bridge"]["directory"]
                .as_str()
                .ok_or_else(|| io::Error::other("Missing session directory"))?,
        ))
    }
    pub async fn dispatch(&self, request: Value) -> Result<Value, String> {
        if !request.is_object()
            || !request
                .get("id")
                .and_then(Value::as_str)
                .is_some_and(valid_id)
        {
            return Err("Expected an object with a 32-character request ID.".into());
        }
        if serde_json::to_vec(&request)
            .map_err(|e| e.to_string())?
            .len()
            > MAX_REQUEST
        {
            return Err("Request exceeds 1 MiB.".into());
        }
        self.log
            .record(
                "request",
                json!({"id":request["id"],"session":request["session"],"op":request["op"]}),
            )
            .map_err(|e| e.to_string())?;
        let (reply, result) = oneshot::channel();
        self.sender
            .try_send(Work { request, reply })
            .map_err(|_| "Bridge queue full or closed; request was not submitted.".to_string())?;
        result
            .await
            .map_err(|_| "Bridge worker stopped; inspect receipt before retrying.".to_string())
    }
}
async fn exchange(root: &std::path::Path, process: u32, request: &Value) -> io::Result<Value> {
    let deadline = Instant::now() + Duration::from_secs(4);
    let pipe = read_json(&root.join(format!("bridge-{process}.json")))?["pipe"]
        .as_str()
        .ok_or_else(|| io::Error::other("Missing pipe name"))?
        .to_owned();
    let payload = serde_json::to_vec(request)?;
    timeout_at(deadline, async {
        let mut client = loop {
            // GENERIC_WRITE also requests FILE_CREATE_PIPE_INSTANCE, denied
            // by the game's data-only ACL. Match its least-privilege client.
            let path: Vec<u16> = format!("\\\\.\\pipe\\{pipe}\0").encode_utf16().collect();
            let handle = unsafe {
                CreateFileW(
                    path.as_ptr(),
                    0x00100003,
                    0,
                    std::ptr::null(),
                    OPEN_EXISTING,
                    0x40110000,
                    std::ptr::null_mut(),
                )
            };
            if handle != INVALID_HANDLE_VALUE {
                // Transfer exclusive ownership of this overlapped handle
                // to Tokio; it closes/cancels outstanding I/O on drop.
                break unsafe { NamedPipeClient::from_raw_handle(handle) }
                    .map_err(|e| io::Error::other(format!("Register pipe I/O: {e}")))?;
            }
            match io::Error::last_os_error() {
                error if matches!(error.raw_os_error(), Some(2 | 231)) => {
                    sleep(Duration::from_millis(10)).await
                }
                error => return Err(io::Error::other(format!("Open game pipe: {error}"))),
            }
        };
        let mut frame = Vec::with_capacity(payload.len() + 4);
        frame.extend_from_slice(&(payload.len() as u32).to_le_bytes());
        frame.extend_from_slice(&payload);
        client.write_all(&frame).await?;
        let size = client.read_u32_le().await? as usize;
        if size == 0 || size > 4 * 1024 * 1024 {
            return Err(io::Error::other("Invalid response frame size"));
        }
        let mut response = vec![0; size];
        client.read_exact(&mut response).await?;
        serde_json::from_slice(&response).map_err(io::Error::other)
    })
    .await
    .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "Bridge I/O timed out"))?
}
