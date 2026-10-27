use crate::{
    storage::{read_json, save_json},
    transport::{Link, MAX_REQUEST, valid_id},
};
use axum::{
    Json, Router,
    body::{Body, Bytes},
    extract::{
        ConnectInfo, DefaultBodyLimit, Path, State, WebSocketUpgrade,
        ws::{CloseFrame, Message, WebSocket},
    },
    http::{HeaderMap, StatusCode},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use futures_util::{SinkExt, StreamExt, stream::SplitSink};
use serde_json::{Value, json};
use std::{net::SocketAddr, path::PathBuf, process::Stdio, sync::Arc, time::Duration};
use subtle::ConstantTimeEq;
use tokio::{
    sync::{Mutex, watch},
    task::JoinSet,
    time::{Instant, sleep, timeout},
};

#[derive(Clone)]
struct Service {
    link: Link,
    bearer: String,
    python: PathBuf,
    child: Arc<Mutex<Option<tokio::process::Child>>>,
    shutdown: watch::Receiver<bool>,
}
struct Failure(StatusCode, String);
impl IntoResponse for Failure {
    fn into_response(self) -> Response {
        (self.0, Json(json!({"ok":false,"error":self.1}))).into_response()
    }
}
impl From<std::io::Error> for Failure {
    fn from(value: std::io::Error) -> Self {
        Self(StatusCode::BAD_REQUEST, value.to_string())
    }
}
impl From<String> for Failure {
    fn from(value: String) -> Self {
        Self(StatusCode::BAD_REQUEST, value)
    }
}
type Result<T> = std::result::Result<T, Failure>;
fn invalid(message: &str) -> Failure {
    Failure(StatusCode::BAD_REQUEST, message.into())
}
fn decode(bytes: Bytes) -> Result<Value> {
    serde_json::from_slice(&bytes).map_err(|e| invalid(&e.to_string()))
}
fn identifier(id: &str) -> Result<()> {
    if valid_id(id) {
        Ok(())
    } else {
        Err(invalid("Invalid identifier."))
    }
}

pub fn app(link: Link, token: String, python: PathBuf, shutdown: watch::Receiver<bool>) -> Router {
    let state = Service {
        link,
        bearer: format!("Bearer {token}"),
        python,
        child: Arc::new(Mutex::new(None)),
        shutdown,
    };
    Router::new()
        .route("/v1/session", get(session))
        .route("/v1/request", post(request))
        .route("/v1/files/{*path}", get(artifact))
        .route("/v1/batches/{id}", get(batch))
        .route("/v1/jobs", post(start_job))
        .route("/v1/jobs/{id}", get(job).delete(cancel_job))
        .route("/v1/ws", get(websocket))
        .route("/openapi.json", get(schema))
        .layer(DefaultBodyLimit::max(MAX_REQUEST))
        .layer(middleware::from_fn_with_state(state.clone(), security))
        .with_state(state)
}
async fn security(
    State(state): State<Service>,
    ConnectInfo(peer): ConnectInfo<SocketAddr>,
    headers: HeaderMap,
    request: axum::extract::Request,
    next: Next,
) -> Response {
    let supplied = headers
        .get("authorization")
        .map(|v| v.as_bytes())
        .unwrap_or_default();
    if !peer.ip().is_loopback()
        || headers.contains_key("origin")
        || !bool::from(supplied.ct_eq(state.bearer.as_bytes()))
    {
        return Failure(
            StatusCode::UNAUTHORIZED,
            "Local bearer authentication required.".into(),
        )
        .into_response();
    }
    next.run(request).await
}
async fn session(State(s): State<Service>) -> Result<Json<Value>> {
    Ok(Json(s.link.discovery()?))
}
async fn request(State(s): State<Service>, bytes: Bytes) -> Result<Json<Value>> {
    Ok(Json(s.link.dispatch(decode(bytes)?).await?))
}
async fn artifact(State(s): State<Service>, Path(relative): Path<String>) -> Result<Response> {
    let root = s.link.directory()?.canonicalize()?;
    let path = root
        .join(relative)
        .canonicalize()
        .map_err(|_| Failure(StatusCode::NOT_FOUND, "Artifact missing.".into()))?;
    if !path.starts_with(&root)
        || path == root
        || !matches!(
            path.extension().and_then(|s| s.to_str()),
            Some("json" | "jsonl" | "png" | "log" | "py")
        )
    {
        return Err(Failure(
            StatusCode::FORBIDDEN,
            "Artifact outside session.".into(),
        ));
    }
    if path.extension().is_some_and(|v| v == "json") {
        return Ok(Json(read_json(&path)?).into_response());
    }
    let mime = if path.extension().is_some_and(|v| v == "png") {
        "image/png"
    } else {
        "text/plain; charset=utf-8"
    };
    let file = tokio::fs::File::open(path).await?;
    Ok((
        [("content-type", mime)],
        Body::from_stream(tokio_util::io::ReaderStream::new(file)),
    )
        .into_response())
}
async fn batch(State(s): State<Service>, Path(id): Path<String>) -> Result<Json<Value>> {
    identifier(&id)?;
    let root = s.link.directory()?;
    let mut status = read_json(&root.join(format!("batch-{id}-status.json")))?;
    if status["batch"] != id {
        return Ok(Json(
            json!({"batch":id,"state":"superseded","error":"Inspect historical result files; do not replay."}),
        ));
    }
    if status["state"] == "completed" {
        let total = status["total"]
            .as_u64()
            .filter(|v| *v <= 128)
            .ok_or_else(|| invalid("Invalid batch size."))?;
        let mut results = Vec::new();
        for i in 0..total {
            results.push(read_json(&root.join(format!("result-{id}-{i}.json")))?["result"].take());
        }
        status["results"] = Value::Array(results);
    }
    Ok(Json(status))
}
async fn start_job(State(s): State<Service>, bytes: Bytes) -> Result<impl IntoResponse> {
    let data = decode(bytes)?;
    let source = PathBuf::from(
        data["script"]
            .as_str()
            .ok_or_else(|| invalid("Expected script path."))?,
    )
    .canonicalize()?;
    if !source.is_file() {
        return Err(invalid("Expected script file."));
    }
    let id = uuid::Uuid::new_v4().simple().to_string();
    let root = s.link.directory()?;
    let directory = root.join(format!("run-{id}"));
    let mut command = tokio::process::Command::new(&s.python);
    command
        .args(["-u", "-m", "gta_use", "run"])
        .arg(&source)
        .arg("--discovery")
        .arg(&s.link.root)
        .arg("--process")
        .arg(s.link.process.to_string())
        .arg("--output")
        .arg(&directory);
    for (field, flag, default) in [
        ("timeout", "--timeout", 180.0),
        ("stuckTimeout", "--stuck-timeout", 30.0),
        ("checkTimeout", "--check-timeout", 10.0),
    ] {
        let value = match data.get(field) {
            None => default,
            Some(v) => v
                .as_f64()
                .ok_or_else(|| invalid("Job limits must be numeric."))?,
        };
        if !(value > 0.0 && value <= 86400.0) {
            return Err(invalid(
                "Job limits must be positive seconds, at most 86400.",
            ));
        }
        command.arg(flag).arg(value.to_string());
    }
    let mut child = s.child.lock().await;
    if let Some(running) = child.as_mut()
        && running.try_wait()?.is_none()
    {
        return Err(Failure(
            StatusCode::CONFLICT,
            "A segment is already running.".into(),
        ));
    }
    std::fs::create_dir(&directory)?;
    let output = std::fs::File::create(root.join(format!("job-{id}.log")))?;
    command
        .stdout(Stdio::from(output.try_clone()?))
        .stderr(Stdio::from(output))
        .creation_flags(0x08000000);
    let process = command.spawn()?;
    let pid = process.id();
    *child = Some(process);
    save_json(
        &root.join(format!("job-{id}.json")),
        &json!({"id":id,"pid":pid,"directory":directory,"script":source}),
    )?;
    s.link
        .log
        .record("job.started", json!({"id":id,"pid":pid}))?;
    Ok((
        StatusCode::ACCEPTED,
        Json(json!({"id":id,"directory":directory})),
    ))
}
async fn job(State(s): State<Service>, Path(id): Path<String>) -> Result<Json<Value>> {
    identifier(&id)?;
    let directory = s.link.directory()?.join(format!("run-{id}"));
    for name in ["result.json", "progress.json", "run.json"] {
        match read_json(&directory.join(name)) {
            Ok(v) => return Ok(Json(v)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(e.into()),
        }
    }
    Err(Failure(StatusCode::NOT_FOUND, "Job missing.".into()))
}
async fn cancel_job(State(s): State<Service>, Path(id): Path<String>) -> Result<Json<Value>> {
    identifier(&id)?;
    let root = s.link.directory()?;
    let receipt = read_json(&root.join(format!("job-{id}.json")))?;
    std::fs::File::create(root.join(format!("run-{id}/cancel")))?;
    Ok(Json(json!({"id":receipt["id"],"cancelRequested":true})))
}
async fn schema() -> Json<Value> {
    Json(
        json!({"openapi":"3.1.0","info":{"title":"GTA use","version":"1"},"paths":{
            "/v1/session":{"get":{"summary":"Current game session","responses":{"200":{"description":"Discovery"}}}},
            "/v1/request":{"post":{"summary":"Versioned bridge request; discover operations with op=describe","responses":{"200":{"description":"Correlated receipt"}}}},
            "/v1/ws":{"get":{"summary":"WebSocket requests and filtered state subscriptions","responses":{"101":{"description":"Upgrade"}}}}
        }}),
    )
}
async fn websocket(State(s): State<Service>, ws: WebSocketUpgrade) -> Response {
    ws.max_message_size(MAX_REQUEST)
        .max_frame_size(MAX_REQUEST)
        .max_write_buffer_size(2 * MAX_REQUEST)
        .on_upgrade(move |socket| connection(s, socket))
}
type Sender = Arc<Mutex<SplitSink<WebSocket, Message>>>;
async fn send(sender: &Sender, value: Value) -> std::result::Result<(), String> {
    timeout(Duration::from_secs(2), async {
        sender
            .lock()
            .await
            .send(Message::Text(value.to_string().into()))
            .await
    })
    .await
    .map_err(|_| "WebSocket send timed out".to_string())?
    .map_err(|e| e.to_string())
}
async fn sample(s: Service, sender: Sender, mut query: Value, hz: f64, session: String) {
    let mut sequence = 0;
    let mut previous = None;
    loop {
        let started = Instant::now();
        query["op"] = json!("state");
        query["version"] = json!(1);
        query["session"] = json!(session);
        query["id"] = json!(uuid::Uuid::new_v4().simple().to_string());
        let response = s.link.dispatch(query.clone()).await;
        let mut response = match response {
            Ok(v) => v,
            Err(e) => {
                let _ = send(&sender, json!({"type":"error","error":e})).await;
                return;
            }
        };
        sequence += 1;
        let ok = response["ok"] == true;
        response["type"] = json!("state");
        response["sequence"] = json!(sequence);
        if send(&sender, response).await.is_err() || !ok {
            return;
        }
        // Missing status is normal until the first batch. Avoid retrying a
        // missing optional file for 200 ms on every subscription observation.
        if let Ok(root) = s.link.directory() {
            let path = root.join("batch-status.json");
            if path.exists()
                && let Ok(status) = read_json(&path)
                && previous.as_ref() != Some(&status)
            {
                if send(
                    &sender,
                    json!({"type":"batch","session":session,"result":status}),
                )
                .await
                .is_err()
                {
                    return;
                }
                previous = Some(status);
            }
        }
        sleep(Duration::from_secs_f64(1.0 / hz).saturating_sub(started.elapsed())).await;
    }
}
async fn connection(mut s: Service, socket: WebSocket) {
    let (writer, mut reader) = socket.split();
    let sender = Arc::new(Mutex::new(writer));
    let mut subscription: Option<tokio::task::JoinHandle<()>> = None;
    let mut pending = JoinSet::new();
    loop {
        let next = tokio::select! {
            _=s.shutdown.changed()=>break,
            result=pending.join_next(),if !pending.is_empty()=>{let _=result;continue;},
            message=reader.next()=>message,
        };
        let bytes = match next {
            Some(Ok(Message::Text(v))) => v.as_bytes().to_vec(),
            Some(Ok(Message::Binary(v))) => v.to_vec(),
            Some(Ok(Message::Close(_))) | None | Some(Err(_)) => break,
            _ => continue,
        };
        let mut message = match serde_json::from_slice::<Value>(&bytes) {
            Ok(v) if v.is_object() => v,
            _ => {
                let _ = send(
                    &sender,
                    json!({"type":"error","error":"Expected JSON object."}),
                )
                .await;
                break;
            }
        };
        match message["type"].as_str() {
            Some("subscribe") => {
                let hz = message.get("hz").map_or(Some(5.0), Value::as_f64);
                let query = message.get("query").cloned().unwrap_or_else(|| json!({}));
                let session = message["session"].as_str();
                if !hz.is_some_and(|h| h > 0.0 && h <= 10.0)
                    || !query.is_object()
                    || session.is_none()
                {
                    if send(&sender,json!({"type":"error","error":"Subscription requires session, query object and 0 < hz <= 10."})).await.is_err() {break;}
                    continue;
                }
                if let Some(task) = subscription.take() {
                    task.abort();
                    let _ = task.await;
                }
                if send(
                    &sender,
                    json!({"type":"subscribed","session":session,"hz":hz}),
                )
                .await
                .is_err()
                {
                    break;
                }
                subscription = Some(tokio::spawn(sample(
                    s.clone(),
                    sender.clone(),
                    query,
                    hz.unwrap(),
                    session.unwrap().into(),
                )));
            }
            Some("request") => {
                if pending.len() >= 16 {
                    let _=send(&sender,json!({"type":"response","id":message["id"],"session":message["session"],"ok":false,"error":"Too many outstanding requests."})).await;
                    continue;
                }
                let link = s.link.clone();
                let sender = sender.clone();
                message.as_object_mut().unwrap().remove("type");
                pending.spawn(async move {
                    let id = message["id"].clone();
                    let mut response = match link.dispatch(message).await {
                        Ok(v) => v,
                        Err(e) => json!({"id":id,"ok":false,"error":e}),
                    };
                    response["type"] = json!("response");
                    let _ = send(&sender, response).await;
                });
            }
            _ => {
                if send(
                    &sender,
                    json!({"type":"error","error":"Expected request or subscribe."}),
                )
                .await
                .is_err()
                {
                    break;
                }
            }
        }
    }
    if let Some(task) = subscription {
        task.abort();
        let _ = task.await;
    }
    pending.abort_all();
    let _ = timeout(Duration::from_secs(2), async {
        sender
            .lock()
            .await
            .send(Message::Close(Some(CloseFrame {
                code: 1001,
                reason: "Connection ended".into(),
            })))
            .await
    })
    .await;
}

#[cfg(test)]
#[path = "scenarios.rs"]
mod scenarios;
