use std::result::Result as StdResult;
use error_location::ErrorLocation;
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
