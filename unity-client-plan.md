# Plan: Unity WebRTC Video Receiver Prototype

## Context

The Pi server streams H.264 video via WebRTC (signaling over WebSocket at `ws://PI_IP:8080/ws`).
We want a Unity prototype on Windows to receive and display that stream — using the same packages
and code structure that will later support a Meta Quest 3 XR build.

## Unity Version & Packages

- **Unity 2022 LTS** (2022.3.x) — stable, good WebRTC support, Quest 3 compatible when needed
- `com.unity.webrtc` — Unity's official WebRTC package (via Package Manager > Unity Registry)
- `com.unity.nuget.newtonsoft-json` — JSON with snake_case field control (via Package Manager)
- `NativeWebSocket` — lightweight WebSocket for Unity (add via git URL in Package Manager):
  `https://github.com/endel/NativeWebSocket.git#upm`

## Project Location

`client/StickHandlerUnity/` inside this repo (alongside the Rust workspace).

## Scene Layout

```
Main.unity
└── Canvas (Screen Space - Overlay)
    ├── InputField  "IP Address"  (default: raspberrypi.local)
    ├── Button      "Connect / Disconnect"
    ├── Text        "Status"
    └── RawImage    (fills remaining screen — video renders here)
```

No 3D objects needed for the prototype. A `RawImage` on a UI Canvas is the simplest
display; swapping to a `MeshRenderer` on a quad (for XR) later is a one-line change.

## Scripts (all in Assets/Scripts/)

### `SignalingClient.cs`
Plain C# class (not MonoBehaviour). Wraps NativeWebSocket:
- `ConnectAsync(string host)` — opens `ws://host:8080/ws`
- `SendOffer(string sdp)` / `SendIceCandidate(...)` — serialize with Newtonsoft snake_case
- `OnAnswer`, `OnIceCandidate`, `OnError` — C# events fired from `OnMessage`
- `DispatchMessages()` — must be called from `MonoBehaviour.Update()` (NativeWebSocket requirement)

### `WebRtcReceiver.cs`
Plain C# class. Owns the `RTCPeerConnection`:
- Constructor sets up STUN (`stun:stun.l.google.com:19302`) and wires callbacks
- `StartNegotiation()` coroutine: adds RecvOnly video transceiver, creates offer, sets local desc,
  sends offer via `SignalingClient`
- `HandleAnswer(string sdp)` coroutine: sets remote description
- `HandleIceCandidate(...)` — calls `pc.AddIceCandidate()`
- `pc.OnIceCandidate` → sends candidate via `SignalingClient`
- `pc.OnTrack` → extracts `VideoStreamTrack`, subscribes to `OnVideoReceived`
- `OnVideoReceived` event (passes `Texture`) — `VideoStreamController` assigns to `RawImage`

### `VideoStreamController.cs`
MonoBehaviour on a GameObject in the scene. Owns the UI references and drives the other two:

```csharp
void Update() {
    _signaling?.DispatchMessages();  // pump NativeWebSocket message queue
}
```

- Connect button click → creates `SignalingClient` + `WebRtcReceiver`, calls `StartCoroutine(receiver.StartNegotiation())`
- Wires signaling events to receiver handler coroutines
- Sets `rawImage.texture` when `WebRtcReceiver.OnVideoReceived` fires
- Shows connection state in the status text

## Key com.unity.webrtc API Calls

```csharp
// One-time init (in Awake)
WebRTC.Initialize();

// Create peer connection
var config = new RTCConfiguration {
    iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
};
var pc = new RTCPeerConnection(ref config);

// Add recvonly video transceiver (no local track needed)
var init = new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly };
pc.AddTransceiver(TrackKind.Video, init);

// Incoming track → VideoStreamTrack → Texture
pc.OnTrack = e => {
    if (e.Track is VideoStreamTrack vt)
        vt.OnVideoReceived += tex => rawImage.texture = tex;
};

// Create offer (coroutine)
var op = pc.CreateOffer();
yield return op;
var desc = op.Desc;                         // RTCSessionDescription
yield return pc.SetLocalDescription(ref desc);

// Set remote answer (coroutine)
var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answerSdp };
yield return pc.SetRemoteDescription(ref answer);

// ICE
pc.OnIceCandidate = c => signaling.SendIceCandidate(c.Candidate, c.SdpMid, c.SdpMLineIndex);
pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit {
    candidate = candidate, sdpMid = sdpMid, sdpMLineIndex = sdpMLineIndex
}));
```

