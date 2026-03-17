# Server Game Loop Design

## Modes

The server operates in three mutually exclusive modes. Mode is the **single source of truth** for camera ownership — you always know exactly what is using the hardware by reading it.

| Mode | Camera | Active When |
|---|---|---|
| **Setup** | None, or WebRTC (`rpicam-vid`) | Default. WebRTC stream + HSV calibration endpoints live. |
| **World Calibration** | MJPEG (`rpicam-vid --codec mjpeg`) | User is placing cylinders on orange stickers. Green blob centroid stream running. `POST /calibration/recalc` available. |
| **Tracking** | MJPEG (`rpicam-vid --codec mjpeg`) | Gameplay. Green blob centroid stream running. |

Mode transitions (all require the stated precondition mode — wrong mode returns `409 Conflict`):
- Setup → World Calibration: `POST /calibration/start`
- World Calibration → Setup: `POST /calibration/end`
- Setup → Tracking: `POST /tracking/start`
- Tracking → Setup: `POST /tracking/stop`

---

## AppState Camera Fields

Three fields in `AppState` govern camera state. Only one pipeline is active at a time, enforced by mode transitions.

```rust
mode: Arc<RwLock<ServerMode>>                    // single source of truth
webrtc_camera: Arc<Mutex<Option<CameraHandle>>>  // Some only while a /ws session is live
mjpeg_pipeline: Arc<Mutex<Option<MjpegPipeline>>> // Some when mode == WorldCalibration | Tracking
```

`MjpegPipeline` holds: the camera `CameraHandle`, a `broadcast::Sender<(f32, f32)>` for centroids, and an `Arc<RwLock<Option<Bytes>>>` for the latest decoded frame (used by `/calibration/recalc`).

### `stop_active_pipeline`

Internal helper called before every mode transition that acquires the camera. Kills whichever of `webrtc_camera` or `mjpeg_pipeline` is populated and resets both to `None`. This ensures hardware is free before the new pipeline spawns, and handles the case where a WebRTC or tracking session was not cleanly closed by the client.

---

## Endpoint Mode Requirements

All endpoints that require the camera, or are only meaningful in a specific mode, enforce their precondition and return `409 Conflict` if not met. The caller (Quest / Unity) owns all mode transitions explicitly — the server never auto-transitions on behalf of a request.

| Endpoint | Required Mode |
|---|---|
| `GET /ws` (WebRTC) | Setup |
| `POST /still/capture` | Setup |
| `GET /still/*` | Setup |
| `PUT /hsv/*` | any |
| `GET /hsv` | any |
| `POST /calibration/start` | Setup |
| `POST /calibration/recalc` | WorldCalibration |
| `POST /calibration/end` | WorldCalibration |
| `POST /tracking/start` | Setup |
| `POST /tracking/stop` | Tracking |
| `GET /tracking` (WS) | WorldCalibration \| Tracking |

**Note:** WebRTC is used only during Setup mode to let the user view a live stream for camera focus adjustment. It is not a primary feature — `/ws` is simply refused if the mode is not Setup.

---

## Setup Mode (default)

- No camera process running by default
- WebRTC available on demand: `/ws` upgrades spawn `rpicam-vid` (H.264/VP8), store the `CameraHandle` in `AppState`, and kill + clear it on disconnect
- HSV calibration endpoints available (`/still/*`, `/hsv/*`) — `still/capture` uses `rpicam-still`, which also requires exclusive camera access, so Setup mode is required

---

## World Calibration Mode

Entered via `POST /calibration/start`, exited via `POST /calibration/end`.

### Start (`POST /calibration/start`)
1. Require mode == Setup; return `409` otherwise
2. Call `stop_active_pipeline` (kills any live WebRTC session defensively)
3. Spawn `rpicam-vid --codec mjpeg` at 1920×1080 @ 30fps
4. Launch detection loop (same as Tracking — see below)
5. Set mode = WorldCalibration; return `200 OK`

### Stop (`POST /calibration/end`)
1. Require mode == WorldCalibration; return `409` otherwise
2. Kill MJPEG pipeline; clear `mjpeg_pipeline`
3. Set mode = Setup; return `200 OK`

Uses the **same MJPEG pipeline and `/tracking` WebSocket** as Tracking Mode — green blob centroid stream is live so the blob indicator cylinder moves in real time while the user positions the orange stickers.

`POST /calibration/recalc` is only valid in this mode. See `World-Calibration.md` for full details.

---

## Tracking Mode

Entered via `POST /tracking/start`, exited via `POST /tracking/stop`.

### Start (`POST /tracking/start`)
1. Require mode == Setup; return `409` otherwise
2. Call `stop_active_pipeline` (kills any live WebRTC session defensively)
3. Spawn `rpicam-vid --codec mjpeg` at **1920×1080 @ 30fps**
4. Launch detection loop:
   - Read MJPEG frame → OpenCV decode → store as latest frame
   - Apply green HSV preset (`/hsv/green`) mask
   - `find_contours` → `min_enclosing_circle` → largest blob centroid `(x, y)`
   - Broadcast centroid over internal `broadcast::Sender<(f32, f32)>`
5. Set mode = Tracking; return `200 OK` once pipeline is running

### Stop (`POST /tracking/stop`)
1. Require mode == Tracking; return `409` otherwise
2. Kill MJPEG pipeline; clear `mjpeg_pipeline`
3. Set mode = Setup; return `200 OK`

---

## Tracking WebSocket (`GET /tracking`)

Shared by both World Calibration and Tracking modes.

- Server-push only. Quest connects once, server spams coords.
- Quest **never requests anything** after the initial WS upgrade.
- Message format (JSON per frame):
  ```json
  { "x": 960.5, "y": 540.2 }
  ```
- If no green blob is detected in a frame, **no message is sent** for that frame.
- Connection drops naturally when the MJPEG pipeline is killed.

---

## Unity Game Flow

```
POST /tracking/start
        │
        ▼
Connect to ws://{host}:8080/tracking
        │
        ▼
Show world-space countdown timer
(waiting for first coord — server spinning up MJPEG pipeline)
        │
        ▼ (first coord arrives)
Hide timer → Game begins
        │
        ▼ (game ends, ~1-2 min)
POST /tracking/stop
```

---

## Resolution

- **Default:** 1920×1080 (highest positional accuracy)
- **Fallback:** 1280×720 if Pi 5 CPU can't sustain 30fps at 1080p
- Tune after profiling — blob detection quality degrades minimally at lower res

---

## Pi Thermals

- Detection loop only runs during active gameplay (~1-2 min)
- Idle between rounds: MJPEG pipeline killed, CPU load drops to near zero
- Start/stop triggered by Unity at game begin/end
