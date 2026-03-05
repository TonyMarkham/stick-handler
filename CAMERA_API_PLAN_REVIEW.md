# CAMERA_API_PLAN — Gordon Ramsay Code Review (Re-Review)
**Reviewed:** 2026-03-05
**Plan:** Camera Start/Stop API (`/camera/start`, `/camera/stop`, `/camera/status`)
**Previous Score: 91/100**
**New Score: 95/100** — *"You listened. Good. But there are still crumbs on the plate."*

---

## Prior Issues — Verification

All five issues from the first review have been addressed. Let me confirm each one:

| Prior Issue | Fix Applied | Verified |
|---|---|---|
| Silent WS error swallowing | `Err(e)` arm with `tracing::warn!` added to `ws_read_task` | ✅ |
| Poisoned mutex swallows stop() | `poisoned.into_inner()` recovery in `CameraHandle::stop` | ✅ |
| SIGTERM not handled | `tokio::signal::unix` + `tokio::select!` in `shutdown_signal` | ✅ |
| `sender` field `pub` exposure | Changed to `pub(crate)` + `subscribe()` method | ✅ |
| Redundant `use` inside `ws_read_task` | Removed — only module-level `use futures_util::StreamExt` remains | ✅ |

Well done. Every single one. That's how you respond to a review.

---

## What's Now Correct ✅

### `ws_read_task` — Fixed Properly
```rust
while let Some(result) = ws_read.next().await {
    match result {
        Ok(msg) => match msg {
            Message::Text(text) => match serde_json::from_str::<SignalMessage>(&text) {
                Ok(signal) => {
                    if in_tx.send(signal).is_err() {
                        break;
                    }
                }
                Err(e) => tracing::warn!("failed to parse signal message: {e}"),
            },
            Message::Close(_) => break,
            _ => {}
        },
        Err(e) => {
            tracing::warn!("WebSocket read error: {e}");
            break;
        }
    }
}
```
Network errors are now logged. Flaky Quest 3 connections will leave breadcrumbs. **Correct.**

### `CameraHandle::stop` — Fixed Properly
```rust
pub fn stop(self) {
    let mut child = match self.child.lock() {
        Ok(guard) => guard,
        Err(poisoned) => {
            tracing::warn!("Camera mutex was poisoned; attempting kill anyway");
            poisoned.into_inner()
        }
    };
    if let Err(e) = child.start_kill() {
        tracing::warn!("Failed to send SIGKILL to camera process: {e}");
    }
}
```
Mutex poison no longer silently abandons the subprocess. **Correct.**

### `shutdown_signal` — Fixed Properly
```rust
async fn shutdown_signal() {
    use tokio::signal::unix::{SignalKind, signal};

    let mut sigterm = signal(SignalKind::terminate()).expect("failed to install SIGTERM handler");

    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            tracing::info!("Received SIGINT, shutting down");
        }
        _ = sigterm.recv() => {
            tracing::info!("Received SIGTERM, shutting down");
        }
    }
}
```
`systemctl stop` will now trigger graceful shutdown. No more orphaned `rpicam-vid`. **Correct.**

### `CameraHandle` Visibility — Fixed
```rust
pub struct CameraHandle {
    pub(crate) sender: broadcast::Sender<Bytes>,
    pub(crate) child: Arc<StdMutex<tokio::process::Child>>,
}

impl CameraHandle {
    pub(crate) fn subscribe(&self) -> broadcast::Receiver<Bytes> {
        self.sender.subscribe()
    }
```
Both fields are now `pub(crate)`. `subscribe()` is the clean accessor. **Correct.**

---

## Remaining Issues — The Crumbs 🟡

---

### 🟡 ISSUE #1 — Variable Named `sender` Is Actually a Receiver (`main.rs` line 347)

