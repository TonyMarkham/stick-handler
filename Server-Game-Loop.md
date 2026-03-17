# Server Game Loop Design

## Modes

The server operates in two mutually exclusive modes:

| Mode | Active When |
|---|---|
| **Setup** | Default. WebRTC stream + calibration endpoints live. |
| **Tracking** | Gameplay. Green blob detection only. Everything else idle. |

---

## Setup Mode (default)

- `rpicam-vid` running, broadcasting H.264 to WebRTC clients via `/ws`
- Calibration endpoints available (`/still/*`, `/hsv/*`)
- No detection loop running

---

## Tracking Mode

Entered via `POST /tracking/start`, exited via `POST /tracking/stop`.

### Start (`POST /tracking/start`)
1. Kill the WebRTC `rpicam-vid` process (H.264/VP8)
2. Spawn a new `rpicam-vid --codec mjpeg` at **1920×1080 @ 30fps**
3. Launch detection loop:
   - Read MJPEG frame → OpenCV decode
   - Apply green HSV preset (`/hsv/green`) mask
   - `find_contours` → `min_enclosing_circle` → largest blob centroid `(x, y)`
   - Broadcast centroid over internal `broadcast::Sender<(f32, f32)>`
4. Returns `200 OK` once pipeline is running

### Stop (`POST /tracking/stop`)
1. Kill detection loop and MJPEG `rpicam-vid` process
2. Returns `200 OK`
3. WebRTC pipeline resumes on next `/ws` client connect

---

## Tracking WebSocket (`GET /tracking`)

- Server-push only. Quest connects once, server spams coords.
- Quest **never requests anything** after the initial WS upgrade.
- Message format (JSON per frame):
  ```json
  { "x": 960.5, "y": 540.2 }
  ```
- If no green blob is detected in a frame, **no message is sent** for that frame.
- Connection drops naturally when `POST /tracking/stop` is called.

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
