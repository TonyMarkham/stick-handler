use crate::app_state::AppState;

use axum::{
    extract::{Query, State},
    http::{StatusCode, header},
    response::{IntoResponse, Response},
};
use bytes::Bytes;
use opencv::{
    core::{self, Mat, Scalar, Vector},
    imgcodecs, imgproc,
    prelude::*,
};
use serde::Deserialize;
use std::sync::Arc;
use tokio::process::Command;

const CAPTURE_COMMAND: &str = "rpicam-still";

#[derive(Debug, Clone, Copy, Deserialize)]
pub struct HsvParams {
    #[serde(default)]
    pub h_min: u8,
    #[serde(default = "default_h_max")]
    pub h_max: u8,
    #[serde(default)]
    pub s_min: u8,
    #[serde(default = "default_max")]
    pub s_max: u8,
    #[serde(default)]
    pub v_min: u8,
    #[serde(default = "default_max")]
    pub v_max: u8,
}

fn default_h_max() -> u8 {
    179
}
fn default_max() -> u8 {
    255
}

/// `POST /still/capture` — fires rpicam-still and stores the JPEG in memory.
pub async fn capture_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    let output = match Command::new(CAPTURE_COMMAND)
        .args(["--output", "-", "--encoding", "jpg", "--timeout", "2000"])
        .output()
        .await
    {
        Ok(o) => o,
        Err(e) => {
            tracing::error!("Failed to spawn {CAPTURE_COMMAND}: {e}");
            return StatusCode::INTERNAL_SERVER_ERROR.into_response();
        }
    };

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        tracing::error!("{CAPTURE_COMMAND} exited with {}: {stderr}", output.status);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    *state.still_jpeg.write().await = Some(Bytes::from(output.stdout));
    StatusCode::OK.into_response()
}

/// `GET /still/original` — returns the raw captured JPEG.
pub async fn original_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    match state.still_jpeg.read().await.clone() {
        Some(data) => jpeg_response(data),
        None => (StatusCode::NOT_FOUND, "No still captured yet").into_response(),
    }
}

/// `GET /still/mask?h_min=&h_max=&s_min=&s_max=&v_min=&v_max=`
/// Returns a pure black-and-white JPEG: white where pixels fall within the HSV
/// range, black elsewhere.
pub async fn mask_handler(
    State(state): State<Arc<AppState>>,
    Query(params): Query<HsvParams>,
) -> impl IntoResponse {
    let Some(data) = state.still_jpeg.read().await.clone() else {
        return (StatusCode::NOT_FOUND, "No still captured yet").into_response();
    };

    match tokio::task::spawn_blocking(move || build_mask(&data, params)).await {
        Ok(Ok(out)) => jpeg_response(out),
        Ok(Err(e)) => {
            tracing::error!("mask build error: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
        Err(e) => {
            tracing::error!("spawn_blocking panicked: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

/// `GET /still/overlay?h_min=&h_max=&s_min=&s_max=&v_min=&v_max=`
/// Returns the original image with matching pixels blended 50% toward yellow,
/// making it easy to see exactly what the filter is catching.
pub async fn overlay_handler(
    State(state): State<Arc<AppState>>,
    Query(params): Query<HsvParams>,
) -> impl IntoResponse {
    let Some(data) = state.still_jpeg.read().await.clone() else {
        return (StatusCode::NOT_FOUND, "No still captured yet").into_response();
    };

    match tokio::task::spawn_blocking(move || build_overlay(&data, params)).await {
        Ok(Ok(out)) => jpeg_response(out),
        Ok(Err(e)) => {
            tracing::error!("overlay build error: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
        Err(e) => {
            tracing::error!("spawn_blocking panicked: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

fn jpeg_response(data: Bytes) -> Response {
    ([(header::CONTENT_TYPE, "image/jpeg")], data).into_response()
}

fn decode_jpeg(data: &Bytes) -> opencv::Result<Mat> {
    let buf = Vector::<u8>::from_slice(data);
    imgcodecs::imdecode(&buf, imgcodecs::IMREAD_COLOR)
}

fn encode_jpeg(mat: &Mat) -> opencv::Result<Bytes> {
    let mut buf = Vector::<u8>::new();
    imgcodecs::imencode(".jpg", mat, &mut buf, &Vector::<i32>::new())?;
    Ok(Bytes::copy_from_slice(buf.as_slice()))
}

/// Converts BGR `src` to HSV and returns a single-channel mask: 255 where
/// the pixel falls within `params`, 0 elsewhere.
fn hsv_mask(src: &Mat, params: HsvParams) -> opencv::Result<Mat> {
    let mut hsv = Mat::default();
    imgproc::cvt_color(src, &mut hsv, imgproc::COLOR_BGR2HSV, 0)?;

    let lower = Scalar::new(
        params.h_min as f64,
        params.s_min as f64,
        params.v_min as f64,
        0.0,
    );
    let upper = Scalar::new(
        params.h_max as f64,
        params.s_max as f64,
        params.v_max as f64,
        0.0,
    );

    let mut mask = Mat::default();
    core::in_range(&hsv, &lower, &upper, &mut mask)?;
    Ok(mask)
}

fn build_mask(data: &Bytes, params: HsvParams) -> opencv::Result<Bytes> {
    let bgr = decode_jpeg(data)?;
    let mask = hsv_mask(&bgr, params)?;
    encode_jpeg(&mask)
}

fn build_overlay(data: &Bytes, params: HsvParams) -> opencv::Result<Bytes> {
    let bgr = decode_jpeg(data)?;
    let mask = hsv_mask(&bgr, params)?;

    // Blend the original 50% with yellow (BGR: 0, 255, 255) across the whole image.
    let yellow = Mat::new_rows_cols_with_default(
        bgr.rows(),
        bgr.cols(),
        core::CV_8UC3,
        Scalar::new(0.0, 255.0, 255.0, 0.0),
    )?;
    let mut blended = Mat::default();
    core::add_weighted(&bgr, 0.5, &yellow, 0.5, 0.0, &mut blended, -1)?;

    // Expand the single-channel mask to 3 channels so we can use it as a
    // bitwise selector (mask values are exactly 0x00 or 0xFF).
    let mut mask_3ch = Mat::default();
    imgproc::cvt_color(&mask, &mut mask_3ch, imgproc::COLOR_GRAY2BGR, 0)?;

    let mut mask_inv = Mat::default();
    core::bitwise_not(&mask_3ch, &mut mask_inv, &core::no_array())?;

    // Where mask = 255: take the yellow-blended pixel.
    // Where mask = 0:   take the original pixel.
    let mut blend_part = Mat::default();
    core::bitwise_and(&blended, &mask_3ch, &mut blend_part, &core::no_array())?;

    let mut orig_part = Mat::default();
    core::bitwise_and(&bgr, &mask_inv, &mut orig_part, &core::no_array())?;

    let mut result = Mat::default();
    core::bitwise_or(&blend_part, &orig_part, &mut result, &core::no_array())?;

    encode_jpeg(&result)
}
