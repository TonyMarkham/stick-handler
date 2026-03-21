# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WebRTC video streaming app in Rust: captures H.264 video from a Raspberry Pi 5 camera via `rpicam-vid` subprocess and streams it to a Meta Quest 3 browser for AR viewing via WebXR.

## Commands

```bash
# Build
cargo build
cargo build --release   # First build on Pi 5 is slow (~10+ min)

# Check / lint
cargo check
cargo check -p signal-server   # Check specific workspace member
cargo clippy

# Run tests
cargo test
cargo test -p signal-server    # Test specific package

# Run the server (requires rpicam-vid on Pi 5)
cargo run -p server
RUST_LOG=info,server=debug cargo run -p server
```

Server listens on `http://0.0.0.0:8080`. The browser frontend is served at `/`, WebSocket signaling at `/ws`.

## Architecture

### Data Flow

```
rpicam-vid (H.264 stdout)
    → SyncIoBridge (sync wrapper around async ChildStdout)
        → H264Reader::next_nal()  [runs in spawn_blocking]
            → broadcast::Sender<Bytes>
                → TrackLocalStaticSample::write_sample()
                    → webrtc-rs RTP packetization → RTCPeerConnection
                        → WebXR <video> on Quest 3
```

Signaling flow: browser creates offer → server answers (trickle-ICE via WebSocket).

### Workspace Members

| Crate | Path | Role |
|---|---|---|
| `server` | `backend/server` | Binary: Axum HTTP + WS server, camera, WebRTC |
| `signal-server` | `backend/web-rtc/signal` | Library: signaling types and session store |

**Crate name disambiguation:** `signal` (workspace dep v0.7.0) = OS signals (SIGINT/SIGTERM); `signal-server` = the local WebRTC signaling library.

### Key Source Files

- `backend/server/src/main.rs` — Axum router, `AppState { camera_tx, stun_urls }`, WebSocket upgrade, WS read/write tasks, graceful shutdown
- `backend/server/src/camera.rs` — spawns `rpicam-vid`, bridges async stdout to sync `H264Reader` via `SyncIoBridge` in `spawn_blocking`, broadcasts NAL units as `Bytes`
- `backend/server/src/webrtc_handler.rs` — `create_peer_connection()` (H.264-only `MediaEngine`, payload type 96), `handle_peer_session()` (offer/answer/ICE loop + camera sample writer)
- `backend/server/src/error/mod.rs` — `ServerError { Camera, WebRtc }` with `error_location::ErrorLocation`
- `backend/web-rtc/signal/src/message.rs` — `SignalMessage` tagged enum (`#[serde(tag = "type", rename_all = "kebab-case")]`)
- `backend/web-rtc/signal/src/session.rs` — `SessionStore` wrapping `Arc<RwLock<HashMap<SessionId, Session>>>`
- `backend/server/static/index.html` — browser frontend: WebRTC offer/answer, trickle-ICE, WebXR `immersive-ar` + `dom-overlay`

### Error Pattern

Errors use constructor functions with `#[track_caller]` to capture source location:

```rust
// Define variant
ServerError::Camera { message: String, location: ErrorLocation }

// Create error (captures call site automatically)
return Err(camera_error(format!("Failed to spawn {COMMAND}: {e}")));
```

### H.264 Codec Parameters

Registered with `webrtc-rs` `MediaEngine`:
- `mime_type`: `"video/H264"`
- `sdp_fmtp_line`: `"level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f"`
- `clock_rate`: `90_000`, `payload_type`: `96`

### Detection Pipeline

**Orange (calibration only, not time-critical)**
- `calibration_handler.rs` → `compute_homography()` → `orange_centroids()` in `still_handler.rs`
- 4 flat floor stickers project as ellipses under angled camera → `fitEllipse` for accurate centres
- `MIN_ORANGE_AREA_PX2 = 30.0` (stickers are small in pixel area)
- Confirmed HSV: `h:5-25 / s:200-255 / v:200-255`
- `POST /calibration/recalc` requires exactly 4 blobs visible + real-world XZ coords in body

**Green puck (game hot path, target 30fps)**
- `mjpeg_pipeline.rs` → `start_detection_loop()` → `green_centroid()` in `still_handler.rs`
- Puck is ~1" tall, projects as irregular shape — moments centroid, no shape assumption
- Optional `green2: Option<HsvRange>` OR'd with `green` to catch specular/washed-out regions
- Morph close (kernel=25, 2 iterations) applied after combined mask — consider gating on `green2.is_some()` if 30fps is tight
- `MIN_GREEN_AREA_PX2 = 500.0`
- Confirmed HSV: `h:75-90 / s:30-255 / v:30-255`
- `PUT/DELETE /hsv/green2` to set/clear the second green range

**HSV presets persistence**
- Server saves to `hsv_presets.json` relative to the binary (e.g. `target/release/hsv_presets.json`)
- Unity-side presets at `Unity/Projects/Stick Handle Trainer/Assets/StreamingAssets/hsv_presets.json` — different format (banks/presets array, camelCase keys), sent to server via `PUT /hsv/*`
- Pi hostname: `tony@test-pi`, server at `http://test-pi:8080`

### Key Design Constraints

- `H264Reader` is **synchronous** — must run inside `tokio::task::spawn_blocking` with `SyncIoBridge`
- `rpicam-vid --inline` flag is **required** — ensures SPS/PPS NAL units precede each IDR so late-joining viewers get a valid stream
- STUN server: `stun:stun.l.google.com:19302` (sufficient for Pi ↔ Quest on the same WiFi)
- Quest 3 browser is Chromium-based and supports H.264 hardware decode natively
