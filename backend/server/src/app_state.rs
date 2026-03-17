use bytes::Bytes;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use std::sync::Arc;
use tokio::sync::RwLock;

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

#[derive(Clone)]
pub struct AppState {
    pub stun_urls: Vec<String>,
    pub still_jpeg: Arc<RwLock<Option<Bytes>>>,
    pub hsv_presets: Arc<RwLock<HsvPresets>>,
    pub hsv_presets_path: PathBuf,
}

impl AppState {
    pub fn new(stun_urls: Vec<String>, hsv_presets: HsvPresets, hsv_presets_path: PathBuf) -> Self {
        Self {
            stun_urls,
            still_jpeg: Arc::new(RwLock::new(None)),
            hsv_presets: Arc::new(RwLock::new(hsv_presets)),
            hsv_presets_path,
        }
    }
}
