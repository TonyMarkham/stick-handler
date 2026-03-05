# Camera Start/Stop API — Implementation Plan

## Overview

Add `POST /camera/start`, `POST /camera/stop`, and `GET /camera/status` HTTP endpoints so the camera subprocess can be controlled at runtime without restarting the server.

**Files that change / are created:**
- `backend/server/src/camera_handle.rs` — **new** — `CameraHandle` struct
- `backend/server/src/app_state.rs` — **new** — `AppState` struct + `spawn_camera_monitor`
- `backend/server/src/camera.rs` — remove `CameraHandle`, import from `camera_handle`
- `backend/server/src/main.rs` — declare new modules, add routes + handlers, shutdown cleanup
- `backend/server/src/webrtc_handler.rs` — **no changes**

---

## Step 1 — `camera_handle.rs` (new file)

Contains `CameraHandle` and its `stop()` implementation. Nothing else.

```rust
// backend/server/src/camera_handle.rs

use bytes::Bytes;
use std::sync::{Arc, Mutex as StdMutex};
use tokio::sync::broadcast;

/// Handle to a running camera instance.
///
/// Call [`stop`] to kill the subprocess. The [`JoinHandle`] returned alongside
/// this by [`crate::camera::start_camera`] resolves when the blocking read task
/// exits — use it to detect unexpected crashes and update shared state.
///
/// [`stop`]: CameraHandle::stop
pub struct CameraHandle {
    pub(crate) sender: broadcast::Sender<Bytes>,
    pub(crate) child: Arc<StdMutex<tokio::process::Child>>,
}

impl CameraHandle {
    pub(crate) fn subscribe(&self) -> broadcast::Receiver<Bytes> {
        self.sender.subscribe()
    }

    /// Send SIGKILL to the `rpicam-vid` subprocess.
    ///
    /// The blocking task observes EOF on its next `next_nal()` call, exits,
    /// drops its `Arc` clone of the child, and the broadcast channel closes.
    /// All active [`broadcast::Receiver`] subscribers receive `RecvError::Closed`.
    pub fn stop(self) {
        let mut child = match self.child.lock() {
            Ok(guard) => guard,
            Err(poisoned) => {
                tracing::warn!("Camera mutex was poisoned; attempting kill anyway");
                poisoned.into_inner()
            }
        };
        if let Err(e) = child.start_kill() {
            tracing::warn!("Failed to send SIGKILL to camera process: {e}");
        }
    }
}
```

**Why `Arc<StdMutex<Child>>` and not `Arc<TokioMutex<Child>>`:** `stop()` is synchronous. The critical section is a single non-blocking `start_kill()` call. The blocking task never locks the mutex — it only holds an `Arc` clone for keepalive. No risk of holding `StdMutex` across an `.await`.

**Why `start_kill()` and not `kill()`:** `tokio::process::Child::kill()` is `async`. `start_kill()` sends SIGKILL synchronously without awaiting exit. The blocking task observes EOF on the next `next_nal()` call and exits cleanly.

---

## Step 2 — `app_state.rs` (new file)

Contains `AppState` and `spawn_camera_monitor`. The monitor is the mechanism that keeps `AppState.camera` accurate when `rpicam-vid` crashes on its own — without it, `AppState.camera` would remain `Some(dead_handle)` and `/camera/start` would return 409 incorrectly.

```rust
// backend/server/src/app_state.rs

use crate::camera_handle::CameraHandle;
use std::sync::Arc;
use tokio::{sync::Mutex as TokioMutex, task::JoinHandle};

#[derive(Clone)]
pub struct AppState {
    pub camera: Arc<TokioMutex<Option<CameraHandle>>>,
    pub stun_urls: Vec<String>,
}

impl AppState {
    pub fn new(stun_urls: Vec<String>) -> Self {
        Self {
            camera: Arc::new(TokioMutex::new(None)),
            stun_urls,
        }
    }
}

/// Spawn a task that watches the camera blocking task and clears `AppState.camera`
/// if it exits for any reason other than an explicit [`CameraHandle::stop`] call.
///
/// This prevents zombie state: `AppState.camera = Some(dead_handle)` after a crash,
/// which would cause `/camera/start` to incorrectly return 409.
pub fn spawn_camera_monitor(
    task: JoinHandle<()>,
    camera: Arc<TokioMutex<Option<CameraHandle>>>,
) {
    tokio::spawn(async move {
        let _ = task.await;
        let mut guard = camera.lock().await;
        if guard.is_some() {
            // Task exited but stop() was not called — subprocess crashed
            *guard = None;
            tracing::warn!("Camera subprocess exited unexpectedly; state cleared");
        }
        // If guard is already None, stop() already took the handle — nothing to do
    });
}
```

