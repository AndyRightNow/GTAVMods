//! Real HTTP/WebSocket and pipe scenarios; only the external game's observations
//! are supplied. No implementation collaborator is replaced.
use super::*;
use crate::storage::Journal;
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::windows::named_pipe::ServerOptions,
    sync::{mpsc, oneshot},
};
use tokio_tungstenite::{connect_async, tungstenite::client::IntoClientRequest};

struct Scene {
    root: tempfile::TempDir,
    _shutdown: watch::Sender<bool>,
    url: String,
    client: reqwest::Client,
    observations: mpsc::Receiver<(Value, oneshot::Sender<Value>)>,
    tasks: Vec<tokio::task::JoinHandle<()>>,
}
impl Drop for Scene {
    fn drop(&mut self) {
        for task in &self.tasks {
            task.abort();
        }
    }
}
impl Scene {
    async fn start() -> Self {
        Self::start_with_rearm_delay(Duration::ZERO).await
    }
    async fn start_with_rearm_delay(rearm_delay: Duration) -> Self {
        Self::start_device(rearm_delay, Arc::new(crate::physical_input::WindowsInput)).await
    }
    async fn start_device(
        rearm_delay: Duration,
        device: Arc<dyn crate::physical_input::Device>,
    ) -> Self {
        let root = tempfile::tempdir_in(
            std::env::var_os("GTA_USE_TEST_ROOT")
                .map(PathBuf::from)
                .unwrap_or_else(std::env::temp_dir),
        )
        .unwrap();
        let pipe = format!("GTAVMods-Rust-Test-{}", uuid::Uuid::new_v4().simple());
        save_json(
            &root.path().join("bridge-1.json"),
            &json!({"pipe":pipe,"bridge":{"session":"session-one","directory":root.path()}}),
        )
        .unwrap();
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let url = format!("http://{}", listener.local_addr().unwrap());
        let journal = Arc::new(Journal::new(root.path().join("service.jsonl")).unwrap());
        let link = Link::start(root.path().into(), 1, journal);
        let (shutdown_sender, shutdown) = watch::channel(false);
        let app = app_with_input(
            link,
            "scenario-token".into(),
            "python.exe".into(),
            shutdown,
            device,
        );
        let http = tokio::spawn(async move {
            axum::serve(
                listener,
                app.into_make_service_with_connect_info::<SocketAddr>(),
            )
            .await
            .unwrap();
        });
        let (sender, observations) = mpsc::channel(8);
        let pipe_task = tokio::spawn(async move {
            loop {
                let mut server = ServerOptions::new()
                    .create(format!(r"\\.\pipe\{pipe}"))
                    .unwrap();
                server.connect().await.unwrap();
                let count = server.read_u32_le().await.unwrap();
                let mut bytes = vec![0; count as usize];
                server.read_exact(&mut bytes).await.unwrap();
                let request: Value = serde_json::from_slice(&bytes).unwrap();
                let (reply, result) = oneshot::channel();
                sender.send((request, reply)).await.unwrap();
                let response = serde_json::to_vec(&result.await.unwrap()).unwrap();
                let _ = server.write_u32_le(response.len() as u32).await;
                let _ = server.write_all(&response).await;
                // NamedPipe disconnect discards unread bytes. Wait for the
                // client to close after consuming its entire response.
                let _ = server.read_u8().await;
                sleep(rearm_delay).await;
            }
        });
        Self {
            root,
            url,
            client: reqwest::Client::new(),
            observations,
            tasks: vec![http, pipe_task],
            _shutdown: shutdown_sender,
        }
    }
    fn get(&self, path: &str) -> reqwest::RequestBuilder {
        self.client
            .get(format!("{}{path}", self.url))
            .bearer_auth("scenario-token")
    }
    fn post(&self, value: &Value) -> reqwest::RequestBuilder {
        self.client
            .post(format!("{}/v1/request", self.url))
            .bearer_auth("scenario-token")
            .json(value)
    }
}
fn envelope(op: &str) -> Value {
    json!({"version":1,"id":uuid::Uuid::new_v4().simple().to_string(),"session":"session-one","op":op})
}