```rust
async fn handle_socket(mut socket: WebSocket, state: Arc<AppState>) {
    let sender = {                          // ← Named "sender"
        let guard = state.camera.lock().await;
        match guard.as_ref() {
            Some(handle) => handle.subscribe(),  // ← Returns broadcast::Receiver<Bytes>
            ...
        }
    };
    // ...
    webrtc_handler::handle_peer_session(in_rx, out_tx, sender, state.stun_urls.clone()).await
```

`handle.subscribe()` returns a `broadcast::Receiver<Bytes>`. The variable is named `sender`. **This is backwards.** A `Receiver` is not a `Sender`. Anyone reading this code cold will do a double-take — "wait, we're passing a *sender* into the peer session handler... but that function takes a *receiver*...".

The prior version named it `nal_rx`. That was better. This rename went in the wrong direction.

**The Fix:**
```rust
let nal_rx = {
    let guard = state.camera.lock().await;
    match guard.as_ref() {
        Some(handle) => handle.subscribe(),
        ...
    }
};
// ...
webrtc_handler::handle_peer_session(in_rx, out_tx, nal_rx, state.stun_urls.clone()).await
```

**Severity: -3 (Style — misleading name, future confusion guaranteed)**

---

### 🔴 ISSUE #2 — Verification Script Is Wrong (Camera Starts Idle)

The plan explicitly states at line 311:
```rust
// Camera starts idle — use POST /camera/start to begin streaming.
```

But the Verification section shows:
```bash
curl -s http://pi:8080/camera/status          # {"running":true}    ← WRONG
curl -s -X POST http://pi:8080/camera/stop    # 200 Camera stopped  ← WRONG
curl -s -X POST http://pi:8080/camera/stop    # 409 Camera not running
curl -s http://pi:8080/camera/status          # {"running":false}
curl -s -X POST http://pi:8080/camera/start   # 200 Camera started
curl -s -X POST http://pi:8080/camera/start   # 409 Camera already running
```

This is a **carryover from an older version** where the camera started automatically. With the camera starting idle:

- First `curl /camera/status` → `{"running":false}` not `{"running":true}`
- First `POST /camera/stop` → `409 Camera not running` not `200 Camera stopped`

The developer running this script on the Pi will get immediate failures on the FIRST TWO COMMANDS. They'll doubt the implementation, start debugging working code, and waste time chasing a ghost.

**The Fix:**
```bash
cargo check
cargo clippy -- -D warnings

# On Pi:
RUST_LOG=info,server=debug cargo run -p server

# Camera starts idle
curl -s http://pi:8080/camera/status           # {"running":false}
curl -s -X POST http://pi:8080/camera/start    # 200 Camera started
curl -s http://pi:8080/camera/status           # {"running":true}
curl -s -X POST http://pi:8080/camera/start    # 409 Camera already running
curl -s -X POST http://pi:8080/camera/stop     # 200 Camera stopped
curl -s -X POST http://pi:8080/camera/stop     # 409 Camera not running
curl -s http://pi:8080/camera/status           # {"running":false}

# Quest 3 browser → streams normally after /camera/start
# Ctrl+C or SIGTERM → rpicam-vid killed cleanly, no orphan
```

**Severity: -4 (Documentation bug — wrong expected outputs will cause real confusion during verification)**

---

### 🟡 ISSUE #3 — `ws_write_task` Swallows Write Errors Without Logging

```rust
async fn ws_write_task(...) {
    use futures_util::SinkExt;
    while let Some(msg) = out_rx.recv().await {
        match serde_json::to_string(&msg) {
            Ok(text) => {
                if ws_write.send(Message::Text(text.into())).await.is_err() {
                    break;  // ← Error discarded silently
                }
            }
            Err(e) => tracing::warn!("failed to serialize signal message: {e}"),
        }
    }
    let _ = ws_write.close().await;
}
```

`ws_read_task` now correctly logs WebSocket errors. `ws_write_task` still silently discards them with `is_err()`. These are different tasks — a write failure tells you the connection dropped mid-session, which is useful for distinguishing "client disconnected cleanly" from "network cut". Minor, but the fix is one line.

