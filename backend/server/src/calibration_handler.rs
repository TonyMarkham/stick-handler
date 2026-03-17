use crate::{
    app_state::{AppState, HsvRange, ServerMode},
    mjpeg_pipeline,
    still_handler::{HsvParams, blob_circles, decode_jpeg, hsv_mask},
};

use axum::{Json, extract::State, http::StatusCode, response::IntoResponse};
use bytes::Bytes;
use opencv::{calib3d, core, prelude::*};
use serde::{Deserialize, Serialize};
use std::sync::Arc;

// ---------------------------------------------------------------------------
// POST /calibration/start
// ---------------------------------------------------------------------------

pub async fn calibration_start_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }

    state.stop_active_pipeline().await;

    let (camera, _task) = match mjpeg_pipeline::start_mjpeg_camera() {
        Ok(r) => r,
        Err(e) => {
            tracing::error!("Failed to start MJPEG camera for calibration: {e}");
            return StatusCode::INTERNAL_SERVER_ERROR.into_response();
        }
    };

    let pipeline = mjpeg_pipeline::start_detection_loop(camera, Arc::clone(&state.hsv_presets));
    *state.mjpeg_pipeline.lock().await = Some(pipeline);
    *state.mode.write().await = ServerMode::WorldCalibration;

    StatusCode::OK.into_response()
}

// ---------------------------------------------------------------------------
// POST /calibration/end
// ---------------------------------------------------------------------------

pub async fn calibration_end_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::WorldCalibration) {
            return StatusCode::CONFLICT.into_response();
        }
    }

    if let Some(pipeline) = state.mjpeg_pipeline.lock().await.take() {
        pipeline.camera.stop();
    }
    *state.mode.write().await = ServerMode::Setup;

    StatusCode::OK.into_response()
}

// ---------------------------------------------------------------------------
// POST /calibration/recalc
// ---------------------------------------------------------------------------

#[derive(Debug, Deserialize)]
pub struct RecalcRequest {
    pub cylinders: Vec<CylinderPoint>,
}

#[derive(Debug, Deserialize)]
pub struct CylinderPoint {
    pub label: u8,
    pub x: f32,
    pub z: f32,
}

#[derive(Debug, Serialize)]
pub struct RecalcResponse {
    /// 3×3 perspective homography matrix, row-major (pixel coords → world XZ plane).
    pub matrix: Vec<f64>,
}