#[tokio::test]
async fn a_following_action_waits_for_the_game_to_accept_a_new_connection() {
    let mut s = Scene::start_with_rearm_delay(Duration::from_millis(150)).await;
    for index in 0..2 {
        let action = envelope("batch");
        let http = s.post(&action);
        let response =
            tokio::spawn(async move { http.send().await.unwrap().json::<Value>().await.unwrap() });
        if index == 1 {
            assert!(
                timeout(Duration::from_millis(30), s.observations.recv())
                    .await
                    .is_err()
            );
            assert!(!response.is_finished());
        }
        let (observed, reply) = timeout(Duration::from_secs(2), s.observations.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(observed, action);
        reply
            .send(json!({"ok":true,"id":action["id"],"result":{"accepted":index}}))
            .unwrap();
        let receipt = response.await.unwrap();
        assert_eq!(receipt["result"]["accepted"], index);
    }
    assert!(
        timeout(Duration::from_millis(200), s.observations.recv())
            .await
            .is_err()
    );
}

#[tokio::test]
async fn a_stalled_game_returns_an_uncertain_receipt_without_replaying_the_action() {
    let mut s = Scene::start().await;
    let action = envelope("batch");
    let http = s.post(&action);
    let response =
        tokio::spawn(async move { http.send().await.unwrap().json::<Value>().await.unwrap() });
    let (observed, reply) = s.observations.recv().await.unwrap();
    assert_eq!(observed, action);
    // Keep the engine response pending past the service deadline. A timeout
    // cannot establish whether the game already executed this mutation.
    let receipt = timeout(Duration::from_secs(6), response)
        .await
        .unwrap()
        .unwrap();
    assert_eq!(receipt["id"], action["id"]);
    assert_eq!(receipt["uncertain"], true);
    assert!(
        receipt["error"]
            .as_str()
            .unwrap()
            .contains("inspect game state")
    );
    reply.send(json!({"ok":true,"id":action["id"]})).unwrap();
    assert!(
        timeout(Duration::from_millis(100), s.observations.recv())
            .await
            .is_err()
    );
    assert_eq!(s.get("/v1/session").send().await.unwrap().status(), 200);
}

#[tokio::test]
async fn browser_origins_and_invalid_requests_cannot_act_on_the_game() {
    let mut s = Scene::start().await;
    assert_eq!(
        s.client
            .get(format!("{}/v1/session", s.url))
            .send()
            .await
            .unwrap()
            .status(),
        401
    );
    assert_eq!(
        s.post(&envelope("batch"))
            .header("origin", "https://example.com")
            .send()
            .await
            .unwrap()
            .status(),
        401
    );
    assert_eq!(s.post(&json!([])).send().await.unwrap().status(), 400);
    assert_eq!(
        s.post(&json!({"id":"../outside"}))
            .send()
            .await
            .unwrap()
            .status(),
        400
    );
    assert_eq!(
        s.post(&json!({"id":"0".repeat(32),"payload":"x".repeat(MAX_REQUEST)}))
            .send()
            .await
            .unwrap()
            .status(),
        413
    );
    assert!(s.observations.try_recv().is_err());
    let discovery: Value = s
        .get("/v1/session")
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(discovery["bridge"]["session"], "session-one");
}

#[tokio::test]
async fn a_disconnected_agent_does_not_overlap_or_replay_an_inflight_action() {
    let mut s = Scene::start().await;
    let request = envelope("batch");
    let http = s.post(&request);
    let first = tokio::spawn(async move { http.send().await });
    let (observed, reply) = timeout(Duration::from_secs(5), s.observations.recv())
        .await
        .unwrap()
        .unwrap();
    assert_eq!(observed, request);
    first.abort();
    let _ = first.await;
    let next = envelope("status");
    let http = s.post(&next);
    let second =
        tokio::spawn(async move { http.send().await.unwrap().json::<Value>().await.unwrap() });
    assert!(
        timeout(Duration::from_millis(100), s.observations.recv())
            .await
            .is_err()
    );
    reply
        .send(json!({"id":request["id"],"ok":true,"result":{"accepted":true}}))
        .unwrap();
    let (observed, reply) = timeout(Duration::from_secs(5), s.observations.recv())
        .await
        .unwrap()
        .unwrap();
    assert_eq!(observed, next);
    reply
        .send(json!({"id":next["id"],"ok":true,"result":{"paused":true}}))
        .unwrap();
    let result = second.await.unwrap();
    assert_eq!(result["id"], next["id"]);
    assert_eq!(result["result"]["paused"], true);
    assert!(s.observations.try_recv().is_err());
}

#[tokio::test]
async fn artifact_inspection_survives_session_replacement_and_confines_downloads() {
    let s = Scene::start().await;
    std::fs::write(s.root.path().join("frame.png"), b"observed pixels").unwrap();
    std::fs::write(s.root.path().join("private.exe"), b"not an artifact").unwrap();
    assert_eq!(
        &s.get("/v1/files/frame.png")
            .send()
            .await
            .unwrap()
            .bytes()
            .await
            .unwrap()[..],
        b"observed pixels"
    );
    assert_eq!(
        s.get("/v1/files/private.exe")
            .send()
            .await
            .unwrap()
            .status(),
        403
    );
    assert_eq!(
        s.get("/v1/files/missing.png")
            .send()
            .await
            .unwrap()
            .status(),
        404
    );
    let outside = tempfile::NamedTempFile::new_in(s.root.path().parent().unwrap()).unwrap();
    let encoded = outside
        .path()
        .display()
        .to_string()
        .replace('\\', "%5C")
        .replace(':', "%3A");
    assert_eq!(
        s.get(&format!("/v1/files/{encoded}"))
            .send()
            .await
            .unwrap()
            .status(),
        403
    );
    let next = s.root.path().join("reloaded");
    std::fs::create_dir(&next).unwrap();
    save_json(
        &s.root.path().join("bridge-1.json"),
        &json!({"bridge":{"session":"session-two","directory":next}}),
    )
    .unwrap();
    let discovery: Value = s
        .get("/v1/session")
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(discovery["bridge"]["session"], "session-two");
    assert_eq!(
        s.get("/v1/files/frame.png").send().await.unwrap().status(),
        404
    );
}

#[tokio::test]
async fn a_temporarily_locked_batch_result_is_read_without_resubmitting_the_action() {
    use std::os::windows::fs::OpenOptionsExt;
    let mut s = Scene::start().await;
    let id = "a".repeat(32);
    let status = s.root.path().join(format!("batch-{id}-status.json"));
    std::fs::write(
        &status,
        json!({"batch":id,"state":"completed","total":1}).to_string(),
    )
    .unwrap();
    std::fs::write(
        s.root.path().join(format!("result-{id}-0.json")),
        r#"{"result":{"paused":true}}"#,
    )
    .unwrap();
    // ReplaceFile/scanners can briefly deny sharing. This is a real Windows
    // handle, not a mocked ErrorKind (sharing violations are not PermissionDenied).
    let held = std::fs::OpenOptions::new()
        .read(true)
        .share_mode(0)
        .open(&status)
        .unwrap();
    let unlock = std::thread::spawn(move || {
        std::thread::sleep(Duration::from_millis(75));
        drop(held);
    });
    let response = s.get(&format!("/v1/batches/{id}")).send().await.unwrap();
    unlock.join().unwrap();
    assert_eq!(response.status(), 200);
    let result: Value = response.json().await.unwrap();
    assert_eq!(result["results"], json!([{"paused":true}]));
    assert!(s.observations.try_recv().is_err());
}

#[tokio::test]
async fn an_agent_can_inspect_an_earlier_batch_after_the_game_starts_another() {
    let mut s = Scene::start().await;
    let id = "b".repeat(32);
    save_json(
        &s.root.path().join(format!("batch-{id}-status.json")),
        &json!({"batch":id,"state":"completed","total":1}),
    )
    .unwrap();
    save_json(
        &s.root.path().join(format!("result-{id}-0.json")),
        &json!({"result":{"paused":true}}),
    )
    .unwrap();
    save_json(
        &s.root.path().join("batch-status.json"),
        &json!({"batch":"c".repeat(32),"state":"running"}),
    )
    .unwrap();
    let result: Value = s
        .get(&format!("/v1/batches/{id}"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(result["batch"], id);
    assert_eq!(result["state"], "completed");
    assert_eq!(result["results"], json!([{"paused":true}]));
    assert_eq!(
        s.get(&format!("/v1/batches/{}", "d".repeat(32)))
            .send()
            .await
            .unwrap()
            .status(),
        400
    );
    assert!(s.observations.try_recv().is_err());
}

#[tokio::test]
async fn websocket_control_is_correlated_while_a_filtered_subscription_is_active() {
    let mut s = Scene::start().await;
    let mut upgrade = format!("{}/v1/ws", s.url.replace("http:", "ws:"))
        .into_client_request()
        .unwrap();
    upgrade
        .headers_mut()
        .insert("authorization", "Bearer scenario-token".parse().unwrap());
    let (mut ws, _) = connect_async(upgrade).await.unwrap();
    // A mistaken subscription must be rejected without losing the connection
    // needed to correct it and inspect the game.
    for invalid in [
        json!({"type":"subscribe","session":"session-one","hz":100}),
        json!({"type":"unknown"}),
    ] {
        ws.send(tokio_tungstenite::tungstenite::Message::Text(
            invalid.to_string().into(),
        ))
        .await
        .unwrap();
        let rejected: Value =
            serde_json::from_slice(&ws.next().await.unwrap().unwrap().into_data()).unwrap();
        assert_eq!(rejected["type"], "error");
    }
    assert!(s.observations.try_recv().is_err());
    ws.send(tokio_tungstenite::tungstenite::Message::Text(
        json!({"type":"subscribe","session":"session-one","hz":5,"query":{"sections":["player"],"limit":5}})
            .to_string()
            .into(),
    ))
    .await
    .unwrap();
    let ack: Value =
        serde_json::from_slice(&ws.next().await.unwrap().unwrap().into_data()).unwrap();
    assert_eq!(ack["type"], "subscribed");
    let (state, reply) = s.observations.recv().await.unwrap();
    assert_eq!(state["sections"], json!(["player"]));
    assert_eq!(state["limit"], 5);
    reply
        .send(json!({"id":state["id"],"ok":true,"result":{"gameTime":150,"health":175}}))
        .unwrap();
    let observed: Value =
        serde_json::from_slice(&ws.next().await.unwrap().unwrap().into_data()).unwrap();
    assert_eq!(observed["result"]["health"], 175);
    let mut request = envelope("status");
    request["type"] = json!("request");
    ws.send(tokio_tungstenite::tungstenite::Message::Text(
        request.to_string().into(),
    ))
    .await
    .unwrap();
    let (control, reply) = s.observations.recv().await.unwrap();
    assert_eq!(control["op"], "status");
    reply
        .send(json!({"id":control["id"],"ok":true,"result":{"paused":true}}))
        .unwrap();
    let receipt: Value =
        serde_json::from_slice(&ws.next().await.unwrap().unwrap().into_data()).unwrap();
    assert_eq!(receipt["type"], "response");
    assert_eq!(receipt["id"], request["id"]);
    ws.close(None).await.unwrap();
}

#[derive(Default)]
struct KeyboardObservation {
    focus_lost: std::sync::atomic::AtomicBool,
    physical_held: std::sync::atomic::AtomicBool,
    events: std::sync::Mutex<Vec<(u16, bool)>>,
    moves: std::sync::Mutex<Vec<(i32, i32)>>,
}
impl crate::physical_input::Device for KeyboardObservation {
    fn reserve(&self, _: u32) -> std::io::Result<Box<dyn Send>> {
        Ok(Box::new(()))
    }
    fn foreground(&self, process: u32) -> bool {
        process == 1 && !self.focus_lost.load(std::sync::atomic::Ordering::SeqCst)
    }
    fn held(&self, _: u16) -> bool {
        self.physical_held.load(std::sync::atomic::Ordering::SeqCst)
    }
    fn move_mouse(&self, dx: i32, dy: i32) -> std::io::Result<()> {
        self.moves.lock().unwrap().push((dx, dy));
        Ok(())
    }
    fn send(&self, key: u16, down: bool) -> std::io::Result<()> {
        self.events.lock().unwrap().push((key, down));
        Ok(())
    }
}
fn keyboard_request(s: &Scene, ms: u64) -> reqwest::RequestBuilder {
    s.client.post(format!("{}/v1/keyboard",s.url)).bearer_auth("scenario-token")
        .json(&json!({"session":"session-one","token":"11111111111111111111111111111111","key":"H","ms":ms}))
}
async fn accept_keyboard_lease(s: &mut Scene, ok: bool) {
    let (request, reply) = timeout(Duration::from_secs(2), s.observations.recv())
        .await
        .unwrap()
        .unwrap();
    assert_eq!(request["op"], "lease");
    assert_eq!(request["action"], "heartbeat");
    assert_eq!(request["token"], "11111111111111111111111111111111");
    reply
        .send(json!({"id":request["id"],"session":request["session"],"ok":ok}))
        .unwrap();
}
#[tokio::test]
async fn held_game_key_releases_at_deadline_and_rejects_overlapping_holds() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = keyboard_request(&s, 180);
    let response =
        tokio::spawn(async move { request.send().await.unwrap().json::<Value>().await.unwrap() });
    accept_keyboard_lease(&mut s, true).await;
    sleep(Duration::from_millis(30)).await;
    assert_eq!(keyboard_request(&s, 20).send().await.unwrap().status(), 400);
    let receipt = response.await.unwrap();
    assert_eq!(receipt["released"], true);
    assert!(receipt["elapsedMs"].as_u64().unwrap() >= 180);
    assert_eq!(
        *device.events.lock().unwrap(),
        vec![(0x48, true), (0x48, false)]
    );
}
#[tokio::test]
async fn focus_loss_releases_the_key_before_its_requested_duration() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = keyboard_request(&s, 3000);
    let response =
        tokio::spawn(async move { request.send().await.unwrap().json::<Value>().await.unwrap() });
    accept_keyboard_lease(&mut s, true).await;
    sleep(Duration::from_millis(30)).await;
    device
        .focus_lost
        .store(true, std::sync::atomic::Ordering::SeqCst);
    let receipt = timeout(Duration::from_secs(1), response)
        .await
        .unwrap()
        .unwrap();
    assert!(receipt["error"].as_str().unwrap().contains("lost focus"));
    assert_eq!(
        *device.events.lock().unwrap(),
        vec![(0x48, true), (0x48, false)]
    );
}
#[tokio::test]
async fn a_rejected_lease_never_sends_keyboard_input() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = keyboard_request(&s, 80);
    let response = tokio::spawn(async move { request.send().await.unwrap() });
    accept_keyboard_lease(&mut s, false).await;
    assert_eq!(response.await.unwrap().status(), 400);
    assert!(device.events.lock().unwrap().is_empty());
}
#[tokio::test]
async fn a_physically_held_key_is_preserved_without_injection_or_release() {
    let device = Arc::new(KeyboardObservation::default());
    device
        .physical_held
        .store(true, std::sync::atomic::Ordering::SeqCst);
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = keyboard_request(&s, 80);
    let response = tokio::spawn(async move { request.send().await.unwrap() });
    accept_keyboard_lease(&mut s, true).await;
    assert_eq!(response.await.unwrap().status(), 400);
    assert!(device.events.lock().unwrap().is_empty());
}
#[tokio::test]
async fn shutdown_releases_a_held_key_without_waiting_for_the_game() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = keyboard_request(&s, 3000);
    let response = tokio::spawn(async move { request.send().await.unwrap() });
    accept_keyboard_lease(&mut s, true).await;
    sleep(Duration::from_millis(30)).await;
    s._shutdown.send(true).unwrap();
    assert_eq!(
        timeout(Duration::from_secs(1), response)
            .await
            .unwrap()
            .unwrap()
            .status(),
        400
    );
    assert_eq!(
        *device.events.lock().unwrap(),
        vec![(0x48, true), (0x48, false)]
    );
}
#[tokio::test]
async fn lost_lease_during_hold_releases_without_resending_key_down() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = keyboard_request(&s, 3000);
    let response = tokio::spawn(async move { request.send().await.unwrap() });
    accept_keyboard_lease(&mut s, true).await;
    accept_keyboard_lease(&mut s, false).await;
    assert_eq!(response.await.unwrap().status(), 400);
    assert_eq!(
        *device.events.lock().unwrap(),
        vec![(0x48, true), (0x48, false)]
    );
}
#[tokio::test]
async fn excessive_key_hold_is_rejected_before_contacting_the_game() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    assert_eq!(
        keyboard_request(&s, 5001).send().await.unwrap().status(),
        400
    );
    assert!(device.events.lock().unwrap().is_empty());
    assert!(
        timeout(Duration::from_millis(50), s.observations.recv())
            .await
            .is_err()
    );
}

