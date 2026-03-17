# stick-handler — Rust Server

Axum HTTP server running on a Raspberry Pi 5. Captures camera video via `rpicam-vid`, streams it to a Meta Quest 3 via WebRTC, and runs real-time green blob detection for AR gameplay.

---

## Requirements

- Raspberry Pi 5 (or compatible) running Ubuntu Server or Desktop (arm64)
- `rpicam-apps` installed (`rpicam-vid`, `rpicam-still`)
- Rust toolchain (see workspace `Cargo.toml` for MSRV)
- OpenCV 4.x (`libopencv-dev`, `libclang-dev`, `cmake`, `pkg-config`)

```bash
sudo apt install libopencv-dev libclang-dev cmake pkg-config rpicam-apps
```

---

## Building & Running

```bash
# Check (fast, no binary)
cargo check -p server

# Debug build + run
RUST_LOG=info,server=debug cargo run -p server

# Release build (first build on Pi 5 is slow — ~10+ min)
cargo build --release -p server
RUST_LOG=info,server=debug ./target/release/server
```

Server listens on `http://0.0.0.0:8080`.

---

## Server Modes

The server operates in three mutually exclusive modes. **Mode is the single source of truth for camera ownership** — exactly one pipeline is active at any time.

| Mode | Camera | Entered via |
|---|---|---|
| **Setup** | None, or WebRTC on demand | Default / on startup |
| **WorldCalibration** | `rpicam-vid --codec mjpeg` | `POST /calibration/start` |
| **Tracking** | `rpicam-vid --codec mjpeg` | `POST /tracking/start` |

Wrong-mode requests return `409 Conflict`. Mode transitions are always explicit — the server never auto-transitions.

---

## API Reference

### Mode Requirements

| Endpoint | Required Mode |
|---|---|
| `GET /ws` | Setup |
| `POST /still/capture` | Setup |
| `GET /still/*` | Setup |
| `PUT /hsv/*` | Any |
| `GET /hsv` | Any |
| `POST /calibration/start` | Setup |
| `POST /calibration/recalc` | WorldCalibration |
| `POST /calibration/end` | WorldCalibration |
| `POST /tracking/start` | Setup |
| `POST /tracking/stop` | Tracking |
| `GET /tracking` (WS) | WorldCalibration or Tracking |

---

### WebRTC Stream — Setup mode

**`GET /ws`** — WebSocket signaling for WebRTC video stream.

Trickle-ICE signaling. Browser creates offer, server answers. The browser frontend at `GET /` implements the full client.

```
Browser → Offer SDP  →  Server
Server  → Answer SDP →  Browser
Server  ↔ ICE candidates (trickle)
```

Codec negotiation is automatic: H.264 for Quest 3 / NVIDIA clients, VP8 fallback for AMD/Intel. The camera subprocess spawns on connection and is killed on disconnect.

---

### Still Capture & HSV Calibration — Setup mode

Used by the Unity calibration UI to tune HSV filter presets before entering a tracking mode.

**`POST /still/capture`**

Fires `rpicam-still` and stores the resulting JPEG in memory. Must be called before any `GET /still/*` endpoint.

```bash
curl -X POST http://pi:8080/still/capture
```

---

**`GET /still/original`**

Returns the raw captured JPEG.

---

**`GET /still/mask?h_min=&h_max=&s_min=&s_max=&v_min=&v_max=`**

Returns a black-and-white JPEG mask: white where pixels fall within the HSV range, black elsewhere. Includes `X-Blob-Count` response header.

```bash
curl "http://pi:8080/still/mask?h_min=0&h_max=20&s_min=230&s_max=255&v_min=230&v_max=255" \
  -o mask.jpg -D -
# X-Blob-Count: 4
```

---

**`GET /still/overlay?h_min=&h_max=&s_min=&s_max=&v_min=&v_max=`**

Returns the original image with matching pixels blended 50% toward yellow. Includes `X-Blob-Count` response header.

---

**`GET /still/detected`**