pub async fn calibration_recalc_handler(
    State(state): State<Arc<AppState>>,
    Json(body): Json<RecalcRequest>,
) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::WorldCalibration) {
            return StatusCode::CONFLICT.into_response();
        }
    }

    if body.cylinders.len() != 4 {
        return (
            StatusCode::UNPROCESSABLE_ENTITY,
            "exactly 4 cylinders required",
        )
            .into_response();
    }

    // Sort cylinders by label so index 0 = label 1, etc.
    let mut cylinders = body.cylinders;
    cylinders.sort_by_key(|c| c.label);

    // Grab the most recent MJPEG frame.
    let frame: Option<Bytes> = {
        let pipeline = state.mjpeg_pipeline.lock().await;
        match pipeline.as_ref() {
            Some(p) => p.latest_frame.read().await.clone(),
            None => None,
        }
    };

    let frame = match frame {
        Some(f) => f,
        None => return StatusCode::SERVICE_UNAVAILABLE.into_response(),
    };

    let orange_preset = state.hsv_presets.read().await.orange.clone();

    let result =
        tokio::task::spawn_blocking(move || compute_homography(&frame, orange_preset, &cylinders))
            .await;

    match result {
        Ok(Ok(matrix)) => Json(RecalcResponse { matrix }).into_response(),
        Ok(Err(RecalcError::WrongBlobCount(n))) => (
            StatusCode::UNPROCESSABLE_ENTITY,
            format!("detected {n} orange blobs, need exactly 4"),
        )
            .into_response(),
        Ok(Err(RecalcError::OpenCv(e))) => {
            tracing::error!("OpenCV error in recalc: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
        Err(e) => {
            tracing::error!("spawn_blocking panicked in recalc: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

// ---------------------------------------------------------------------------
// Homography computation (runs inside spawn_blocking)
// ---------------------------------------------------------------------------

#[derive(Debug)]
enum RecalcError {
    WrongBlobCount(usize),
    OpenCv(opencv::Error),
}

impl From<opencv::Error> for RecalcError {
    fn from(e: opencv::Error) -> Self {
        RecalcError::OpenCv(e)
    }
}

fn compute_homography(
    frame: &Bytes,
    orange_preset: HsvRange,
    cylinders: &[CylinderPoint],
) -> Result<Vec<f64>, RecalcError> {
    let bgr = decode_jpeg(frame)?;
    let orange_mask = hsv_mask(&bgr, HsvParams::from(orange_preset))?;
    let blobs = blob_circles(&orange_mask, 4)?;

    if blobs.len() != 4 {
        return Err(RecalcError::WrongBlobCount(blobs.len()));
    }

    // Drop radii — keep centroids only.
    let centroids: Vec<(f32, f32)> = blobs.into_iter().map(|(cx, cy, _)| (cx, cy)).collect();

    // Mean pixel centroid.
    let mean_x = centroids.iter().map(|(x, _)| x).sum::<f32>() / 4.0;
    let mean_y = centroids.iter().map(|(_, y)| y).sum::<f32>() / 4.0;

    // Sort blobs clockwise from north (−Y direction in screen space).
    // atan2(dx, -dy) gives the clockwise-from-north angle.
    let mut sorted = centroids;
    sorted.sort_by(|a, b| {
        let angle_a = (a.0 - mean_x).atan2(-(a.1 - mean_y));
        let angle_b = (b.0 - mean_x).atan2(-(b.1 - mean_y));
        angle_a
            .partial_cmp(&angle_b)
            .unwrap_or(std::cmp::Ordering::Equal)
    });

    // Winding check: compare the signed area of triangle blobs[0,1,2]
    // against cylinders[0,1,2] (labels 1,2,3).
    //
    // Same sign → spatial orderings match → forward assignment.
    // Different sign → orderings are mirrored → reverse blob list.
    let cross_px = (sorted[1].0 - sorted[0].0) * (sorted[2].1 - sorted[0].1)
        - (sorted[1].1 - sorted[0].1) * (sorted[2].0 - sorted[0].0);

    let cross_world = (cylinders[1].x - cylinders[0].x) * (cylinders[2].z - cylinders[0].z)
        - (cylinders[1].z - cylinders[0].z) * (cylinders[2].x - cylinders[0].x);

    let matched_blobs: Vec<(f32, f32)> = if cross_px.signum() == cross_world.signum() {
        sorted
    } else {
        sorted.into_iter().rev().collect()
    };

    // Build OpenCV point vectors: pixel coords → world XZ coords.
    let src: core::Vector<core::Point2f> = matched_blobs
        .iter()
        .map(|(x, y)| core::Point2f::new(*x, *y))
        .collect();

    let dst: core::Vector<core::Point2f> = cylinders
        .iter()
        .map(|c| core::Point2f::new(c.x, c.z))
        .collect();

    // 4 exact pairs → method 0 (least squares, no RANSAC) gives a unique solution.
    let mut mask_out = opencv::core::Mat::default();
    let h = calib3d::find_homography(&src, &dst, &mut mask_out, 0, 3.0)?;

    // Extract 3×3 CV_64F matrix row-major.
    let mut matrix = Vec::with_capacity(9);
    for row in 0..3i32 {
        for col in 0..3i32 {
            matrix.push(*h.at_2d::<f64>(row, col)?);
        }
    }

    Ok(matrix)
}
