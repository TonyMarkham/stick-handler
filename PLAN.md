# WebRTC Pi 5 Camera → Meta Quest 3 AR Streaming

## Context
Build a WebRTC video streaming app in Rust that captures H.264 video from a Raspberry Pi 5 camera via `rpicam-vid` subprocess and streams it to a Meta Quest 3 browser for AR viewing using WebXR.

---

## Architecture

```
Pi 5                                    Quest 3 Browser
  rpicam-vid (H.264 stdout)               RTCPeerConnection
      ↓ spawn_blocking + SyncIoBridge         ↑ video track
  H264Reader (NAL units)               WebSocket /ws (signaling)
      ↓ broadcast::Sender<Bytes>            WebXR immersive-ar
  TrackLocalStaticSample
      ↓ webrtc-rs RTP packetization
  RTCPeerConnection ←──── WebSocket signaling ────→
```

**Signaling flow:** browser offers, server answers (standard trickle-ICE).

---

## Current State (Already Done)

- `backend/server/src/error/mod.rs` — `ServerError { Camera { message, location }, WebRtc { message, location } }` + `pub type Result<T>`
- `backend/web-rtc/signal/src/error/mod.rs` — `SignalError { Signal { message, location } }` + `pub type Result<T>`
- `backend/web-rtc/signal/src/lib.rs` — `pub mod error; pub use error::{Result, Result as SignalResult}`
- All workspace deps in place (`axum`, `webrtc 0.17.1`, `webrtc-media 0.17.1`, `error-location`, `thiserror`, etc.)

---

## Workspace Fixes Needed (Before Coding)

Edit `/home/tony/git/stick-handler/Cargo.toml`:
1. `serde` → add `features = ["derive"]`
2. `tokio` → add `features = ["full"]`

Edit `backend/server/Cargo.toml`:
3. Add `signal-server = { workspace = true }` (the local WebRTC signaling crate; `signal` is the OS signals crate)

---

## File Structure (What Still Needs Creating)

```
backend/
├── server/
│   ├── static/
│   │   └── index.html          (create: HTML/JS/WebXR frontend)
│   └── src/
│       ├── main.rs             (update: Axum router, AppState, WS upgrade)
│       ├── camera.rs           (create: rpicam-vid subprocess + NAL broadcast)
│       └── webrtc_handler.rs   (create: RTCPeerConnection, offer/answer, sample writing)
└── web-rtc/signal/src/
    ├── message.rs              (create: SignalMessage serde enum)
    └── session.rs              (create: SessionId, Session, SessionStore)
```

`signal/lib.rs` needs `pub mod message; pub mod session;` added.

---

## Crate Names

- `signal-server` — local WebRTC signaling library (`backend/web-rtc/signal`)
- `signal` (v0.7.0) — external OS signal handling (SIGINT/SIGTERM graceful shutdown)
- `error_location::ErrorLocation` — used in all error variants for source location

---

## Signaling Protocol (JSON)

```jsonc
// Client → Server
{ "type": "offer", "sdp": "..." }
{ "type": "ice-candidate", "candidate": "...", "sdp_mid": "0", "sdp_mline_index": 0 }

// Server → Client
{ "type": "answer", "sdp": "..." }
{ "type": "ice-candidate", "candidate": "...", "sdp_mid": "0", "sdp_mline_index": 0 }
{ "type": "error", "message": "..." }
```

`SignalMessage` uses `#[serde(tag = "type", rename_all = "kebab-case")]`.

---

## Phase 1: `signal-server` crate — `message.rs` + `session.rs`

**`message.rs`** — `SignalMessage` tagged enum:
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "kebab-case")]
pub enum SignalMessage {
    Offer { sdp: String },
    Answer { sdp: String },
    IceCandidate { candidate: String, sdp_mid: Option<String>, sdp_mline_index: Option<u16> },
    Error { message: String },
}
```

**`session.rs`** — `SessionStore` wrapping `Arc<RwLock<HashMap<SessionId, Session>>>`. Each `Session` holds `SignalTx = mpsc::UnboundedSender<SignalMessage>`.

`lib.rs` additions: `pub mod message; pub mod session;` + re-exports.

---

## Phase 2: `camera.rs`

```
rpicam-vid --codec h264 --width 1920 --height 1080 --framerate 30 --inline --timeout 0 --output -
```

Threading model:
```
tokio::process::ChildStdout (AsyncRead)
    → SyncIoBridge (std::io::Read)  [tokio_util::io::SyncIoBridge]
        → H264Reader::new(bridge, 1_048_576)  [webrtc_media::io::h264_reader]
            → spawn_blocking { loop { next_nal() } }
                → broadcast::Sender<Bytes>