---

## Step 3 — `camera.rs` (modify)

Remove `CameraHandle` from this file entirely. Import it from `crate::camera_handle`. The only change is the import and the updated return type — the subprocess logic is untouched.

**Diff:**

```rust
// Add at top
use crate::camera_handle::CameraHandle;

// Add to existing tokio import
use tokio::{process::Command, sync::broadcast, task::JoinHandle};

// Remove the CameraHandle struct and impl block (now in camera_handle.rs)

// Change start_camera return type
pub fn start_camera(
    width: u32,
    height: u32,
    framerate: u32,
) -> ServerResult<(CameraHandle, JoinHandle<()>)> {
    // ... all existing subprocess/spawn_blocking logic unchanged ...

    // Wrap child in Arc<StdMutex> for shared ownership with CameraHandle
    let child = Arc::new(StdMutex::new(child));
    let child_for_task = Arc::clone(&child);

    let task = tokio::task::spawn_blocking(move || {
        let _child_guard = child_for_task; // keeps process alive
        // ... existing NAL read loop unchanged ...
    });

    Ok((CameraHandle { sender: tx, child }, task))
}
```

Full replacement for `camera.rs` with all changes applied:

```rust
// backend/server/src/camera.rs

use crate::camera_handle::CameraHandle;
use crate::error::{ServerResult, camera_error};

use bytes::{BufMut, Bytes, BytesMut};
use std::sync::{Arc, Mutex as StdMutex};
use tokio::{process::Command, sync::broadcast, task::JoinHandle};
use tokio_util::io::SyncIoBridge;
use webrtc_media::io::h264_reader::{H264Reader, NalUnitType};

const BROADCAST_CAPACITY: usize = 64;
const H264_READER_BUF: usize = 1_048_576; // 1 MiB
const COMMAND: &str = "rpicam-vid";
const CODEC: &str = "h264";
const LIBAV_FORMAT: &str = "h264";
const LIBAV_VIDEO_CODEC: &str = "h264_v4l2m2m";
const TIMEOUT: &str = "0";
const OUTPUT: &str = "-";

/// Annex B start code that precedes every NAL unit in the stream
const START_CODE: &[u8] = &[0x00, 0x00, 0x00, 0x01];

/// Spawn `rpicam-vid` and return a [`CameraHandle`] for control plus a
/// [`JoinHandle`] for lifecycle tracking.
///
/// The [`JoinHandle`] resolves when the blocking read task exits — either
/// because [`CameraHandle::stop`] was called or because the subprocess crashed.
/// Pass it to [`crate::app_state::spawn_camera_monitor`] to keep shared state accurate.
pub fn start_camera(
    width: u32,
    height: u32,
    framerate: u32,
) -> ServerResult<(CameraHandle, JoinHandle<()>)> {
    let mut child = Command::new(COMMAND)
        .args([
            "--codec",
            CODEC,
            "--libav-format",
            LIBAV_FORMAT,
            "--libav-video-codec",
            LIBAV_VIDEO_CODEC,
            "--width",
            &width.to_string(),
            "--height",
            &height.to_string(),
            "--framerate",
            &framerate.to_string(),
            "--inline",
            "--intra",
            "30",
            "--timeout",
            TIMEOUT,
            "--output",
            OUTPUT,
        ])
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .spawn()
        .map_err(|e| camera_error(format!("Failed to spawn {COMMAND}: {e}")))?;

    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| camera_error(format!("{COMMAND} stdout not captured")))?;

    let child = Arc::new(StdMutex::new(child));
    let child_for_task = Arc::clone(&child);

    let (tx, _) = broadcast::channel(BROADCAST_CAPACITY);
    let tx_clone = tx.clone();

    let task = tokio::task::spawn_blocking(move || {
        // Keep child alive for the duration of the blocking task.
        // When this guard drops, the Arc refcount falls and the Child is freed.
        let _child_guard = child_for_task;

        let bridge = SyncIoBridge::new(stdout);
        let mut reader = H264Reader::new(bridge, H264_READER_BUF);
        let mut access_unit = BytesMut::new();

        loop {
            match reader.next_nal() {
                Ok(nal) => {
                    let unit_type = nal.unit_type;

                    access_unit.put_slice(START_CODE);
                    access_unit.put_slice(&nal.data);

                    match unit_type {
                        NalUnitType::CodedSliceIdr | NalUnitType::CodedSliceNonIdr => {
                            let frame = access_unit.split().freeze();
                            tracing::trace!("access unit: {} bytes", frame.len());
                            let _ = tx_clone.send(frame);
                        }
                        _ => {}
                    }
                }
                Err(e) => {
                    tracing::error!("H264Reader error: {e}");
                    break;
                }
            }
        }
    });

    Ok((CameraHandle { sender: tx, child }, task))
}
```

