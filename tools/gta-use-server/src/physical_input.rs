//! Bounded foreground keyboard and relative mouse input under the game lease.
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
    fn move_mouse(&self, dx: i32, dy: i32) -> io::Result<()>;
}
struct Reservation(usize);
impl Drop for Reservation {
    fn drop(&mut self) {
        unsafe {
            windows_sys::Win32::Foundation::CloseHandle(self.0 as _);
        }
    }
}
fn send_input(input: INPUT) -> io::Result<()> {
    if unsafe { SendInput(1, &input, std::mem::size_of::<INPUT>() as i32) } != 1 {
        return Err(io::Error::other(
            "Physical input rejected; inspect before retrying.",
        ));
    }
    Ok(())
}
fn mouse_input(dx: i32, dy: i32, flags: u32) -> INPUT {
    INPUT {
        r#type: INPUT_MOUSE,
        Anonymous: INPUT_0 {
            mi: MOUSEINPUT {
                dx,
                dy,
                mouseData: 0,
                dwFlags: flags,
                time: 0,
                dwExtraInfo: 0,
            },
        },
    }
}
pub struct WindowsInput;
impl Device for WindowsInput {
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
            return Err(io::Error::other("Another physical input owns this game."));
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
    fn move_mouse(&self, dx: i32, dy: i32) -> io::Result<()> {
        // Relative deltas only. Absolute pointer placement can spin a raw-input camera.
        send_input(mouse_input(
            dx,
            dy,
            MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE,
        ))
    }
    fn send(&self, key: u16, down: bool) -> io::Result<()> {
        let button = match (key, down) {
            (VK_LBUTTON, true) => Some(MOUSEEVENTF_LEFTDOWN),
            (VK_LBUTTON, false) => Some(MOUSEEVENTF_LEFTUP),
            (VK_RBUTTON, true) => Some(MOUSEEVENTF_RIGHTDOWN),
            (VK_RBUTTON, false) => Some(MOUSEEVENTF_RIGHTUP),
            (VK_MBUTTON, true) => Some(MOUSEEVENTF_MIDDLEDOWN),
            (VK_MBUTTON, false) => Some(MOUSEEVENTF_MIDDLEUP),
            _ => None,
        };
        if let Some(flags) = button {
            return send_input(mouse_input(0, 0, flags));
        }
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
        send_input(input)
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
        return Err(
            "Physical input controller lease was rejected; inspect before retrying.".into(),
        );
    }
    Ok(())
}
pub async fn keyboard(
    link: &Link,
    data: Value,
    device: Arc<dyn Device>,
    shutdown: watch::Receiver<bool>,
) -> Result<Value, String> {
    let key = data["key"].as_str().ok_or("Expected one key name.")?;
    let code = key_code(key).ok_or("Unsupported key; use one game key, no OS chords.")?;
    let ms = hold_duration(&data)?;
    hold(link, &data, device, shutdown, code, ms, json!({"key":key})).await
}
fn hold_duration(data: &Value) -> Result<u64, String> {
    data["ms"]
        .as_u64()
        .filter(|n| (1..=5000).contains(n))
        .ok_or_else(|| "Input hold must be 1..5000 milliseconds.".into())
}
pub async fn mouse(
    link: &Link,
    data: Value,
    device: Arc<dyn Device>,
    shutdown: watch::Receiver<bool>,
) -> Result<Value, String> {
    if let Some(button) = data.get("button") {
        if data.get("dx").is_some() || data.get("dy").is_some() {
            return Err("Choose button hold or relative movement, not both.".into());
        }
        let button = button
            .as_str()
            .ok_or("Expected left, right or middle button.")?;
        let code = match button {
            "left" => VK_LBUTTON,
            "right" => VK_RBUTTON,
            "middle" => VK_MBUTTON,
            _ => return Err("Expected left, right or middle button.".into()),
        };
        let ms = hold_duration(&data)?;
        return hold(
            link,
            &data,
            device,
            shutdown,
            code,
            ms,
            json!({"button":button}),
        )
        .await;
    }
    if data.get("ms").is_some() {
        return Err("Relative movement is one delta; omit ms.".into());
    }
    let delta = |field: &str| {
        data[field]
            .as_i64()
            .filter(|n| (-2048..=2048).contains(n))
            .map(|n| n as i32)
            .ok_or_else(|| format!("{field} must be an integer within -2048..2048."))
    };
    let (dx, dy) = (delta("dx")?, delta("dy")?);
    let _reservation = device.reserve(link.process).map_err(|e| e.to_string())?;
    authorize_input(link, &data, &*device, &shutdown).await?;
    if [VK_LBUTTON, VK_RBUTTON, VK_MBUTTON]
        .iter()
        .any(|key| device.held(*key))
    {
        return Err("A mouse button is already held; movement would drag user input.".into());
    }
    device.move_mouse(dx, dy).map_err(|e| e.to_string())?;
    let result = json!({"dx":dx,"dy":dy,"submitted":true});
    let _ = link.log.record("mouse.move", result.clone());
    Ok(result)
}
async fn authorize_input(
    link: &Link,
    data: &Value,
    device: &dyn Device,
    shutdown: &watch::Receiver<bool>,
) -> Result<(), String> {
    let session = data["session"].as_str().ok_or("Expected game session.")?;
    let token = data["token"]
        .as_str()
        .filter(|s| crate::transport::valid_id(s))
        .ok_or("Expected current controller token.")?;
    if *shutdown.borrow() {
        return Err("Service is stopping.".into());
    }
    authorize(link, session, token).await?;
    if *shutdown.borrow() {
        return Err("Service is stopping.".into());
    }
    if !device.foreground(link.process) {
        return Err("GTA must be the foreground process for physical input.".into());
    }
    Ok(())
}
async fn hold(
    link: &Link,
    data: &Value,
    device: Arc<dyn Device>,
    mut shutdown: watch::Receiver<bool>,
    code: u16,
    ms: u64,
    mut result: Value,
) -> Result<Value, String> {
    let session = data["session"].as_str().ok_or("Expected game session.")?;
    let token = data["token"]
        .as_str()
        .filter(|s| crate::transport::valid_id(s))
        .ok_or("Expected current controller token.")?;
    let _reservation = device.reserve(link.process).map_err(|e| e.to_string())?;
    authorize_input(link, data, &*device, &shutdown).await?;
    if device.held(code) {
        return Err("Input is already held; preserve user input.".into());
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
            error = Some("Service stopped during input hold.");
            break;
        }
        if !device.foreground(link.process) {
            error = Some("GTA lost focus during input hold.");
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
                error = Some("Controller lease could not be verified during input hold.");
                break;
            }
            next_lease = Instant::now() + Duration::from_millis(500);
        }
        tokio::select! {
            _ = sleep(end.saturating_duration_since(Instant::now()).min(Duration::from_millis(20))) => {},
            _ = shutdown.changed() => { error=Some("Service stopped during input hold."); break; },
        }
    }
    held.release().map_err(|e| e.to_string())?;
    let elapsed = started.elapsed().as_millis();
    result["requestedMs"] = json!(ms);
    result["elapsedMs"] = json!(elapsed);
    result["released"] = json!(true);
    result["error"] = json!(error);
    let _ = link.log.record("input.completed", result.clone());
    if let Some(error) = error {
        return Err(format!("{error} Input released; inspect game state."));
    }
    Ok(result)
}
