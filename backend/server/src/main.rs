mod app_state;
mod camera;
mod camera_handle;
pub mod error;
mod still_handler;
mod webrtc_handler;

use app_state::AppState;
use axum::{
    Router,
    extract::{
        ws::{Message, WebSocket},
        {State, WebSocketUpgrade},
    },
    response::{Html, Response},
    routing::{get, post},
};
use futures_util::{SinkExt, StreamExt};
use signal_server::SignalMessage;
use std::sync::Arc;
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

    let state = Arc::new(AppState::new(vec![
        "stun:stun.l.google.com:19302".to_owned(),
    ]));

    let app = Router::new()
        .route("/", get(index_handler))
        .route("/ws", get(ws_handler))
        .route("/still/capture", post(still_handler::capture_handler))
        .route("/still/original", get(still_handler::original_handler))
        .route("/still/mask", get(still_handler::mask_handler))
        .route("/still/overlay", get(still_handler::overlay_handler))
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
    ws.on_upgrade(move |socket| handle_socket(socket, state))
}

async fn handle_socket(socket: WebSocket, state: Arc<AppState>) {
    let (ws_write, ws_read) = socket.split();
    let (out_tx, out_rx) = mpsc::unbounded_channel::<SignalMessage>();
    let (in_tx, in_rx) = mpsc::unbounded_channel::<SignalMessage>();

    tokio::spawn(ws_read_task(ws_read, in_tx));
    tokio::spawn(ws_write_task(ws_write, out_rx));

    if let Err(e) =
        webrtc_handler::handle_peer_session(in_rx, out_tx, state.stun_urls.clone()).await
    {
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