---

## Step 4 — `main.rs` (modify)

Declare the two new modules, wire up routes, add handlers, and stop the camera after `axum::serve` returns.

```rust
// backend/server/src/main.rs

mod app_state;
mod camera;
mod camera_handle;
pub mod error;
mod webrtc_handler;

use app_state::{AppState, spawn_camera_monitor};
use axum::{
    Json, Router,
    extract::{
        ws::{Message, WebSocket},
        {State, WebSocketUpgrade},
    },
    http::StatusCode,
    response::{Html, IntoResponse, Response},
    routing::{get, post},
};
use futures_util::StreamExt;
use serde::Serialize;
use signal_server::SignalMessage;
use std::sync::Arc;
use tokio::sync::mpsc;

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt()
        .with_env_filter("info,server=debug")
        .init();

    let state = Arc::new(AppState::new(vec![
        "stun:stun.l.google.com:19302".to_owned(),
    ]));

    // Camera starts idle — use POST /camera/start to begin streaming.

    let app = Router::new()
        .route("/", get(index_handler))
        .route("/ws", get(ws_handler))
        .route("/camera/start", post(start_camera_handler))
        .route("/camera/stop", post(stop_camera_handler))
        .route("/camera/status", get(camera_status_handler))
        .with_state(Arc::clone(&state));

    let listener = tokio::net::TcpListener::bind("0.0.0.0:8080").await.unwrap();
    tracing::info!("Listening on http://0.0.0.0:8080");

    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await
        .unwrap();

    // Explicitly stop the camera on shutdown — prevents orphaned rpicam-vid process.
    if let Some(handle) = state.camera.lock().await.take() {
        tracing::info!("Stopping camera on shutdown");
        handle.stop();
    }
}

async fn index_handler() -> Html<&'static str> {
    Html(include_str!("../static/index.html"))
}

async fn ws_handler(ws: WebSocketUpgrade, State(state): State<Arc<AppState>>) -> Response {
    ws.on_upgrade(move |socket| handle_socket(socket, state))
}

async fn handle_socket(mut socket: WebSocket, state: Arc<AppState>) {
    // Hold the lock only long enough to clone the sender.
    // Send a typed error to the client if the camera is not running.
    let nal_rx = {
        let guard = state.camera.lock().await;
        match guard.as_ref() {
            Some(handle) => handle.subscribe(),
            None => {
                tracing::warn!("WebRTC client connected but camera is not running");
                let msg = serde_json::to_string(&SignalMessage::Error {
                    message: "Camera is not running".to_owned(),
                })
                .unwrap_or_default();
                let _ = socket.send(Message::Text(msg.into())).await;
                return;
            }
        }
    };

    let (ws_write, ws_read) = socket.split();
    let (out_tx, out_rx) = mpsc::unbounded_channel::<SignalMessage>();
    let (in_tx, in_rx) = mpsc::unbounded_channel::<SignalMessage>();

    tokio::spawn(ws_read_task(ws_read, in_tx));
    tokio::spawn(ws_write_task(ws_write, out_rx));

    if let Err(e) =
        webrtc_handler::handle_peer_session(in_rx, out_tx, nal_rx, state.stun_urls.clone()).await
    {
        tracing::warn!("peer session error: {e}");
    }
}

async fn start_camera_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    // Hold the lock for the entire check-then-set to prevent TOCTOU.
    let mut guard = state.camera.lock().await;
    if guard.is_some() {
        return (StatusCode::CONFLICT, "Camera already running");
    }
    match camera::start_camera(1920, 1080, 30) {
        Ok((handle, task)) => {
            *guard = Some(handle);
            drop(guard); // release before spawning monitor (monitor re-acquires)
            spawn_camera_monitor(task, Arc::clone(&state.camera));
            tracing::info!("Camera started via API");
            (StatusCode::OK, "Camera started")
        }
        Err(e) => {
            tracing::error!("Failed to start camera: {e}");
            (StatusCode::INTERNAL_SERVER_ERROR, "Failed to start camera")
        }
    }
}

async fn stop_camera_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    match state.camera.lock().await.take() {
        Some(handle) => {
            handle.stop();
            tracing::info!("Camera stopped via API");
            (StatusCode::OK, "Camera stopped")
        }
        None => (StatusCode::CONFLICT, "Camera not running"),
    }
}

#[derive(Serialize)]
struct CameraStatus {
    running: bool,
}

async fn camera_status_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    let running = state.camera.lock().await.is_some();
    Json(CameraStatus { running })
}

async fn ws_read_task(
    mut ws_read: futures_util::stream::SplitStream<WebSocket>,
    in_tx: mpsc::UnboundedSender<SignalMessage>,
) {
    while let Some(result) = ws_read.next().await {
        match result {
            Ok(msg) => match msg {
                Message::Text(text) => match serde_json::from_str::<SignalMessage>(&text) {
                    Ok(signal) => {
                        if in_tx.send(signal).is_err() {
                            break;
                        }
                    }
                    Err(e) => tracing::warn!("failed to parse signal message: {e}"),
                },
                Message::Close(_) => break,
                _ => {}
            },
            Err(e) => {
                tracing::warn!("WebSocket read error: {e}");
                break;
            }
        }
    }
}

async fn ws_write_task(
    mut ws_write: futures_util::stream::SplitSink<WebSocket, Message>,
    mut out_rx: mpsc::UnboundedReceiver<SignalMessage>,
) {
    use futures_util::SinkExt;
    while let Some(msg) = out_rx.recv().await {
        match serde_json::to_string(&msg) {
            Ok(text) => {
                if let Err(e) = ws_write.send(Message::Text(text.into())).await {
                    tracing::debug!("WebSocket write error: {e}");
                    break;
                }
            }
            Err(e) => tracing::warn!("failed to serialize signal message: {e}"),
        }
    }
    let _ = ws_write.close().await;
}

async fn shutdown_signal() {
    use tokio::signal::unix::{SignalKind, signal};

    let mut sigterm = signal(SignalKind::terminate()).expect("failed to install SIGTERM handler");

    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            tracing::info!("Received SIGINT, shutting down");
        }
        _ = sigterm.recv() => {
            tracing::info!("Received SIGTERM, shutting down");
        }
    }
}
```

