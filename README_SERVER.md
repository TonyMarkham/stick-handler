# server

Axum HTTP server that captures H.264 video from a Raspberry Pi 5 camera and streams it to a
Meta Quest 3 browser via WebRTC.

## Requirements

- Raspberry Pi 5 with `rpicam-vid` installed
- Rust toolchain (see workspace root for minimum supported version [MSRV])

## Running

  ```bash
  # Development
  RUST_LOG=info,server=debug cargo run -p server

  # Release (first build is slow — ~10+ min on Pi 5)
  cargo build --release -p server
  ./target/release/server
  ```

  ```bash
  RUST_LOG=info,server=debug ./target/release/server
  ```

Server listens on `http://0.0.0.0:8080`.

## Endpoints

| Method | Path | Description |
  |--------|------|-------------|
| `GET` | `/` | Browser frontend (WebXR + WebRTC client) |
| `GET` | `/ws` | WebSocket signaling endpoint |
| `POST` | `/camera/start` | Start the `rpicam-vid` subprocess |
| `POST` | `/camera/stop` | Stop the `rpicam-vid` subprocess |
| `GET` | `/camera/status` | `{"running": true\|false}` |

### Camera API

The camera starts **idle**. Start it explicitly before connecting a WebRTC client.

  ```bash
  curl -X POST http://test-pi:8080/camera/start    # 200 Camera started
  curl      http://test-pi:8080/camera/status      # {"running":true}
  curl -X POST http://test-pi:8080/camera/stop     # 200 Camera stopped
  ```

If a WebSocket client connects while the camera is not running, the server sends
`{"type":"error","message":"Camera is not running"}` and closes the connection.

## Architecture

  ```
  rpicam-vid (H.264 stdout)
      → SyncIoBridge (sync wrapper around async ChildStdout)
          → H264Reader::next_nal()  [runs in spawn_blocking]
              → broadcast::Sender<Bytes>
                  → TrackLocalStaticSample::write_sample()
                      → webrtc-rs RTP packetization → RTCPeerConnection
                          → WebXR <video> on Quest 3
  ```

### Source layout

| File | Role |
  |------|------|
| `main.rs` | Axum router, HTTP/WS handlers, startup and graceful shutdown |
| `app_state.rs` | `AppState` (shared camera handle + STUN URLs), crash monitor |
| `camera_handle.rs` | `CameraHandle` — wraps subprocess, exposes `stop()` and `subscribe()` |
| `camera.rs` | Spawns `rpicam-vid`, bridges async stdout → sync `H264Reader` |
| `webrtc_handler.rs` | `RTCPeerConnection` lifecycle, offer/answer/ICE, sample writing |
| `error/mod.rs` | `ServerError` with call-site location capture |

## Codec

H.264 registered with `webrtc-rs`:

- `mime_type`: `video/H264`
- `sdp_fmtp_line`: `level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f`
- `clock_rate`: `90000`, `payload_type`: `96`

## Graceful shutdown

Both `SIGINT` (Ctrl+C) and `SIGTERM` (systemd) are handled. On either signal:

1. `axum::serve` stops accepting new connections.
2. The camera subprocess receives `SIGKILL` — no orphaned `rpicam-vid` processes.