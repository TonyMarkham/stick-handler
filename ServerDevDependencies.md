# Server — Developer Dependencies

Everything needed to build the server binary from source on a Raspberry Pi 5 running Ubuntu Server/Desktop (arm64).

---

## System Packages

Install all at once:

```bash
sudo apt update
sudo apt install -y \
  build-essential \
  pkg-config \
  cmake \
  libclang-dev \
  clang \
  libopencv-dev \
  rpicam-apps
```

| Package | Version | Purpose |
|---|---|---|
| `build-essential` | system | C/C++ compiler and linker (`gcc`, `g++`, `make`) required by Cargo build scripts and C FFI crates |
| `pkg-config` | system | Lets `opencv-rs`'s build script locate OpenCV headers and libraries |
| `cmake` | ≥ 3.16 | Required by opencv-rs if building OpenCV from source (not needed if using `libopencv-dev`) |
| `libclang-dev` | system | Required by `bindgen` (used internally by `opencv-rs` to generate Rust bindings from C++ headers) |
| `clang` | system | Provides `libclang` runtime alongside `libclang-dev` |
| `libopencv-dev` | ≥ 4.5 | OpenCV headers and static/shared libs. Pulls in modules: `core`, `imgproc`, `imgcodecs`, `calib3d` |
| `rpicam-apps` | system | Provides `rpicam-vid` and `rpicam-still` — needed for integration testing against real camera hardware |

---

## Rust Toolchain

Install via `rustup` (do **not** use the `apt` package — it is outdated):

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source "$HOME/.cargo/env"
```

| Component | Version | Purpose |
|---|---|---|
| `rustc` | edition 2024 (≥ 1.85) | Compiler. Edition 2024 is required by `edition = "2024"` in `Cargo.toml` |
| `cargo` | matches `rustc` | Build tool, dependency resolver, test runner |
| `rust-std` (aarch64) | matches `rustc` | Standard library for the Pi 5's native target (`aarch64-unknown-linux-gnu`) |

Verify:
```bash
rustc --version   # rustc 1.85.0 or later
cargo --version
```

---

## Cargo / Rust Dependencies

All resolved automatically by Cargo from `Cargo.lock`. Listed here for visibility.

### Direct dependencies (`backend/server/Cargo.toml`)

| Crate | Version | Purpose |
|---|---|---|
| `axum` | 0.8.8 | HTTP + WebSocket server framework (`ws` feature enabled) |
| `futures-util` | 0.3.32 | `SinkExt` / `StreamExt` for WebSocket stream handling |
| `bytes` | 1.11.1 | Zero-copy byte buffer (`Bytes`) for frame broadcast |
| `error-location` | 0.1.0 | Captures `file!():line!()` into error values via `#[track_caller]` |
| `opencv` | 0.98.1 | OpenCV Rust bindings. Features: `calib3d`, `imgcodecs`, `imgproc` |
| `serde` | 1.0.228 | Serialization framework (`derive` feature) |
| `serde_json` | 1.0.149 | JSON encode/decode for API request/response bodies |
| `signal` | 0.7.0 | OS signal handling (`SIGINT` / `SIGTERM`) |
| `signal-server` | 0.1.0 | Local crate — WebRTC signaling message types and session store |
| `thiserror` | 2.0.18 | Derive macro for `ServerError` |
| `tokio` | 1.50.0 | Async runtime (`full` feature — includes `sync`, `process`, `net`, etc.) |
| `tokio-util` | 0.7.18 | `SyncIoBridge` — wraps async `ChildStdout` for synchronous `Read` inside `spawn_blocking` |
| `tower-http` | 0.6.8 | HTTP middleware (`tracing` feature) |
| `tracing` | 0.1.44 | Structured logging macros (`info!`, `warn!`, `debug!`, etc.) |
| `tracing-subscriber` | 0.3.23 | `RUST_LOG` env-filter subscriber |
| `uuid` | 1.22.0 | Session ID generation |
| `webrtc` | 0.17.1 | Pure-Rust WebRTC stack (ICE, DTLS, SRTP, RTP) |
| `webrtc-media` | 0.17.1 | `H264Reader` and `IVFReader` for NAL/frame parsing |

---

## Build Commands

```bash
# Type-check only (fastest feedback loop)
cargo check -p server

# Lint
cargo clippy -p server

# Debug build
cargo build -p server

# Release build (~10+ min on Pi 5, run once)
cargo build --release -p server

# Run tests
cargo test -p server
```

### Environment Variables

| Variable | Example | Purpose |
|---|---|---|
| `RUST_LOG` | `info,server=debug` | Log level filter. `server=debug` enables verbose per-module logs |
| `OPENCV_INCLUDE_PATHS` | _(usually auto-detected)_ | Override OpenCV header path if `pkg-config` fails |
| `OPENCV_LINK_PATHS` | _(usually auto-detected)_ | Override OpenCV library path |

---

## Camera Hardware Setup

Required even for a build-only machine if you plan to do end-to-end testing:

```bash
# Enable camera in firmware config (Pi 5)
sudo nano /boot/firmware/config.txt
# Add or confirm: camera_auto_detect=1

# Add your user to the video group
sudo usermod -aG video $USER

# Reboot
sudo reboot

# Verify camera is detected
rpicam-hello --list-cameras
```
