use crate::{
    camera_handle::CameraHandle,
    error::{ServerResult, camera_error},
};

use bytes::{BufMut, BytesMut};
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
