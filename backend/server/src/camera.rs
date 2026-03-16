use crate::{
    camera_handle::CameraHandle,
    error::{ServerResult, camera_error},
};

use bytes::{BufMut, BytesMut};
use std::sync::{Arc, Mutex as StdMutex};
use tokio::{process::Command, sync::broadcast, task::JoinHandle};
use tokio_util::io::SyncIoBridge;
use webrtc_media::io::{
    h264_reader::{H264Reader, NalUnitType},
    ivf_reader::IVFReader,
};

const BROADCAST_CAPACITY: usize = 64;
const H264_READER_BUF: usize = 1_048_576; // 1 MiB
const IVF_READER_BUF: usize = 1_048_576; // 1 MiB
const COMMAND: &str = "rpicam-vid";
const TIMEOUT: &str = "0";
const OUTPUT: &str = "-";

/// Annex B start code that precedes every NAL unit in the stream
const START_CODE: &[u8] = &[0x00, 0x00, 0x00, 0x01];

/// Video codec to use for the camera stream.
///
/// Chosen per-session based on what the WebRTC client advertises in its SDP offer:
/// - [`H264`]: hardware-accelerated 1080p30, for Quest 3 and NVIDIA Windows clients
/// - [`Vp8`]: software-encoded 720p30 via libvpx, for AMD/Intel Windows clients
#[derive(Debug, Clone, Copy)]
pub enum VideoCodec {
    H264,
    Vp8,
}

/// Spawn `rpicam-vid` and return a [`CameraHandle`] for control plus a
/// [`JoinHandle`] for lifecycle tracking.
///
/// Codec-specific parameters (resolution, encoder, container) are chosen
/// automatically from the `codec` argument.
pub fn start_camera(codec: VideoCodec) -> ServerResult<(CameraHandle, JoinHandle<()>)> {
    let args: &[&str] = match codec {
        VideoCodec::H264 => &[
            "--codec",
            "h264",
            "--libav-format",
            "h264",
            "--libav-video-codec",
            "h264_v4l2m2m",
            "--width",
            "1920",
            "--height",
            "1080",
            "--framerate",
            "30",
            "--inline",
            "--intra",
            "30",
            "--timeout",
            TIMEOUT,
            "--output",
            OUTPUT,
        ],
        VideoCodec::Vp8 => &[
            "--codec",
            "libav",
            "--libav-format",
            "ivf",
            "--libav-video-codec",
            "libvpx-vp8",
            "--width",
            "1280",
            "--height",
            "720",
            "--framerate",
            "30",
            "--timeout",
            TIMEOUT,
            "--output",
            OUTPUT,
        ],
    };

    let mut child = Command::new(COMMAND)
        .args(args)
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
        let _child_guard = child_for_task;
        let bridge = SyncIoBridge::new(stdout);

        match codec {
            VideoCodec::H264 => {
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
                                    tracing::trace!("H264 access unit: {} bytes", frame.len());
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
            }
            VideoCodec::Vp8 => {
                let (mut reader, _header) = match IVFReader::new(bridge, IVF_READER_BUF) {
                    Ok(r) => r,
                    Err(e) => {
                        tracing::error!("IVFReader init error: {e}");
                        return;
                    }
                };

                loop {
                    match reader.next_packet() {
                        Ok((packet, _frame_header)) => {
                            tracing::trace!("VP8 frame: {} bytes", packet.data.len());
                            let _ = tx_clone.send(packet.data);
                        }
                        Err(e) => {
                            tracing::error!("IVFReader error: {e}");
                            break;
                        }
                    }
                }
            }
        }
    });

    Ok((CameraHandle { sender: tx, child }, task))
}
