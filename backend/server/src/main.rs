mod app_state;
mod calibration_handler;
mod camera;
mod camera_handle;
pub mod error;
mod hsv_handler;
mod mjpeg_pipeline;
mod still_handler;
mod tracking_handler;
mod webrtc_handler;

use app_state::{AppState, ServerMode};
use axum::{
    Router,
    extract::{
        ws::{Message, WebSocket},
        {State, WebSocketUpgrade},
    },
    http::StatusCode,
    response::{Html, IntoResponse, Response},
    routing::{get, post, put},
};
use futures_util::{SinkExt, StreamExt};
use signal_server::SignalMessage;
use std::{path::PathBuf, sync::Arc};
use tokio::{
    signal::unix::{SignalKind, signal},
    sync::mpsc,
};
use tracing::{debug, info, warn};

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt()
        .with_env_filter("info,server=debug")
        .init();

    let hsv_presets_path = PathBuf::from("hsv_presets.json");
    let hsv_presets = hsv_handler::load_presets(&hsv_presets_path).await;
    let state = Arc::new(AppState::new(
        vec!["stun:stun.l.google.com:19302".to_owned()],
        hsv_presets,
        hsv_presets_path,
    ));

    let app = Router::new()
        .route("/", get(index_handler))
        // WebRTC (Setup mode only)
        .route("/ws", get(ws_handler))
        // Still capture + HSV calibration (Setup mode only for capture/stills)
        .route("/still/capture", post(still_handler::capture_handler))
        .route("/still/original", get(still_handler::original_handler))
        .route("/still/mask", get(still_handler::mask_handler))
        .route("/still/overlay", get(still_handler::overlay_handler))
        .route("/still/detected", get(still_handler::detected_handler))
        // HSV presets (any mode)
        .route("/hsv", get(hsv_handler::get_handler))
        .route("/hsv/green", put(hsv_handler::put_green_handler))
        .route("/hsv/orange", put(hsv_handler::put_orange_handler))
        // World calibration mode
        .route(
            "/calibration/start",
            post(calibration_handler::calibration_start_handler),
        )
        .route(
            "/calibration/end",
            post(calibration_handler::calibration_end_handler),
        )
        .route(
            "/calibration/recalc",
            post(calibration_handler::calibration_recalc_handler),
        )
        // Tracking mode
        .route(
            "/tracking/start",
            post(tracking_handler::tracking_start_handler),
        )
        .route(
            "/tracking/stop",
            post(tracking_handler::tracking_stop_handler),
        )
        .route("/tracking", get(tracking_handler::tracking_ws_handler))
        .with_state(Arc::clone(&state));

    let listener = tokio::net::TcpListener::bind("0.0.0.0:8080").await.unwrap();
    info!("Listening on http://0.0.0.0:8080");

    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await
        .unwrap();
}

async fn index_handler() -> Html<&'static str> {
    Html(include_str!("../static/index.html"))
}

async fn ws_handler(ws: WebSocketUpgrade, State(state): State<Arc<AppState>>) -> Response {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }
    ws.on_upgrade(move |socket| handle_socket(socket, state))
}

async fn handle_socket(socket: WebSocket, state: Arc<AppState>) {
    let (ws_write, ws_read) = socket.split();
    let (out_tx, out_rx) = mpsc::unbounded_channel::<SignalMessage>();
    let (in_tx, in_rx) = mpsc::unbounded_channel::<SignalMessage>();

    tokio::spawn(ws_read_task(ws_read, in_tx));
    tokio::spawn(ws_write_task(ws_write, out_rx));

    if let Err(e) = webrtc_handler::handle_peer_session(in_rx, out_tx, state).await {
        warn!("peer session error: {e}");
    }
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