```

```rust
pub fn start_camera(width: u32, height: u32, framerate: u32) -> broadcast::Sender<Bytes>
```

Errors map to `ServerError::Camera { message, location }`.

---

## Phase 3: `webrtc_handler.rs`

**MediaEngine:** H.264 only, payload type 96:
```rust
mime_type: "video/H264"
sdp_fmtp_line: "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f"
clock_rate: 90_000
```

**`create_peer_connection(stun_urls: Vec<String>) -> Result<(Arc<RTCPeerConnection>, Arc<TrackLocalStaticSample>)>`**

**`handle_peer_session(ws_rx, ws_tx, nal_rx, stun_urls) -> Result<()>`** flow:
1. `create_peer_connection()`
2. Register `on_ice_candidate` → send `SignalMessage::IceCandidate` to `ws_tx`
3. Register `on_peer_connection_state_change` → abort on `Failed`/`Disconnected`
4. Receive `Offer` from `ws_rx`
5. `set_remote_description` → `create_answer` → `set_local_description`
6. Send `SignalMessage::Answer` to `ws_tx`
7. Spawn camera task: `loop { nal_rx.recv() → track.write_sample(Sample { data, duration: 33ms }) }`
8. Loop: receive ICE candidates from `ws_rx` → `add_ice_candidate`

Errors map to `ServerError::WebRtc { message, location }`.

---

## Phase 4: `main.rs`

```rust
#[derive(Clone)]
struct AppState {
    camera_tx: broadcast::Sender<Bytes>,
    stun_urls: Vec<String>,
}
// Routes: GET / → index.html, GET /ws → WebSocket upgrade
```

`handle_socket` spawns three tasks per connection:
- WS read → JSON parse → `SignalMessage` → mpsc channel
- mpsc channel → JSON serialize → WS write
- `webrtc_handler::handle_peer_session(ws_rx, ws_tx, camera_tx.subscribe(), stun_urls)`

Graceful shutdown: `tokio::signal::ctrl_c()` or `signal` crate for SIGTERM.

---

## Phase 5: `static/index.html`

```javascript
// Browser offers, server answers
pc.addTransceiver('video', { direction: 'recvonly' });
const offer = await pc.createOffer();
await pc.setLocalDescription(offer);
ws.send(JSON.stringify({ type: 'offer', sdp: offer.sdp }));

pc.ontrack = ({ streams }) => { videoEl.srcObject = streams[0]; };
```

WebXR AR via `dom-overlay` (simplest path on Quest 3 — browser composites `<video>` over passthrough):
```javascript
const session = await navigator.xr.requestSession('immersive-ar', {
  requiredFeatures: ['local'],
  optionalFeatures: ['dom-overlay'],
  domOverlay: { root: document.body }
});
```

---

## Implementation Sequence

1. Workspace + server Cargo.toml fixes (serde derive, tokio full, signal-server dep)
2. `signal-server`: `message.rs` → `session.rs` → update `lib.rs` → `cargo check -p signal-server`
3. `server`: `camera.rs` → log NAL sizes to verify frames
4. `server`: `webrtc_handler.rs`
5. `server`: update `main.rs`
6. `static/index.html`
7. `cargo check` → `cargo build --release` (first build is slow on Pi 5)
8. Test desktop Chrome → Quest 3 browser → Quest 3 WebXR AR mode

---

## Key Pitfalls

- **`H264Reader` is sync** — must run in `spawn_blocking` with `SyncIoBridge`
- **`rpicam-vid --inline`** — required so late-joining viewers receive SPS/PPS with each IDR
- **`webrtc 0.17.1` API** — verify `TrackLocalStaticSample`, `MediaEngine`, `RTCPeerConnection` type paths against this specific version
- **ICE on LAN** — `stun:stun.l.google.com:19302` + link-local candidates sufficient for Pi↔Quest on same WiFi
- **Quest 3 browser** — Chromium-based, H.264 hardware decode supported natively
- **`serde` derive feature** — workspace dep is missing it; `#[derive(Serialize, Deserialize)]` will fail without it
