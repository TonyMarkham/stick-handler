use crate::camera_handle::CameraHandle;

use bytes::Bytes;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use std::sync::Arc;
use tokio::sync::{Mutex, RwLock, broadcast};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HsvRange {
    pub h_min: u8,
    pub h_max: u8,
    pub s_min: u8,
    pub s_max: u8,
    pub v_min: u8,
    pub v_max: u8,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HsvPresets {
    pub green: HsvRange,
    #[serde(default)]
    pub green2: Option<HsvRange>,
    pub orange: HsvRange,
}

impl Default for HsvPresets {
    fn default() -> Self {
        Self {
            green: HsvRange {
                h_min: 75,
                h_max: 90,
                s_min: 150,
                s_max: 255,
                v_min: 30,
                v_max: 110,
            },
            green2: None,
            orange: HsvRange {
                h_min: 0,
                h_max: 20,
                s_min: 230,
                s_max: 255,
                v_min: 230,
                v_max: 255,
            },
        }
    }
}

/// The server's operating mode. Only one camera pipeline is active at a time.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ServerMode {
    /// Default. WebRTC stream and HSV calibration endpoints live.
    Setup,
    /// MJPEG + green blob centroid stream running. User places cylinders on stickers.
    WorldCalibration,
    /// MJPEG + green blob centroid stream running. Active gameplay.
    Tracking,
}

/// A running MJPEG detection pipeline.
pub struct MjpegPipeline {
    /// The underlying camera subprocess handle.
    pub camera: CameraHandle,
    /// Broadcasts `(cx, cy)` pixel centroids for the largest green blob each frame.
    pub centroid_tx: broadcast::Sender<(f32, f32)>,
    /// Most recent decoded MJPEG frame, used by `/calibration/recalc`.
    pub latest_frame: Arc<RwLock<Option<Bytes>>>,
}

#[derive(Clone)]
pub struct AppState {
    pub stun_urls: Vec<String>,
    pub still_jpeg: Arc<RwLock<Option<Bytes>>>,
    pub hsv_presets: Arc<RwLock<HsvPresets>>,
    pub hsv_presets_path: PathBuf,
    /// Single source of truth for camera ownership.
    pub mode: Arc<RwLock<ServerMode>>,
    /// Populated only while a `/ws` WebRTC session is live.
    pub webrtc_camera: Arc<Mutex<Option<CameraHandle>>>,
    /// Populated when mode is `WorldCalibration` or `Tracking`.
    pub mjpeg_pipeline: Arc<Mutex<Option<MjpegPipeline>>>,
}

impl AppState {
    pub fn new(stun_urls: Vec<String>, hsv_presets: HsvPresets, hsv_presets_path: PathBuf) -> Self {
        Self {
            stun_urls,
            still_jpeg: Arc::new(RwLock::new(None)),
            hsv_presets: Arc::new(RwLock::new(hsv_presets)),
            hsv_presets_path,
            mode: Arc::new(RwLock::new(ServerMode::Setup)),
            webrtc_camera: Arc::new(Mutex::new(None)),
            mjpeg_pipeline: Arc::new(Mutex::new(None)),
        }
    }

    /// Kill whichever pipeline is currently active and reset both camera fields to `None`.
    ///
    /// Called before every mode transition that acquires the camera, ensuring hardware
    /// is free before the new pipeline spawns. Handles the case where a WebRTC or
    /// tracking session was not cleanly closed by the client.
    pub async fn stop_active_pipeline(&self) {
        if let Some(handle) = self.webrtc_camera.lock().await.take() {
            handle.stop();
        }
        if let Some(pipeline) = self.mjpeg_pipeline.lock().await.take() {
            pipeline.camera.stop();
        }
    }
}