**The Fix:**
```rust
if let Err(e) = ws_write.send(Message::Text(text.into())).await {
    tracing::debug!("WebSocket write error: {e}");
    break;
}
```

**Severity: -2 (Minor — the `ws_read_task` fix already logs most disconnects; write errors are secondary)**

---

### 🟡 ISSUE #4 — SIGTERM Missing from Lifecycle Correctness Table

The lifecycle table correctly documents the before/after for four scenarios. But SIGTERM is now handled — it's one of the key additions in this revision — and it's not in the table at all.

```markdown
| Server Ctrl+C | `rpicam-vid` becomes orphan | `handle.stop()` called after serve exits |
```

There's no row for:
```markdown
| Server SIGTERM (systemd) | `rpicam-vid` becomes orphan | `shutdown_signal` catches SIGTERM, same cleanup path |
```

This matters because the *whole reason* SIGTERM was added was for systemd. Leaving it out of the lifecycle table means the plan is missing its own justification.

**Severity: -1 (Documentation completeness)**

---

## Architecture Verification: Still Sound ✅

No regressions introduced. The changes are purely additions/fixes:
- `subscribe()` method cleanly encapsulates `sender.subscribe()`
- Poison mutex recovery is defensive-but-correct
- SIGTERM handling slots into `axum::serve`'s graceful shutdown naturally — the cleanup path below `serve().await` runs identically for both signals

The `spawn_camera_monitor` logic, the TOCTOU prevention, the `StdMutex` vs `TokioMutex` design, the `_child_guard` keepalive pattern — all correct, all untouched. Good.

---

## Lifecycle Correctness: Re-Verified ✅

| Scenario | Status |
|---|---|
| `rpicam-vid` crashes unexpectedly | ✅ Monitor clears state |
| Explicit `/camera/stop` | ✅ `.take()` before `stop()`, monitor sees `None` |
| Server Ctrl+C (SIGINT) | ✅ Graceful shutdown, camera killed cleanly |
| Server SIGTERM (systemd) | ✅ Now handled — same cleanup path |
| Two concurrent `/camera/start` | ✅ Lock held across check+set, second gets 409 |
| WS connect while camera off | ✅ `SignalMessage::Error` sent before close |
| WS read error (network drop) | ✅ Logged with `tracing::warn!` |
| WS write error | ⚠️ Silently discarded (Issue #3) |
| Mutex poisoned on `stop()` | ✅ Recovered via `into_inner()` |

---

## Score Breakdown

| Category | Points |
|---|---|
| Base | 100 |
| Good hierarchical structure | +5 |
| Clear dependencies documented | +5 |
| Full production code in every task | +10 |
| **Variable named `sender` is a Receiver** | -3 |
| **Verification script wrong (camera starts idle)** | -4 |
| `ws_write_task` silent write errors | -2 |
| SIGTERM missing from lifecycle table | -1 |

**FINAL SCORE: 95 / 100**

---

## What Must Be Fixed

1. **Rename `sender` → `nal_rx`** in `handle_socket` — one word change, zero ambiguity
2. **Fix the Verification curl script** — reorder to match "camera starts idle" design

Issues #3 and #4 are housekeeping — fix them before implementation, not after.

---

## Verdict

You took the feedback seriously and fixed every bug cleanly. No half-measures, no workarounds — proper fixes. The poisoned mutex recovery, the SIGTERM handling, the WebSocket error logging — all done correctly.

The remaining issues are crumbs, not catastrophes. Fix the verification script before you hand this to an implementer, rename that variable before someone reads it upside-down, and this plan is **ready to execute**.

**Now get in the kitchen and start cooking.**

---

*— Gordon Ramsay, PM Review Division*
*(Re-Review — All Prior Bugs Addressed)*
