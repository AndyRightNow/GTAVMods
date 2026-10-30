//! Bounded OS keyboard delivery for interactions that ignore control-value injection.
use crate::transport::Link;
use serde_json::{Value, json};
use std::{io, sync::Arc, time::Duration};
use tokio::{
    sync::watch,
    time::{Instant, sleep, timeout},
};
use windows_sys::Win32::UI::{Input::KeyboardAndMouse::*, WindowsAndMessaging::*};

pub trait Device: Send + Sync {
    fn reserve(&self, process: u32) -> io::Result<Box<dyn Send>>;
    fn foreground(&self, process: u32) -> bool;
    fn held(&self, key: u16) -> bool;
    fn send(&self, key: u16, down: bool) -> io::Result<()>;
}
struct Reservation(usize);
impl Drop for Reservation {
    fn drop(&mut self) {
        unsafe {
            windows_sys::Win32::Foundation::CloseHandle(self.0 as _);
        }
    }
}
pub struct WindowsKeyboard;
impl Device for WindowsKeyboard {
    fn reserve(&self, process: u32) -> io::Result<Box<dyn Send>> {
        use windows_sys::Win32::{
            Foundation::{ERROR_ALREADY_EXISTS, GetLastError},
            System::Threading::CreateMutexW,
        };
        let name: Vec<u16> = format!("Local\\GTAVMods-Keyboard-{process}\0")
            .encode_utf16()
            .collect();
        let handle = unsafe { CreateMutexW(std::ptr::null(), 0, name.as_ptr()) };
        if handle.is_null() {
            return Err(io::Error::last_os_error());
        }
        let occupied = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
        let reservation = Reservation(handle as usize);
        if occupied {
            return Err(io::Error::other("Another keyboard hold owns this game."));
        }
        Ok(Box::new(reservation))
    }
    fn foreground(&self, process: u32) -> bool {
        let mut pid = 0;
        unsafe {
            GetWindowThreadProcessId(GetForegroundWindow(), &mut pid);
        }
        pid == process && pid != 0
    }
    fn held(&self, key: u16) -> bool {
        unsafe { GetAsyncKeyState(i32::from(key)) < 0 }
    }
    fn send(&self, key: u16, down: bool) -> io::Result<()> {
        let scan = unsafe { MapVirtualKeyW(u32::from(key), MAPVK_VK_TO_VSC) } as u16;
        if scan == 0 {
            return Err(io::Error::other("Key has no scan code."));
        }
        let extended = matches!(key, 0x21..=0x28 | 0x2d | 0x2e);
        let input = INPUT {
            r#type: INPUT_KEYBOARD,
            Anonymous: INPUT_0 {
                ki: KEYBDINPUT {
                    wVk: 0,
                    wScan: scan,
                    dwFlags: KEYEVENTF_SCANCODE
                        | if down { 0 } else { KEYEVENTF_KEYUP }
                        | if extended { KEYEVENTF_EXTENDEDKEY } else { 0 },
                    time: 0,
                    dwExtraInfo: 0,
                },
            },
        };
        if unsafe { SendInput(1, &input, std::mem::size_of::<INPUT>() as i32) } != 1 {
            return Err(io::Error::other(
                "Keyboard input rejected; inspect before retrying.",
            ));
        }
        Ok(())
    }
}
fn key_code(name: &str) -> Option<u16> {
    let value = name.to_ascii_uppercase();
    if value.len() == 1 && value.as_bytes()[0].is_ascii_alphanumeric() {
        return Some(u16::from(value.as_bytes()[0]));
    }
    Some(match value.as_str() {
        "ENTER" => 0x0d,
        "ESCAPE" => 0x1b,
        "SPACE" => 0x20,
        "TAB" => 9,
        "SHIFT" => 0xa0,
        "CONTROL" => 0xa2,
        "LEFT" => 0x25,
        "UP" => 0x26,
        "RIGHT" => 0x27,
        "DOWN" => 0x28,
        "INSERT" => 0x2d,
        "DELETE" => 0x2e,
        "HOME" => 0x24,
        "END" => 0x23,
        "PAGEUP" => 0x21,
        "PAGEDOWN" => 0x22,
        _ => {
            return value
                .strip_prefix('F')
                .and_then(|n| n.parse::<u16>().ok())
                .filter(|n| (1..=12).contains(n))
                .map(|n| 0x6f + n);
        }
    })
}
struct HeldKey {
    device: Arc<dyn Device>,
    code: u16,
    released: bool,
}
impl HeldKey {
    fn release(&mut self) -> io::Result<()> {
        self.device.send(self.code, false)?;
        self.released = true;
        Ok(())
    }
}
impl Drop for HeldKey {
    fn drop(&mut self) {
        if !self.released {
            let _ = self.device.send(self.code, false);
        }
    }
}
async fn authorize(link: &Link, session: &str, token: &str) -> Result<(), String> {
    let reply = link
        .dispatch(
            json!({"version":1,"id":uuid::Uuid::new_v4().simple().to_string(),
        "session":session,"op":"lease","action":"heartbeat","token":token,"ms":5000}),
        )
        .await?;
    if reply["ok"] != true {
        return Err("Keyboard controller lease was rejected; inspect before retrying.".into());
    }
    Ok(())
}
pub async fn hold(
    link: &Link,
    data: Value,
    device: Arc<dyn Device>,
    mut shutdown: watch::Receiver<bool>,
) -> Result<Value, String> {
    let key = data["key"].as_str().ok_or("Expected one key name.")?;
    let code = key_code(key).ok_or("Unsupported key; use one game key, no OS chords.")?;
    let ms = data["ms"]
        .as_u64()
        .filter(|n| (1..=5000).contains(n))
        .ok_or("Key hold must be 1..5000 milliseconds.")?;
    let session = data["session"].as_str().ok_or("Expected game session.")?;
    let token = data["token"]
        .as_str()
        .filter(|s| crate::transport::valid_id(s))
        .ok_or("Expected current controller token.")?;
    if *shutdown.borrow() {
        return Err("Service is stopping.".into());
    }
    let _reservation = device.reserve(link.process).map_err(|e| e.to_string())?;
    authorize(link, session, token).await?;
    if !device.foreground(link.process) {
        return Err("GTA must be the foreground process for keyboard input.".into());
    }
    if device.held(code) {
        return Err("Key is already held; preserve user input.".into());
    }
    let mut held = HeldKey {
        device: device.clone(),
        code,
        released: false,
    };
    device.send(code, true).map_err(|e| e.to_string())?;
    let started = Instant::now();
    let end = started + Duration::from_millis(ms);
    let mut next_lease = started + Duration::from_millis(500);
    let mut error = None;
    while Instant::now() < end {
        if *shutdown.borrow() {
            error = Some("Service stopped during key hold.");
            break;
        }
        if !device.foreground(link.process) {
            error = Some("GTA lost focus during key hold.");
            break;
        }
        if Instant::now() >= next_lease {
            let budget = end
                .saturating_duration_since(Instant::now())
                .min(Duration::from_millis(200));
            if !matches!(
                timeout(budget, authorize(link, session, token)).await,
                Ok(Ok(()))
            ) {
                error = Some("Controller lease could not be verified during key hold.");
                break;
            }
            next_lease = Instant::now() + Duration::from_millis(500);
        }
        tokio::select! {
            _ = sleep(end.saturating_duration_since(Instant::now()).min(Duration::from_millis(20))) => {},
            _ = shutdown.changed() => { error=Some("Service stopped during key hold."); break; },
        }
    }
    held.release().map_err(|e| e.to_string())?;
    let elapsed = started.elapsed().as_millis();
    let _ = link.log.record(
        "keyboard.completed",
        json!({"key":key,"requestedMs":ms,"elapsedMs":elapsed,"error":error}),
    );
    if let Some(error) = error {
        return Err(format!("{error} Key released; inspect game state."));
    }
    Ok(json!({"key":key,"requestedMs":ms,"elapsedMs":elapsed,"released":true}))
}
