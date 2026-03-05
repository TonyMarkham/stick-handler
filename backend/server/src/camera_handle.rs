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
