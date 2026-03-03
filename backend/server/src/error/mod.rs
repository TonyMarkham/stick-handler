use error_location::ErrorLocation;
use std::{panic::Location, result::Result as StdResult};
use thiserror::Error as ThisError;

#[derive(ThisError, Debug)]
pub enum ServerError {
    #[error("Camera error: {message} {location}")]
    Camera {
        message: String,
        location: ErrorLocation,
    },

    #[error("WebRtc error: {message} {location}")]
    WebRtc {
        message: String,
        location: ErrorLocation,
    },
}

#[track_caller]
pub(crate) fn camera_error(message: impl Into<String>) -> ServerError {
    ServerError::Camera {
        message: message.into(),
        location: ErrorLocation::from(Location::caller()),
    }
}

#[track_caller]
pub(crate) fn webrtc_error(message: impl Into<String>) -> ServerError {
    ServerError::WebRtc {
        message: message.into(),
        location: ErrorLocation::from(Location::caller()),
    }
}

pub type ServerResult<T> = StdResult<T, ServerError>;
