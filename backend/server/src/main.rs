mod camera;
pub mod error;
mod webrtc_handler;

use axum::{
    Router,
    extract::{
        ws::{Message, WebSocket},
        {State, WebSocketUpgrade},
    },
    response::{Html, Response},
    routing::get,
};
use bytes::Bytes;
use futures_util::StreamExt;
use signal_server::SignalMessage;
use std::sync::Arc;
use tokio::sync::{broadcast, mpsc};

#[derive(Clone)]
struct AppState {
    camera_tx: broadcast::Sender<Bytes>,
    stun_urls: Vec<String>,
}

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt()
        .with_env_filter("info,server=debug")
        .init();

    let camera_tx = match camera::start_camera(1920, 1080, 30) {
        Ok(tx) => tx,
        Err(e) => {
            tracing::error!("Failed to start camera: {e}");
            std::process::exit(1);
        }
    };

    let state = Arc::new(AppState {
        camera_tx,
        stun_urls: vec!["stun:stun.l.google.com:19302".to_owned()],
    });

    let app = Router::new()
        .route("/", get(index_handler))
        .route("/ws", get(ws_handler))
        .with_state(state);

    let listener = tokio::net::TcpListener::bind("0.0.0.0:8080").await.unwrap();
    tracing::info!("Listening on http://0.0.0.0:8080");

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

    let nal_rx = state.camera_tx.subscribe();
    if let Err(e) =
        webrtc_handler::handle_peer_session(in_rx, out_tx, nal_rx, state.stun_urls.clone()).await
    {
        tracing::warn!("peer session error: {e}");
    }
}

async fn ws_read_task(
    mut ws_read: futures_util::stream::SplitStream<WebSocket>,
    in_tx: mpsc::UnboundedSender<SignalMessage>,
) {
    use futures_util::StreamExt;
    while let Some(Ok(msg)) = ws_read.next().await {
        match msg {
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
                if ws_write.send(Message::Text(text.into())).await.is_err() {
                    break;
                }
            }
            Err(e) => tracing::warn!("failed to serialize signal message: {e}"),
        }
    }
}

async fn shutdown_signal() {
    tokio::signal::ctrl_c()
        .await
        .expect("failed to install Ctrl+C handler");
    tracing::info!("Shutting down");
}