## JSON Signaling — snake_case Mapping

The server uses `sdp_mid` and `sdp_mline_index` (Rust snake_case). Newtonsoft.Json handles this:

```csharp
// Outgoing ICE candidate
var msg = new {
    type = "ice-candidate",
    candidate = candidate,
    sdp_mid = sdpMid,
    sdp_mline_index = (int?)sdpMLineIndex
};
ws.SendText(JsonConvert.SerializeObject(msg));

// Incoming — parse type field, then extract fields by name
var root = JObject.Parse(text);
var type = root["type"]?.ToString();
switch (type) {
    case "answer": HandleAnswer(root["sdp"]!.ToString()); break;
    case "ice-candidate":
        HandleIceCandidate(
            root["candidate"]!.ToString(),
            root["sdp_mid"]?.ToString(),
            (ushort?)root["sdp_mline_index"]?.Value<int>());
        break;
}
```

## Important Gotchas

1. **`com.unity.webrtc` coroutines** — `CreateOffer`, `SetLocalDescription`, `SetRemoteDescription`
   are all `RTCSessionDescriptionAsyncOperation` — must be `yield return`-ed inside a coroutine.
   Don't `await` them; Unity WebRTC doesn't use `async/await`.

2. **`VideoStreamTrack.OnVideoReceived` thread** — fires on the render thread. Only assign
   `rawImage.texture` from the main thread; use a flag + assign in `Update()` if needed.

3. **`pc.AddTransceiver` before `CreateOffer`** — the transceiver direction must be set before
   creating the offer, otherwise the SDP won't include a video m-line and the server sends no video.

4. **H.264 codec selection** — `com.unity.webrtc` on Windows defaults to H.264 hardware encode
   for send, but for *receive* it negotiates based on the remote offer/answer. The server's SDP
   offers `profile-level-id=42e01f` (Baseline 3.1). Unity WebRTC's receiver accepts this.
   No explicit codec filtering is needed on the Unity side for receive-only.

5. **`WebRTC.Initialize()` must be called before any `RTCPeerConnection`** — call it in `Awake`.
   Call `WebRTC.Dispose()` in `OnDestroy` or `OnApplicationQuit`.

6. **NativeWebSocket `DispatchMessages()`** — must be called every frame in `Update()`.
   Without it, `OnMessage` callbacks never fire on WebGL; on Standalone it matters too for
   thread-safe delivery.

## Files to Create

| File | Purpose |
|---|---|
| `client/StickHandlerUnity/Assets/Scripts/SignalingClient.cs` | WebSocket + JSON signaling |
| `client/StickHandlerUnity/Assets/Scripts/WebRtcReceiver.cs` | RTCPeerConnection lifecycle |
| `client/StickHandlerUnity/Assets/Scripts/VideoStreamController.cs` | MonoBehaviour glue + UI |
| `client/StickHandlerUnity/Packages/manifest.json` | Package dependencies |

The Unity project itself (`.meta` files, scene, etc.) is created by the Unity Editor — only the
scripts and manifest are written here.

## `manifest.json` Key Dependencies

```json
{
  "dependencies": {
    "com.unity.webrtc": "3.0.0-pre.8",
    "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.github.endel.nativewebsocket": "https://github.com/endel/NativeWebSocket.git#upm"
  }
}
```

> Note: `com.unity.webrtc` 3.0.x is the current stable release for Unity 2022 LTS.

## Verification Steps

1. Open Unity Hub → create new project from `client/StickHandlerUnity/` with Unity 2022 LTS
2. Unity Package Manager auto-installs packages from `manifest.json`
3. Create `Main` scene, add Canvas + UI elements, attach `VideoStreamController`
4. Start the Pi server: `RUST_LOG=info cargo run -p server`
5. Press Play in Editor → enter Pi IP → click Connect
6. Check Console: should see `[WebRTC] Connection state: Connected`
7. Video frame should appear in the `RawImage`

## Path to Quest 3

When ready to move to Quest 3:
1. Install Android Build Support + Oculus XR Plugin in Unity Hub
2. Switch platform to Android, set minimum API to Android 10 (Quest requirement)
3. Add `com.unity.xr.oculus` package
4. Replace Canvas `RawImage` with a world-space quad (`MeshRenderer` + `RenderTexture`)
5. Add `XROrigin` to the scene
6. Build & deploy — the `SignalingClient` and `WebRtcReceiver` scripts are unchanged