fn mouse_request(s: &Scene, mut data: Value) -> reqwest::RequestBuilder {
    data["session"] = json!("session-one");
    data["token"] = json!("11111111111111111111111111111111");
    s.client
        .post(format!("{}/v1/mouse", s.url))
        .bearer_auth("scenario-token")
        .json(&data)
}
#[tokio::test]
async fn photo_shutter_releases_without_moving_the_camera_or_overlapping_keyboard() {
    for (button, code) in [("left", 1), ("right", 2), ("middle", 4)] {
        let device = Arc::new(KeyboardObservation::default());
        let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
        let request = mouse_request(&s, json!({"button":button,"ms":180}));
        let response =
            tokio::spawn(
                async move { request.send().await.unwrap().json::<Value>().await.unwrap() },
            );
        accept_keyboard_lease(&mut s, true).await;
        sleep(Duration::from_millis(30)).await;
        assert_eq!(keyboard_request(&s, 20).send().await.unwrap().status(), 400);
        assert_eq!(
            mouse_request(&s, json!({"dx":3,"dy":0}))
                .send()
                .await
                .unwrap()
                .status(),
            400
        );
        let receipt = response.await.unwrap();
        assert_eq!(receipt["released"], true);
        assert!(receipt["elapsedMs"].as_u64().unwrap() >= 180);
        assert_eq!(
            *device.events.lock().unwrap(),
            vec![(code, true), (code, false)]
        );
        assert!(device.moves.lock().unwrap().is_empty());
    }
}
#[tokio::test]
async fn relative_camera_motion_is_one_signed_delta_with_no_button_or_absolute_position() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    let request = mouse_request(&s, json!({"dx":-12,"dy":7}));
    let response =
        tokio::spawn(async move { request.send().await.unwrap().json::<Value>().await.unwrap() });
    accept_keyboard_lease(&mut s, true).await;
    assert_eq!(
        response.await.unwrap(),
        json!({"dx":-12,"dy":7,"submitted":true})
    );
    assert_eq!(*device.moves.lock().unwrap(), vec![(-12, 7)]);
    assert!(device.events.lock().unwrap().is_empty());
}
#[tokio::test]
async fn mouse_inputs_preserve_user_buttons_and_reject_background_or_unowned_game() {
    for cause in ["focus", "user", "lease", "shutdown"] {
        for input in [json!({"dx":8,"dy":-2}), json!({"button":"left","ms":80})] {
            let device = Arc::new(KeyboardObservation::default());
            device
                .focus_lost
                .store(cause == "focus", std::sync::atomic::Ordering::SeqCst);
            device
                .physical_held
                .store(cause == "user", std::sync::atomic::Ordering::SeqCst);
            let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
            let request = mouse_request(&s, input);
            if cause == "shutdown" {
                s._shutdown.send(true).unwrap();
            }
            let response = tokio::spawn(async move { request.send().await.unwrap() });
            if cause != "shutdown" {
                accept_keyboard_lease(&mut s, cause != "lease").await;
            }
            assert_eq!(response.await.unwrap().status(), 400, "{cause}");
            assert!(device.events.lock().unwrap().is_empty(), "{cause}");
            assert!(device.moves.lock().unwrap().is_empty(), "{cause}");
        }
    }
}
#[tokio::test]
async fn interrupted_mouse_hold_releases_once_and_never_repeats_the_shutter() {
    for cause in ["focus", "shutdown", "lease"] {
        let device = Arc::new(KeyboardObservation::default());
        let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
        let request = mouse_request(&s, json!({"button":"left","ms":3000}));
        let response = tokio::spawn(async move { request.send().await.unwrap() });
        accept_keyboard_lease(&mut s, true).await;
        sleep(Duration::from_millis(30)).await;
        match cause {
            "focus" => device
                .focus_lost
                .store(true, std::sync::atomic::Ordering::SeqCst),
            "shutdown" => {
                s._shutdown.send(true).unwrap();
            }
            _ => accept_keyboard_lease(&mut s, false).await,
        }
        assert_eq!(
            timeout(Duration::from_secs(1), response)
                .await
                .unwrap()
                .unwrap()
                .status(),
            400,
            "{cause}"
        );
        assert_eq!(
            *device.events.lock().unwrap(),
            vec![(1, true), (1, false)],
            "{cause}"
        );
        assert!(device.moves.lock().unwrap().is_empty());
    }
}
#[tokio::test]
async fn ambiguous_or_unbounded_mouse_input_is_rejected_before_game_contact() {
    let device = Arc::new(KeyboardObservation::default());
    let mut s = Scene::start_device(Duration::ZERO, device.clone()).await;
    for data in [
        json!({"dx":2049,"dy":0}),
        json!({"dx":0,"dy":-2049}),
        json!({"dx":1.5,"dy":0}),
        json!({"dx":true,"dy":0}),
        json!({"dx":0}),
        json!({"button":"other","ms":80}),
        json!({"button":"left","ms":0}),
        json!({"button":"left","ms":5001}),
        json!({"button":"left","ms":80,"dx":0}),
        json!({"dx":0,"dy":0,"ms":80}),
    ] {
        assert_eq!(mouse_request(&s, data).send().await.unwrap().status(), 400);
    }
    assert!(device.moves.lock().unwrap().is_empty());
    assert!(device.events.lock().unwrap().is_empty());
    assert!(
        timeout(Duration::from_millis(50), s.observations.recv())
            .await
            .is_err()
    );
}
