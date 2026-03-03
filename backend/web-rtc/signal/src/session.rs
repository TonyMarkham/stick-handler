use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::{RwLock, mpsc};
use uuid::Uuid;

use crate::message::SignalMessage;

pub type SessionId = Uuid;
pub type SignalTx = mpsc::UnboundedSender<SignalMessage>;

#[derive(Debug, Clone)]
pub struct Session {
    pub id: SessionId,
    pub signal_tx: SignalTx,
}

#[derive(Debug, Default, Clone)]
pub struct SessionStore {
    sessions: Arc<RwLock<HashMap<SessionId, Session>>>,
}

impl SessionStore {
    pub fn new() -> Self {
        Self::default()
    }

    pub async fn insert(&self, session: Session) -> SessionId {
        let id = session.id;
        self.sessions.write().await.insert(id, session);
        id
    }

    pub async fn remove(&self, id: &SessionId) {
        self.sessions.write().await.remove(id);
    }

    pub async fn get(&self, id: &SessionId) -> Option<Session> {
        self.sessions.read().await.get(id).cloned()
    }

    pub async fn ids(&self) -> Vec<SessionId> {
        self.sessions.read().await.keys().copied().collect()
    }
}
