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
pub fn spawn_camera_monitor(task: JoinHandle<()>, camera: Arc<TokioMutex<Option<CameraHandle>>>) {
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