---

## Step 5 — `webrtc_handler.rs`: No changes needed

Already parameterised on `nal_rx: broadcast::Receiver<Bytes>`. Zero changes.

---

## Final Module Structure

```
backend/server/src/
├── main.rs              — router, handlers, startup/shutdown
├── app_state.rs         — AppState, spawn_camera_monitor        [NEW]
├── camera_handle.rs     — CameraHandle, stop()                  [NEW]
├── camera.rs            — start_camera() subprocess logic       [modified]
├── webrtc_handler.rs    — RTCPeerConnection lifecycle            [unchanged]
└── error/
    └── mod.rs           — ServerError, camera_error, webrtc_error [unchanged]
```

---

## API Contract

| Method | Endpoint | Camera state | Response |
|---|---|---|---|
| `POST` | `/camera/start` | Not running | `200 "Camera started"` |
| `POST` | `/camera/start` | Running | `409 "Camera already running"` |
| `POST` | `/camera/start` | Spawn fails | `500 "Failed to start camera"` |
| `POST` | `/camera/stop` | Running | `200 "Camera stopped"` |
| `POST` | `/camera/stop` | Not running | `409 "Camera not running"` |
| `GET` | `/camera/status` | Any | `200 {"running": true\|false}` |
| `GET` | `/ws` | Not running | WS closes with `{"type":"error","message":"Camera is not running"}` |

---

## Lifecycle Correctness Analysis

| Scenario | Before this plan | After this plan |
|---|---|---|
| `rpicam-vid` crashes | `AppState.camera = Some(dead)` — start returns 409 | Monitor task fires, sets `None` — start works |
| Server Ctrl+C (SIGINT) | `rpicam-vid` becomes orphan | `handle.stop()` called after serve exits |
| Server SIGTERM (systemd) | `rpicam-vid` becomes orphan | `shutdown_signal` catches SIGTERM — same cleanup path |
| Two concurrent `/camera/start` | TOCTOU possible | Lock held across check+set; second request gets 409 |
| WS connect while camera off | Silent close | `SignalMessage::Error` sent before close |

---

## Verification

```bash
cargo check
cargo clippy -- -D warnings

# On Pi:
RUST_LOG=info,server=debug cargo run -p server

# Camera starts idle
curl -s http://pi:8080/camera/status           # {"running":false}
curl -s -X POST http://pi:8080/camera/start    # 200 Camera started
curl -s http://pi:8080/camera/status           # {"running":true}
curl -s -X POST http://pi:8080/camera/start    # 409 Camera already running
curl -s -X POST http://pi:8080/camera/stop     # 200 Camera stopped
curl -s -X POST http://pi:8080/camera/stop     # 409 Camera not running
curl -s http://pi:8080/camera/status           # {"running":false}

# Quest 3 browser → streams normally after /camera/start
# Ctrl+C or SIGTERM → rpicam-vid killed cleanly, no orphan
```
