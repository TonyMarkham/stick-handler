use crate::app_state::{AppState, HsvPresets, HsvRange, ServerMode};

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

const MORPH_CLOSE_KERNEL: i32 = 25;
const MIN_ORANGE_AREA_PX2: f64 = 30.0;
const MIN_GREEN_AREA_PX2: f64 = 500.0;
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

impl From<HsvRange> for HsvParams {
    fn from(r: HsvRange) -> Self {
        Self {
            h_min: r.h_min,
            h_max: r.h_max,
            s_min: r.s_min,
            s_max: r.s_max,
            v_min: r.v_min,
            v_max: r.v_max,
        }
    }
}

/// `POST /still/capture` — fires rpicam-still and stores the JPEG in memory.
pub async fn capture_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }

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
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }
    match state.still_jpeg.read().await.clone() {
        Some(data) => jpeg_response(data),
        None => (StatusCode::NOT_FOUND, "No still captured yet").into_response(),
    }
}

/// `GET /still/mask?h_min=&h_max=&s_min=&s_max=&v_min=&v_max=`
/// Returns a pure black-and-white JPEG: white where pixels fall within the HSV
/// range, black elsewhere. Includes `X-Blob-Count` response header.
pub async fn mask_handler(
    State(state): State<Arc<AppState>>,
    Query(params): Query<HsvParams>,
) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }
    let Some(data) = state.still_jpeg.read().await.clone() else {
        return (StatusCode::NOT_FOUND, "No still captured yet").into_response();
    };

    match tokio::task::spawn_blocking(move || build_mask(&data, params)).await {
        Ok(Ok((out, count))) => jpeg_response_with_count(out, count),
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
/// making it easy to see exactly what the filter is catching. Includes `X-Blob-Count`.
pub async fn overlay_handler(
    State(state): State<Arc<AppState>>,
    Query(params): Query<HsvParams>,
) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }
    let Some(data) = state.still_jpeg.read().await.clone() else {
        return (StatusCode::NOT_FOUND, "No still captured yet").into_response();
    };

    match tokio::task::spawn_blocking(move || build_overlay(&data, params)).await {
        Ok(Ok((out, count))) => jpeg_response_with_count(out, count),
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

fn jpeg_response_with_count(data: Bytes, count: usize) -> Response {
    use axum::http::{HeaderMap, HeaderName, HeaderValue};
    let mut headers = HeaderMap::new();
    headers.insert(header::CONTENT_TYPE, HeaderValue::from_static("image/jpeg"));
    headers.insert(
        HeaderName::from_static("x-blob-count"),
        HeaderValue::from_str(&count.to_string()).unwrap_or(HeaderValue::from_static("0")),
    );
    (headers, data).into_response()
}

pub(crate) fn decode_jpeg(data: &Bytes) -> opencv::Result<Mat> {
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
pub(crate) fn hsv_mask(src: &Mat, params: HsvParams) -> opencv::Result<Mat> {
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

/// Applies morphological closing (dilate → erode) to `mask`, filling holes
/// caused by specular highlights.  Uses an elliptical kernel of `kernel_size`.
fn morph_close(mask: &Mat, kernel_size: i32) -> opencv::Result<Mat> {
    let kernel = imgproc::get_structuring_element(
        imgproc::MORPH_ELLIPSE,
        core::Size::new(kernel_size, kernel_size),
        core::Point::new(-1, -1),
    )?;
    let mut closed = Mat::default();
    imgproc::morphology_ex(
        mask,
        &mut closed,
        imgproc::MORPH_CLOSE,
        &kernel,
        core::Point::new(-1, -1),
        2,
        core::BORDER_CONSTANT,
        imgproc::morphology_default_border_value()?,
    )?;
    Ok(closed)
}

/// Finds up to `max` orange sticker centroids using `fitEllipse` (accurate for
/// flat, angled elliptical projections).  Returns `(cx, cy, representative_radius)`.
pub(crate) fn orange_centroids(mask: &Mat, max: usize) -> opencv::Result<Vec<(f32, f32, f32)>> {
    let mask_mut = mask.clone();
    let mut contours: Vector<Vector<core::Point>> = Vector::new();
    imgproc::find_contours(
        &mask_mut,
        &mut contours,
        imgproc::RETR_EXTERNAL,
        imgproc::CHAIN_APPROX_SIMPLE,
        core::Point::new(0, 0),
    )?;

    let mut results: Vec<(f32, f32, f32, f64)> = Vec::new(); // (cx, cy, r, area)
    for i in 0..contours.len() {
        let contour = contours.get(i)?;
        if contour.len() < 5 {
            continue; // fitEllipse requires at least 5 points
        }
        let area = imgproc::contour_area(&contour, false)?;
        if area < MIN_ORANGE_AREA_PX2 {
            continue;
        }
        let ellipse = imgproc::fit_ellipse(&contour)?;
        let cx = ellipse.center.x;
        let cy = ellipse.center.y;
        let r = (ellipse.size.width + ellipse.size.height) / 4.0;
        results.push((cx, cy, r, area));
    }
    results.sort_by(|a, b| b.3.partial_cmp(&a.3).unwrap_or(std::cmp::Ordering::Equal));
    results.truncate(max);
    Ok(results
        .into_iter()
        .map(|(cx, cy, r, _)| (cx, cy, r))
        .collect())
}

/// Detects the green puck centroid via image moments on the combined
/// (green OR green2) mask after morphological closing.
pub(crate) fn green_centroid(
    bgr: &Mat,
    green_params: HsvParams,
    green2_params: Option<HsvParams>,
) -> opencv::Result<Option<(f32, f32)>> {
    let mut mask = hsv_mask(bgr, green_params)?;
    if let Some(p2) = green2_params {
        let mask2 = hsv_mask(bgr, p2)?;
        let mut combined = Mat::default();
        core::bitwise_or(&mask, &mask2, &mut combined, &core::no_array())?;
        mask = combined;
    }
    let closed = morph_close(&mask, MORPH_CLOSE_KERNEL)?;

    let mut contours: Vector<Vector<core::Point>> = Vector::new();
    imgproc::find_contours(
        &closed.clone(),
        &mut contours,
        imgproc::RETR_EXTERNAL,
        imgproc::CHAIN_APPROX_SIMPLE,
        core::Point::new(0, 0),
    )?;

    let mut best: Option<(f64, usize)> = None; // (area, index)
    for i in 0..contours.len() {
        let contour = contours.get(i)?;
        let area = imgproc::contour_area(&contour, false)?;
        if area < MIN_GREEN_AREA_PX2 {
            continue;
        }
        if best.is_none_or(|(best_area, _)| area > best_area) {
            best = Some((area, i));
        }
    }

    let Some((_, idx)) = best else {
        return Ok(None);
    };
    let contour = contours.get(idx)?;
    let m = imgproc::moments(&contour, false)?;
    if m.m00 == 0.0 {
        return Ok(None);
    }
    Ok(Some(((m.m10 / m.m00) as f32, (m.m01 / m.m00) as f32)))
}

fn build_mask(data: &Bytes, params: HsvParams) -> opencv::Result<(Bytes, usize)> {
    let bgr = decode_jpeg(data)?;
    let mask = hsv_mask(&bgr, params)?;
    let count = blob_circles(&mask, 20)?.len();
    Ok((encode_jpeg(&mask)?, count))
}

fn build_overlay(data: &Bytes, params: HsvParams) -> opencv::Result<(Bytes, usize)> {
    let bgr = decode_jpeg(data)?;
    let mask = hsv_mask(&bgr, params)?;
    let count = blob_circles(&mask, 20)?.len();

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

    Ok((encode_jpeg(&result)?, count))
}

/// Returns `(cx, cy, radius)` for the `max_blobs` largest blobs in `mask`,
/// treating each contour as a circle via `min_enclosing_circle`, ranked by
/// radius descending.
pub(crate) fn blob_circles(mask: &Mat, max_blobs: usize) -> opencv::Result<Vec<(f32, f32, f32)>> {
    let mask_mut = mask.clone();
    let mut contours: Vector<Vector<core::Point>> = Vector::new();
    imgproc::find_contours(
        &mask_mut,
        &mut contours,
        imgproc::RETR_EXTERNAL,
        imgproc::CHAIN_APPROX_SIMPLE,
        core::Point::new(0, 0),
    )?;

    let mut circles: Vec<(f32, f32, f32)> = Vec::new();
    for i in 0..contours.len() {
        let contour = contours.get(i)?;
        if contour.len() < 3 {
            continue;
        }
        let mut center = core::Point2f::new(0.0, 0.0);
        let mut radius = 0f32;
        imgproc::min_enclosing_circle(&contour, &mut center, &mut radius)?;
        circles.push((center.x, center.y, radius));
    }
    circles.sort_by(|a, b| b.2.partial_cmp(&a.2).unwrap_or(std::cmp::Ordering::Equal));
    circles.truncate(max_blobs);
    Ok(circles)
}

/// Draws a crosshair + circle outline with a white halo for visibility on any
/// background.  Arm length is 60% of the blob radius.
fn draw_circle_crosshair(img: &mut Mat, cx: f32, cy: f32, radius: f32) -> opencv::Result<()> {
    let cxi = cx as i32;
    let cyi = cy as i32;
    let r = radius as i32;
    let arm = (radius * 0.6) as i32;
    let black = Scalar::new(0.0, 0.0, 0.0, 0.0);
    let white = Scalar::new(255.0, 255.0, 255.0, 0.0);

    // Horizontal arm: white halo then black line.
    imgproc::line(
        img,
        core::Point::new(cxi - arm, cyi),
        core::Point::new(cxi + arm, cyi),
        white,
        5,
        imgproc::LINE_AA,
        0,
    )?;
    imgproc::line(
        img,
        core::Point::new(cxi - arm, cyi),
        core::Point::new(cxi + arm, cyi),
        black,
        3,
        imgproc::LINE_AA,
        0,
    )?;
    // Vertical arm.
    imgproc::line(
        img,
        core::Point::new(cxi, cyi - arm),
        core::Point::new(cxi, cyi + arm),
        white,
        5,
        imgproc::LINE_AA,
        0,
    )?;
    imgproc::line(
        img,
        core::Point::new(cxi, cyi - arm),
        core::Point::new(cxi, cyi + arm),
        black,
        3,
        imgproc::LINE_AA,
        0,
    )?;
    // Circle outline.
    imgproc::circle(
        img,
        core::Point::new(cxi, cyi),
        r,
        white,
        5,
        imgproc::LINE_AA,
        0,
    )?;
    imgproc::circle(
        img,
        core::Point::new(cxi, cyi),
        r,
        black,
        3,
        imgproc::LINE_AA,
        0,
    )?;

    Ok(())
}

fn build_detected(data: &Bytes, presets: HsvPresets) -> opencv::Result<Bytes> {
    let bgr = decode_jpeg(data)?;

    let orange_mask = hsv_mask(&bgr, presets.orange.into())?;
    let green_params: HsvParams = presets.green.into();
    let green2_params: Option<HsvParams> = presets.green2.map(Into::into);

    // Build combined green mask (with green2 OR'd in) for the yellow overlay.
    let mut green_combined = hsv_mask(&bgr, green_params)?;
    if let Some(p2) = green2_params {
        let mask2 = hsv_mask(&bgr, p2)?;
        let mut tmp = Mat::default();
        core::bitwise_or(&green_combined, &mask2, &mut tmp, &core::no_array())?;
        green_combined = tmp;
    }

    // Combined orange+green mask for the yellow overlay.
    let mut combined = Mat::default();
    core::bitwise_or(
        &orange_mask,
        &green_combined,
        &mut combined,
        &core::no_array(),
    )?;

    // Build yellow overlay on the combined mask.
    let yellow = Mat::new_rows_cols_with_default(
        bgr.rows(),
        bgr.cols(),
        core::CV_8UC3,
        Scalar::new(0.0, 255.0, 255.0, 0.0),
    )?;
    let mut blended = Mat::default();
    core::add_weighted(&bgr, 0.5, &yellow, 0.5, 0.0, &mut blended, -1)?;

    let mut mask_3ch = Mat::default();
    imgproc::cvt_color(&combined, &mut mask_3ch, imgproc::COLOR_GRAY2BGR, 0)?;
    let mut mask_inv = Mat::default();
    core::bitwise_not(&mask_3ch, &mut mask_inv, &core::no_array())?;

    let mut blend_part = Mat::default();
    core::bitwise_and(&blended, &mask_3ch, &mut blend_part, &core::no_array())?;
    let mut orig_part = Mat::default();
    core::bitwise_and(&bgr, &mask_inv, &mut orig_part, &core::no_array())?;
    let mut result = Mat::default();
    core::bitwise_or(&blend_part, &orig_part, &mut result, &core::no_array())?;

    // Draw crosshairs: 4 largest orange stickers (fitEllipse centres).
    for (cx, cy, r) in orange_centroids(&orange_mask, 4)? {
        draw_circle_crosshair(&mut result, cx, cy, r)?;
    }

    // Green puck: moments centroid on the closed combined mask.
    if let Some((cx, cy)) = green_centroid(&bgr, green_params, green2_params)? {
        draw_circle_crosshair(&mut result, cx, cy, 30.0)?;
    }

    encode_jpeg(&result)
}

/// `GET /still/detected` — yellow overlay on both orange + green blobs, with a
/// crosshair + circle drawn at the 3 largest orange and 1 largest green blob.
pub async fn detected_handler(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    {
        let mode = state.mode.read().await;
        if !matches!(*mode, ServerMode::Setup) {
            return StatusCode::CONFLICT.into_response();
        }
    }
    let Some(data) = state.still_jpeg.read().await.clone() else {
        return (StatusCode::NOT_FOUND, "No still captured yet").into_response();
    };
    let presets = state.hsv_presets.read().await.clone();

    match tokio::task::spawn_blocking(move || build_detected(&data, presets)).await {
        Ok(Ok(out)) => jpeg_response(out),
        Ok(Err(e)) => {
            tracing::error!("detected overlay error: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
        Err(e) => {
            tracing::error!("spawn_blocking panicked: {e}");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}
