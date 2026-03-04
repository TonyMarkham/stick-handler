use crate::error::{ServerResult, camera_error};

use bytes::{BufMut, Bytes, BytesMut};
use tokio::{process::Command, sync::broadcast};
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

pub fn start_camera(
    width: u32,
    height: u32,
    framerate: u32,
) -> ServerResult<broadcast::Sender<Bytes>> {
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

    let (tx, _) = broadcast::channel(BROADCAST_CAPACITY);
    let tx_clone = tx.clone();

    tokio::task::spawn_blocking(move || {
        // Keep child alive for the duration of the blocking task
        let _child = child;

        let bridge = SyncIoBridge::new(stdout);
        let mut reader = H264Reader::new(bridge, H264_READER_BUF);
        let mut access_unit = BytesMut::new();

        loop {
            match reader.next_nal() {
                Ok(nal) => {
                    let unit_type = nal.unit_type;

                    // Prepend Annex B start code then append the NAL data
                    access_unit.put_slice(START_CODE);
                    access_unit.put_slice(&nal.data);

                    // VCL NALs (IDR and non-IDR slices) mark the end of an
                    // access unit — flush the accumulated buffer as one frame.
                    // SPS, PPS, SEI etc. keep buffering until the VCL NAL.
                    match unit_type {
                        NalUnitType::CodedSliceIdr | NalUnitType::CodedSliceNonIdr => {
                            let frame = access_unit.split().freeze();
                            tracing::trace!("access unit: {} bytes", frame.len());
                            // Ignore send errors — no active receivers is fine
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

    Ok(tx)
}