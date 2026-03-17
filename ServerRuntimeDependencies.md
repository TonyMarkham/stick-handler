# Server — Runtime Dependencies

Everything needed to run the pre-built `server` binary on a Raspberry Pi 5 running Ubuntu Server/Desktop (arm64). No Rust toolchain or compiler is required on the runtime machine.

---

## System Packages

```bash
sudo apt update
sudo apt install -y \
  libopencv-core-dev \
  libopencv-imgproc-dev \
  libopencv-imgcodecs-dev \
  libopencv-calib3d-dev \
  rpicam-apps
```

> **Alternative (simpler, larger):** `sudo apt install libopencv-dev rpicam-apps`
> installs all OpenCV modules at once. Use the minimal list above for a leaner runtime image.

| Package | Purpose |
|---|---|
| `libopencv-core` | Core Mat types, array ops (`in_range`, `bitwise_and`, etc.) |
| `libopencv-imgproc` | `cvtColor`, `findContours`, `minEnclosingCircle`, drawing functions |
| `libopencv-imgcodecs` | `imdecode` / `imencode` — JPEG decode/encode |
| `libopencv-calib3d` | `findHomography` — perspective transform for world calibration |
| `rpicam-apps` | Provides `rpicam-vid` and `rpicam-still` subprocesses spawned by the server |

---

## Shared Libraries

The binary dynamically links these at runtime. They are provided by the packages above:

```
libopencv_core.so.4xx
libopencv_imgproc.so.4xx
libopencv_imgcodecs.so.4xx
libopencv_calib3d.so.4xx
libstdc++.so.6
libgcc_s.so.1
libc.so.6
```

Verify all shared libraries are resolved before deploying:

```bash
ldd ./server | grep "not found"
# Should print nothing. Any "not found" lines indicate a missing package.
```

---

## Binary

| File | Notes |
|---|---|
| `server` | Compiled with `cargo build --release -p server`. Strip for smaller size: `strip server` |
| `hsv_presets.json` | Optional. Place in the same directory as the binary. If absent, safe defaults are used and the file is created on the first `PUT /hsv/*` call |

---

## Camera Hardware

| Requirement | Details |
|---|---|
| Raspberry Pi 5 | The MJPEG and H.264 pipelines use the Pi 5's hardware ISP and codec via `rpicam-vid` |
| Camera module | Any Pi-compatible camera (Pi Camera Module 3 recommended for quality) |
| Firmware config | `/boot/firmware/config.txt` must include `camera_auto_detect=1` (default on current Pi OS / Ubuntu Pi images) |
| User group | The user running the binary must be in the `video` group: `sudo usermod -aG video <user>` |

Verify the camera is accessible before starting the server:

```bash
rpicam-hello --list-cameras
# Should list at least one camera
```

---

## Network

| Requirement | Details |
|---|---|
| Port 8080 (TCP) | HTTP + WebSocket. Must be reachable from the Quest 3 and any Unity development machine |
| Outbound UDP | Required for WebRTC ICE connectivity to `stun.l.google.com:19302` (port 19302). Used only during WebRTC session setup in Setup mode |
| LAN | Pi and Quest 3 must be on the same WiFi network. No TURN server is needed for LAN-only use |

### WiFi Setup (terminal / headless)

Ubuntu Server uses **NetworkManager** (`nmcli`) to manage network connections.

**1. Find your WiFi interface name:**
```bash
nmcli device status
# Look for a device of type "wifi" — usually "wlan0"
```

**2. Scan for available networks:**
```bash
nmcli device wifi list
```

**3. Connect to a network:**
```bash
sudo nmcli device wifi connect "YourNetworkName" password "YourPassword"
```

**4. Verify the connection:**
```bash
nmcli connection show --active
ip addr show wlan0        # confirm an IP address was assigned
ping -c 3 google.com      # confirm internet/LAN routing
```

**5. Find the Pi's IP address** (so you can point the Quest at it):
```bash
ip addr show wlan0 | grep "inet "
# e.g.  inet 192.168.1.42/24
```

The connection is saved and reconnects automatically on reboot — no further configuration needed.

**Switching networks** (e.g. moving the Pi to a different venue):
```bash
# List saved connections
nmcli connection show

# Delete the old one (optional)
sudo nmcli connection delete "OldNetworkName"

# Connect to the new one
sudo nmcli device wifi connect "NewNetworkName" password "NewPassword"
```

**Troubleshooting:**
```bash
# Check NetworkManager status
sudo systemctl status NetworkManager

# See recent connection events
sudo journalctl -u NetworkManager -n 50

# Force a rescan if networks aren't showing up
nmcli device wifi rescan
nmcli device wifi list
```

---

## Running

```bash
# Foreground (logs to stdout)
RUST_LOG=info,server=debug ./server

# Background with systemd (recommended for production)
sudo systemctl start stick-handler
```

### Systemd Unit (optional)

Create `/etc/systemd/system/stick-handler.service`:

```ini
[Unit]
Description=stick-handler camera server
After=network.target

[Service]
Type=simple
User=<your-user>
WorkingDirectory=/opt/stick-handler
ExecStart=/opt/stick-handler/server
Restart=on-failure
Environment=RUST_LOG=info,server=debug

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now stick-handler
sudo journalctl -u stick-handler -f   # follow logs
```

---

## Environment Variables

| Variable | Default | Purpose |
|---|---|---|
| `RUST_LOG` | `info` | Log verbosity. `info,server=debug` enables per-module debug logs |

---

## Runtime Behaviour Notes

- **No display required.** The server is fully headless. OpenCV is used for compute only — no `imshow` or GUI calls are made.
- **Camera is exclusive.** Only one pipeline runs at a time. `rpicam-vid` and `rpicam-still` hold the camera hardware exclusively while active. Other processes (e.g. `rpicam-hello`) will fail while the server has the camera.
- **`hsv_presets.json`** is written to the working directory on every `PUT /hsv/*` call and read on startup. The working directory should be writable by the server process.
- **No root required.** The server runs as a normal user, provided that user is in the `video` group.
