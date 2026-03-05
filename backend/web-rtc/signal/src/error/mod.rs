use error_location::ErrorLocation;
use std::result::Result as StdResult;
use thiserror::Error as ThisError;

#[derive(ThisError, Debug)]
pub enum SignalError {
    #[error("Signal error: {message} {location}")]
    Signal {
        message: String,
        location: ErrorLocation,
    },
}

pub type SignalServerResult<T> = StdResult<T, SignalError>;
