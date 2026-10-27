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
        let app = app(link, "scenario-token".into(), "python.exe".into(), shutdown);
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
