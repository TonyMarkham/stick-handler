use bytes::Bytes;
use std::sync::Arc;
use tokio::sync::RwLock;

#[derive(Clone)]
pub struct AppState {
    pub stun_urls: Vec<String>,
    pub still_jpeg: Arc<RwLock<Option<Bytes>>>,
}

impl AppState {
    pub fn new(stun_urls: Vec<String>) -> Self {
        Self {
            stun_urls,
            still_jpeg: Arc::new(RwLock::new(None)),
        }
    }
}
