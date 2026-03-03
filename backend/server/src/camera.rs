use crate::error::{ServerResult, camera_error};

use bytes::Bytes;
use tokio::{process::Command, sync::broadcast};
use tokio_util::io::SyncIoBridge;
use webrtc_media::io::h264_reader::H264Reader;

const BROADCAST_CAPACITY: usize = 64;
const H264_READER_BUF: usize = 1_048_576; // 1 MiB
const COMMAND: &str = "rpicam-vid";
const CODEC: &str = "h264";
const TIMEOUT: &str = "0";
const OUTPUT: &str = "-";

pub fn start_camera(
    width: u32,
    height: u32,
    framerate: u32,
) -> ServerResult<broadcast::Sender<Bytes>> {
    let mut child = Command::new(COMMAND)
        .args([
            "--codec",
            CODEC,
            "--width",
            &width.to_string(),
            "--height",
            &height.to_string(),
            "--framerate",
            &framerate.to_string(),
            "--inline",
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

    let (tx, _) = broadcast::channel(BROADCAST_CAPACITY);
    let tx_clone = tx.clone();

    tokio::task::spawn_blocking(move || {
        // Keep child alive for the duration of the blocking task
        let _child = child;

        let bridge = SyncIoBridge::new(stdout);
        let mut reader = H264Reader::new(bridge, H264_READER_BUF);

        loop {
            match reader.next_nal() {
                Ok(nal) => {
                    let data: Bytes = nal.data.freeze();
                    tracing::trace!("NAL unit: {} bytes", data.len());
                    // Ignore send errors — no active receivers is fine
                    let _ = tx_clone.send(data);
                }
                Err(e) => {
                    tracing::error!("H264Reader error: {e}");
                    break;
                }
            }
        }
    });

    Ok(tx)
}
