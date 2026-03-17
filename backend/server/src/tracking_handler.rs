use crate::{
    app_state::{AppState, ServerMode},
    mjpeg_pipeline,
};

use axum::{
    extract::ws::{Message, WebSocket},
    extract::{State, WebSocketUpgrade},
    http::StatusCode,
    response::{IntoResponse, Response},
};
use futures_util::{SinkExt, StreamExt};
use std::sync::Arc;
use tokio::sync::broadcast;

// ---------------------------------------------------------------------------
// POST /tracking/start
// ---------------------------------------------------------------------------

pub async fn tracking_start_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }

    state.stop_active_pipeline().await;

    let (camera, _task) = match mjpeg_pipeline::start_mjpeg_camera() {
        Ok(r) => r,
        Err(e) => {
            tracing::error!("Failed to start MJPEG camera for tracking: {e}");
            return StatusCode::INTERNAL_SERVER_ERROR.into_response();
        }
    };

    let pipeline = mjpeg_pipeline::start_detection_loop(camera, Arc::clone(&state.hsv_presets));
    *state.mjpeg_pipeline.lock().await = Some(pipeline);
    *state.mode.write().await = ServerMode::Tracking;

    StatusCode::OK.into_response()
}

// ---------------------------------------------------------------------------
// POST /tracking/stop
// ---------------------------------------------------------------------------

pub async fn tracking_stop_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Tracking) {
            return StatusCode::CONFLICT.into_response();
        }
    }

    if let Some(pipeline) = state.mjpeg_pipeline.lock().await.take() {
        pipeline.camera.stop();
    }
    *state.mode.write().await = ServerMode::Setup;

    StatusCode::OK.into_response()
}

// ---------------------------------------------------------------------------
// GET /tracking  (WebSocket — valid in WorldCalibration | Tracking mode)
// ---------------------------------------------------------------------------

pub async fn tracking_ws_handler(
    ws: WebSocketUpgrade,
    State(state): State<Arc<AppState>>,
) -> Response {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::WorldCalibration | ServerMode::Tracking) {
            return StatusCode::CONFLICT.into_response();
        }
    }
    ws.on_upgrade(move |socket| handle_tracking_socket(socket, state))
}

async fn handle_tracking_socket(socket: WebSocket, state: Arc<AppState>) {
    // Subscribe to the centroid broadcast. We acquire the pipeline lock briefly
    // to get the Sender, then release it — the Receiver is self-contained.
    let mut centroid_rx: broadcast::Receiver<(f32, f32)> = {
        let pipeline = state.mjpeg_pipeline.lock().await;
        match pipeline.as_ref() {
            Some(p) => p.centroid_tx.subscribe(),
            None => return, // pipeline went away before upgrade completed
        }
    };

    let (mut ws_write, _ws_read) = socket.split::<Message>();

    loop {
        match centroid_rx.recv().await {
            Ok((x, y)) => {
                // Inline JSON format: {"x":960.5,"y":540.2}
                let msg = format!("{{\"x\":{x},\"y\":{y}}}");
                if ws_write.send(Message::Text(msg.into())).await.is_err() {
                    break; // client disconnected
                }
            }
            Err(broadcast::error::RecvError::Lagged(n)) => {
                tracing::warn!("tracking WS lagged {n} frames");
                // Continue — lagged frames are skipped, not fatal.
            }
            Err(broadcast::error::RecvError::Closed) => {
                // MJPEG pipeline was killed (mode transition or server shutdown).
                break;
            }
        }
    }
}
