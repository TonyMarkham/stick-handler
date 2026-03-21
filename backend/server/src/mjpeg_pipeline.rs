use crate::{
    app_state::{HsvPresets, MjpegPipeline},
    camera_handle::CameraHandle,
    error::{ServerResult, camera_error},
    still_handler::{HsvParams, decode_jpeg, green_centroid},
};

use bytes::Bytes;
use std::{
    io::Read,
    sync::{Arc, Mutex as StdMutex},
};
use tokio::{
    process::Command,
    sync::{RwLock, broadcast},
    task::JoinHandle,
};
use tokio_util::io::SyncIoBridge;

const COMMAND: &str = "rpicam-vid";
/// Keep the channel small — we only want the latest frame in the ring.
const BROADCAST_CAPACITY: usize = 4;

/// Spawn `rpicam-vid --codec mjpeg` at 1920×1080 @ 30fps and return a
/// [`CameraHandle`] (for stopping) plus a [`JoinHandle`] (for lifecycle tracking).
///
/// Each complete MJPEG frame is broadcast as raw [`Bytes`] (a full JPEG).
pub fn start_mjpeg_camera() -> ServerResult<(CameraHandle, JoinHandle<()>)> {
    let mut child = Command::new(COMMAND)
        .args([
            "--codec",
            "mjpeg",
            "--width",
            "1920",
            "--height",
            "1080",
            "--framerate",
            "30",
            "--timeout",
            "0",
            "--output",
            "-",
        ])
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .spawn()
        .map_err(|e| camera_error(format!("Failed to spawn {COMMAND} (mjpeg): {e}")))?;

    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| camera_error(format!("{COMMAND} stdout not captured")))?;

    let child = Arc::new(StdMutex::new(child));
    let child_for_task = Arc::clone(&child);

    let (tx, _) = broadcast::channel::<Bytes>(BROADCAST_CAPACITY);
    let tx_clone = tx.clone();

    let task = tokio::task::spawn_blocking(move || {
        let _child_guard = child_for_task;
        let mut reader = SyncIoBridge::new(stdout);
        let mut buf: Vec<u8> = Vec::with_capacity(1_048_576);
        let mut tmp = [0u8; 65536];

        loop {
            match reader.read(&mut tmp) {
                Ok(0) => break, // EOF — process exited
                Ok(n) => {
                    buf.extend_from_slice(&tmp[..n]);
                    // Drain all complete JPEG frames (terminated by FF D9 EOI marker).
                    while let Some(eoi) = find_eoi(&buf) {
                        let frame_bytes = &buf[..eoi + 2];
                        // Only emit well-formed frames that start with JPEG SOI.
                        if frame_bytes.len() >= 2
                            && frame_bytes[0] == 0xFF
                            && frame_bytes[1] == 0xD8
                        {
                            let _ = tx_clone.send(Bytes::copy_from_slice(frame_bytes));
                        }
                        buf.drain(..eoi + 2);
                    }
                }
                Err(e) => {
                    tracing::error!("MJPEG read error: {e}");
                    break;
                }
            }
        }
    });

    Ok((CameraHandle { sender: tx, child }, task))
}

/// Find the byte offset of the first JPEG EOI marker (`FF D9`) in `buf`.
/// Returns the index of the `0xFF` byte.
fn find_eoi(buf: &[u8]) -> Option<usize> {
    buf.windows(2).position(|w| w == [0xFF, 0xD9])
}

/// Spawn the green blob detection loop and return the populated [`MjpegPipeline`].
///
/// The detection loop reads MJPEG frames broadcast by `camera`, stores the latest
/// frame in `MjpegPipeline::latest_frame`, applies the green HSV preset, and
/// broadcasts `(cx, cy)` centroids over `MjpegPipeline::centroid_tx`.
///
/// The loop exits naturally when the camera broadcast closes (i.e., after
/// [`CameraHandle::stop`] is called on the returned pipeline's `camera` field).
pub fn start_detection_loop(
    camera: CameraHandle,
    hsv_presets: Arc<RwLock<HsvPresets>>,
) -> MjpegPipeline {
    let (centroid_tx, _) = broadcast::channel::<(f32, f32)>(64);
    let latest_frame: Arc<RwLock<Option<Bytes>>> = Arc::new(RwLock::new(None));

    let mut frame_rx = camera.subscribe();
    let centroid_tx_clone = centroid_tx.clone();
    let latest_frame_clone = Arc::clone(&latest_frame);

    tokio::spawn(async move {
        loop {
            match frame_rx.recv().await {
                Ok(frame) => {
                    *latest_frame_clone.write().await = Some(frame.clone());

                    let presets = hsv_presets.read().await;
                    let green_params: HsvParams = presets.green.clone().into();
                    let green2_params: Option<HsvParams> = presets.green2.clone().map(Into::into);
                    drop(presets);

                    let result = tokio::task::spawn_blocking(move || {
                        let bgr = decode_jpeg(&frame)?;
                        green_centroid(&bgr, green_params, green2_params)
                    })
                    .await;

                    match result {
                        Ok(Ok(Some((cx, cy)))) => {
                            let _ = centroid_tx_clone.send((cx, cy));
                        }
                        Ok(Ok(None)) => {} // no green blob this frame
                        Ok(Err(e)) => tracing::warn!("green detection error: {e}"),
                        Err(e) => tracing::warn!("detection task panicked: {e}"),
                    }
                }
                Err(broadcast::error::RecvError::Lagged(n)) => {
                    tracing::warn!("detection loop lagged {n} frames — dropping");
                }
                Err(broadcast::error::RecvError::Closed) => {
                    tracing::info!("MJPEG broadcast closed, detection loop exiting");
                    break;
                }
            }
        }
    });

    MjpegPipeline {
        camera,
        centroid_tx,
        latest_frame,
    }
}
