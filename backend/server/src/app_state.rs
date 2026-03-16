#[derive(Clone)]
pub struct AppState {
    pub stun_urls: Vec<String>,
}

impl AppState {
    pub fn new(stun_urls: Vec<String>) -> Self {
        Self { stun_urls }
    }
}
