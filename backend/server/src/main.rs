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
use futures_util::{SinkExt, StreamExt};
use serde::Serialize;
use signal_server::SignalMessage;
use std::sync::Arc;
use tokio::{
    signal::unix::{SignalKind, signal},
    sync::mpsc,
};
use tracing::{debug, error, info, warn};

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
    info!("Listening on http://0.0.0.0:8080");

    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await
        .unwrap();

    // Explicitly stop the camera on shutdown — prevents orphaned rpicam-vid process.
    if let Some(handle) = state.camera.lock().await.take() {
        info!("Stopping camera on shutdown");
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
                warn!("WebRTC client connected but camera is not running");
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
        warn!("peer session error: {e}");
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
            info!("Camera started via API");
            (StatusCode::OK, "Camera started")
        }
        Err(e) => {
            error!("Failed to start camera: {e}");
            (StatusCode::INTERNAL_SERVER_ERROR, "Failed to start camera")
        }
    }
}

async fn stop_camera_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    match state.camera.lock().await.take() {
        Some(handle) => {
            handle.stop();
            info!("Camera stopped via API");
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
                    Err(e) => warn!("failed to parse signal message: {e}"),
                },
                Message::Close(_) => break,
                _ => {}
            },
            Err(e) => {
                warn!("WebSocket read error: {e}");
                break;
            }
        }
    }
}

async fn ws_write_task(
    mut ws_write: futures_util::stream::SplitSink<WebSocket, Message>,
    mut out_rx: mpsc::UnboundedReceiver<SignalMessage>,
) {
    while let Some(msg) = out_rx.recv().await {
        match serde_json::to_string(&msg) {
            Ok(text) => {
                if let Err(e) = ws_write.send(Message::Text(text.into())).await {
                    debug!("WebSocket write error: {e}");
                    break;
                }
            }
            Err(e) => warn!("failed to serialize signal message: {e}"),
        }
    }
    let _ = ws_write.close().await;
}

async fn shutdown_signal() {
    let mut sigterm = signal(SignalKind::terminate()).expect("failed to install SIGTERM handler");

    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            info!("Received SIGINT, shutting down");
        }
        _ = sigterm.recv() => {
            info!("Received SIGTERM, shutting down");
        }
    }
}
