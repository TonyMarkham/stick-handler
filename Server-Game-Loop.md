# Server Game Loop Design

## Modes

The server operates in three mutually exclusive modes:

| Mode | Active When |
|---|---|
| **Setup** | Default. WebRTC stream + calibration endpoints live. |
| **World Calibration** | User is placing cylinders on orange stickers. MJPEG + green blob detection running. `POST /calibration/recalc` available. |
| **Tracking** | Gameplay. MJPEG + green blob detection running. Everything else idle. |

Mode transitions:
- Setup → World Calibration: `POST /calibration/start`
- World Calibration → Setup: `POST /calibration/end`
- Setup → Tracking: `POST /tracking/start`
- Tracking → Setup: `POST /tracking/stop`

---

## Setup Mode (default)

- `rpicam-vid` running, broadcasting H.264 to WebRTC clients via `/ws`
- Calibration endpoints available (`/still/*`, `/hsv/*`)
- No detection loop running

---

## World Calibration Mode

Entered via `POST /calibration/start`, exited via `POST /calibration/end`.

Uses the **same MJPEG pipeline and `/tracking` WebSocket** as Tracking Mode — green blob centroid stream is live so the blob indicator cylinder moves in real time while the user positions the orange stickers.

`POST /calibration/recalc` is only valid in this mode. See `World-Calibration.md` for full details.

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