Returns the original image with a yellow overlay on all detected blobs (3 largest orange + 1 largest green), with crosshair+circle annotations at each blob centroid. Uses saved HSV presets.

---

### HSV Presets — Any mode

Presets are persisted to `hsv_presets.json` and survive server restarts.

**`GET /hsv`** — Returns both presets as JSON.

```json
{
  "green":  { "h_min": 75, "h_max": 90,  "s_min": 150, "s_max": 255, "v_min": 30,  "v_max": 110 },
  "orange": { "h_min": 0,  "h_max": 20,  "s_min": 230, "s_max": 255, "v_min": 230, "v_max": 255 }
}
```

**`PUT /hsv/green`** — Update the green preset (JSON body, same shape as above).

**`PUT /hsv/orange`** — Update the orange preset.

---

### World Calibration Mode

**`POST /calibration/start`** — Requires Setup mode. Kills any live WebRTC session, spawns `rpicam-vid --codec mjpeg` at 1920×1080 @ 30fps, starts the green blob detection loop, and enters WorldCalibration mode.

**`POST /calibration/end`** — Requires WorldCalibration mode. Kills MJPEG pipeline, returns to Setup.

---

**`POST /calibration/recalc`** — Requires WorldCalibration mode.

Takes the 4 virtual cylinder positions (Unity world XZ coords) and the current MJPEG frame, detects 4 orange blobs, and computes a perspective homography mapping pixel coordinates to world coordinates.

Request:
```json
{
  "cylinders": [
    { "label": 1, "x": -0.15, "z":  0.15 },
    { "label": 2, "x":  0.15, "z":  0.15 },
    { "label": 3, "x":  0.15, "z": -0.15 },
    { "label": 4, "x": -0.15, "z": -0.15 }
  ]
}
```

Response:
```json
{ "matrix": [m00, m01, m02, m10, m11, m12, m20, m21, m22] }
```

The `matrix` is a 3×3 perspective homography (row-major, float64). Apply it as:

```
[wx, wy, w] = M × [px, py, 1]
world_pos = (wx/w, wy/w)   →  Vector3(wx/w, 0, wy/w)
```

**Errors:**
- `409 Conflict` — not in WorldCalibration mode
- `422 Unprocessable Entity` — detected blob count ≠ 4 (includes count in body)
- `503 Service Unavailable` — no MJPEG frame available yet (retry after ~100ms)

**Blob matching algorithm:**
1. Detect orange blobs → 4 centroids
2. Sort clockwise from north (atan2(dx, −dy) from mean centroid)
3. Compare winding of blob triangle [0,1,2] vs cylinder triangle [1,2,3] via cross product sign
4. Same sign → forward assignment; different sign → reverse blob list
5. `opencv::find_homography` on the 4 matched pairs (exactly determined, no RANSAC)

---

### Tracking Mode

**`POST /tracking/start`** — Requires Setup mode. Same pipeline startup as calibration, enters Tracking mode.

**`POST /tracking/stop`** — Requires Tracking mode. Kills MJPEG pipeline, returns to Setup.

---

**`GET /tracking`** — WebSocket. Valid in WorldCalibration **or** Tracking mode.

Server-push only. Streams one JSON message per frame whenever a green blob is detected. No message is sent for frames where no blob is found.

```json
{ "x": 960.5, "y": 540.2 }
```

Coordinates are pixel positions in the 1920×1080 frame. Connection closes naturally when the MJPEG pipeline is killed (mode transition or server shutdown).

```bash
# Watch the centroid stream
websocat ws://pi:8080/tracking
```

---

## Data Flow

### WebRTC (Setup mode)

```
rpicam-vid (H.264/VP8 stdout)
    → SyncIoBridge          [sync wrapper around async ChildStdout]
        → H264Reader / IVFReader::next_nal()   [spawn_blocking]
            → broadcast::Sender<Bytes>
                → TrackLocalStaticSample::write_sample()
                    → webrtc-rs RTP packetization
                        → RTCPeerConnection → Quest 3 browser
```

### MJPEG Detection (WorldCalibration / Tracking)

