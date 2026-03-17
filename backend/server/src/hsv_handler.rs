use crate::app_state::{AppState, HsvPresets, HsvRange};
use axum::{Json, extract::State, http::StatusCode};
use std::path::Path;
use std::sync::Arc;
use tracing::warn;

pub async fn load_presets(path: &Path) -> HsvPresets {
    match tokio::fs::read_to_string(path).await {
        Ok(contents) => match serde_json::from_str::<HsvPresets>(&contents) {
            Ok(presets) => presets,
            Err(e) => {
                warn!("Failed to parse {}: {e}, using defaults", path.display());
                HsvPresets::default()
            }
        },
        Err(_) => HsvPresets::default(),
    }
}

pub async fn get_handler(State(state): State<Arc<AppState>>) -> Json<HsvPresets> {
    Json(state.hsv_presets.read().await.clone())
}

pub async fn put_green_handler(
    State(state): State<Arc<AppState>>,
    Json(range): Json<HsvRange>,
) -> StatusCode {
    let presets = {
        let mut lock = state.hsv_presets.write().await;
        lock.green = range;
        lock.clone()
    };
    persist(&state.hsv_presets_path, &presets).await
}

pub async fn put_orange_handler(
    State(state): State<Arc<AppState>>,
    Json(range): Json<HsvRange>,
) -> StatusCode {
    let presets = {
        let mut lock = state.hsv_presets.write().await;
        lock.orange = range;
        lock.clone()
    };
    persist(&state.hsv_presets_path, &presets).await
}

async fn persist(path: &Path, presets: &HsvPresets) -> StatusCode {
    match serde_json::to_string_pretty(presets) {
        Ok(json) => match tokio::fs::write(path, json).await {
            Ok(_) => StatusCode::OK,
            Err(e) => {
                warn!("Failed to write {}: {e}", path.display());
                StatusCode::INTERNAL_SERVER_ERROR
            }
        },
        Err(e) => {
            warn!("Failed to serialize HSV presets: {e}");
            StatusCode::INTERNAL_SERVER_ERROR
        }
    }
}