```
rpicam-vid --codec mjpeg (stdout)
    → SyncIoBridge / byte scanner (FF D9 EOI boundary)   [spawn_blocking]
        → broadcast::Sender<Bytes>   [raw JPEG frames]
            → detection loop (async task)
                → spawn_blocking: decode JPEG → HSV mask → blob_circles
                    → broadcast::Sender<(f32, f32)>   [centroids]
                        → GET /tracking  WebSocket subscribers
                        → latest_frame cache  →  POST /calibration/recalc
```

---

## Source Layout

| File | Role |
|---|---|
| `main.rs` | Axum router, WebRTC WS handler, graceful shutdown |
| `app_state.rs` | `AppState`, `ServerMode`, `MjpegPipeline`, `stop_active_pipeline` |
| `camera_handle.rs` | `CameraHandle` — wraps subprocess, `stop()` + `subscribe()` |
| `camera.rs` | Spawns `rpicam-vid` (H.264 or VP8), bridges stdout → NAL broadcast |
| `mjpeg_pipeline.rs` | Spawns `rpicam-vid --codec mjpeg`, JPEG frame parser, detection loop |
| `webrtc_handler.rs` | `RTCPeerConnection` lifecycle, offer/answer/ICE, sample writing |
| `calibration_handler.rs` | `POST /calibration/start\|end\|recalc`, homography computation |
| `tracking_handler.rs` | `POST /tracking/start\|stop`, `GET /tracking` WebSocket |
| `still_handler.rs` | `POST /still/capture`, `GET /still/*`, OpenCV mask/overlay/detect |
| `hsv_handler.rs` | `GET /hsv`, `PUT /hsv/green\|orange`, preset persistence |
| `error/mod.rs` | `ServerError` with `#[track_caller]` call-site location capture |

---

## HSV Presets File

`hsv_presets.json` is read on startup and written on every `PUT /hsv/*` call. If absent, safe defaults are used.

```json
{
  "green":  { "h_min": 75, "h_max": 90,  "s_min": 150, "s_max": 255, "v_min": 30,  "v_max": 110 },
  "orange": { "h_min": 0,  "h_max": 20,  "s_min": 230, "s_max": 255, "v_min": 230, "v_max": 255 }
}
```

Place it next to the binary or in the working directory.

---

## Error Handling

Errors use `#[track_caller]` constructor functions that capture the source file, line, and column automatically:

```
Camera error: Failed to spawn rpicam-vid: No such file or directory
  at backend/server/src/camera.rs:88:18
```

---

## Graceful Shutdown

Both `SIGINT` (Ctrl+C) and `SIGTERM` (systemd `stop`) are handled:

1. Axum stops accepting new connections and drains in-flight requests.
2. Any live camera subprocess receives `SIGKILL` — no orphaned `rpicam-vid` processes.

---

## Codec Details

### H.264 (Quest 3, NVIDIA Windows)

| Parameter | Value |
|---|---|
| `mime_type` | `video/H264` |
| `sdp_fmtp_line` | `level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=640c28` |
| `clock_rate` | 90 000 Hz |
| `payload_type` | 96 |
| Resolution | 1920×1080 @ 30 fps |

### VP8 (AMD/Intel Windows fallback)

| Parameter | Value |
|---|---|
| `mime_type` | `video/VP8` |
| `clock_rate` | 90 000 Hz |
| `payload_type` | 97 |
| Resolution | 640×360 @ 30 fps |

Codec is auto-detected from the SDP offer — no client configuration required.

### MJPEG (WorldCalibration / Tracking)

`rpicam-vid --codec mjpeg` at 1920×1080 @ 30 fps. Frames are delimited by JPEG SOI (`FF D8`) / EOI (`FF D9`) markers. If the Pi 5 CPU cannot sustain 30 fps at 1080p, reduce to 1280×720 in `mjpeg_pipeline.rs`.

---

## STUN

`stun:stun.l.google.com:19302` — sufficient for Pi ↔ Quest on the same WiFi network. No TURN server is required for LAN-only use.
