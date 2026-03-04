#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.WebRTC;

[RequireComponent(typeof(UIDocument))]
public class VideoStreamController : MonoBehaviour
{
    [Header("Video")] [SerializeField] private RenderTexture _videoFeedRT;

    // UI Toolkit element references (queried in OnEnable)
    private TextField _ipField;
    private Button _connectBtn;
    private Label _statusLabel;

    // BUG3 (R3): _signaling is read from Unity.WebRTC's ICE-gathering worker thread
    // (in OnIceCandidateReady). On ARM (Quest 3 / Snapdragon XR2), a plain field write
    // on the main thread is not guaranteed visible to the background thread without a
    // memory barrier. volatile ensures visibility of the null/non-null reference value.
    private volatile SignalingClient? _signaling;
    private WebRtcReceiver _receiver;
    private bool _connected;

    private bool _connecting; // BUG4: guard against double-connect race

    // BUG1 (R7): Store the Connect() coroutine handle so Disconnect() can stop it.
    // Without StopCoroutine, tapping Connect → Disconnect → Connect leaves the first
    // coroutine alive in WaitUntil. When the second session opens _socketOpen, both
    // coroutines resume in the same frame, both pass the R5 null-guard (fields point
    // at the second session's live objects), and both call StartNegotiation on the same
    // RTCPeerConnection — adding two transceivers and sending two offers → Rust server
    // receives unexpected renegotiation it was never designed to handle → black quad.
    private Coroutine _connectCoroutine;

    // R10 BUG: WebRTC.Update() is the required public coroutine that ticks
    // VideoStreamTrack.UpdateTexture() each frame, submitting GPU decode commands to
    // the native plugin and firing OnVideoFrameResize → OnVideoReceived. Starting it
    // in Awake() means it is never restarted if the GameObject is disabled and
    // re-enabled: Unity stops all coroutines on disable and calls only OnEnable() on
    // re-enable, not Awake(). Moving the coroutine to OnEnable/OnDisable ensures the
    // decoder tick is always running while the component is active.
    // Note: WebRTC.Initialize/Dispose are internal; ContextManager calls them
    // automatically via [RuntimeInitializeOnLoadMethod] / Application.quitting.
    private Coroutine _webRtcUpdateCoroutine;

    // Render-thread → main-thread texture handoff
    private volatile Texture _pendingTexture;

    // BUG5 (R6): Prior comment was factually incorrect. SignalingClient marshals
    // OnConnected/OnError/OnDisconnected through _mainThreadQueue and dispatches
    // them via DispatchMessages() on the Unity main thread. These fields are written
    // and read exclusively on the main thread. volatile is retained from a prior
    // incorrect cross-thread analysis; it is harmless but not required here.
    private volatile bool _socketOpen;
    private volatile bool _socketError;

    // ICE candidates fire on the WebRTC worker thread.
    // Enqueue there; dequeue and send on the main thread in Update().
    private readonly ConcurrentQueue<(string candidate, string? sdpMid, ushort? sdpMLineIndex)>
        _pendingIceCandidates = new();

    // ── Unity Lifecycle ──────────────────────────────────────────────────
    private void OnEnable()
    {
        // R10 BUG: Start WebRTC.Update() here (not in Awake) so it restarts every
        // time the component is re-enabled. See _webRtcUpdateCoroutine field comment
        // for full rationale. BUG1+BUG2+BUG3 (R6): WebRTC.Initialize/Dispose are
        // internal APIs called automatically by ContextManager — do not call manually.
        _webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());

        // BUG1 (R11): UIDocument.rootVisualElement returns null when Panel Settings is
        // not assigned in the Inspector — a common first-setup error per PONE-17.
        // When root is null, root.Q<TextField>() throws NRE before the BUG7 null guards
        // (which check Q<>'s return value) can execute. Both failure modes must produce
        // a controlled, logged early-return rather than an opaque NullReferenceException:
        //   Case 1 — element name mismatch → Q<>() returns null → caught by BUG7 guards.
        //   Case 2 — Panel Settings missing → rootVisualElement is null → caught here.
        var doc = GetComponent<UIDocument>();
        var root = doc.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[Controller] UIDocument.rootVisualElement is null — assign Panel Settings in Inspector");
            return;
        }

        _ipField = root.Q<TextField>("ip-field");
        _connectBtn = root.Q<Button>("connect-btn");
        _statusLabel = root.Q<Label>("status-label");

        // BUG7: Q<T>() returns null on name mismatch — guard before use.
        if (_ipField == null)
        {
            Debug.LogError("[Controller] 'ip-field' not found in UXML");
            return;
        }

        if (_connectBtn == null)
        {
            Debug.LogError("[Controller] 'connect-btn' not found in UXML");
            return;
        }

        if (_statusLabel == null)
        {
            Debug.LogError("[Controller] 'status-label' not found in UXML");
            return;
        }

        _connectBtn.clicked += OnConnectClicked;
    }

    private void OnDisable()
    {
        // R10 BUG: Stop WebRTC.Update() symmetrically with OnEnable. Unity already
        // stops all coroutines on disable, but explicit cleanup clears the handle so
        // the next OnEnable unconditionally starts a fresh coroutine with no stale ref.
        if (_webRtcUpdateCoroutine != null)
        {
            StopCoroutine(_webRtcUpdateCoroutine);
            _webRtcUpdateCoroutine = null;
        }

        // BUG7: null-guard for symmetry with null-checked OnEnable
        if (_connectBtn != null)
            _connectBtn.clicked -= OnConnectClicked;
    }

    private void Update()
    {
        // Pump NativeWebSocket message queue every frame (required)
        _signaling?.DispatchMessages();

        // Blit incoming video texture into RenderTexture on the main thread.
        // BUG4 (R6): Guard _videoFeedRT — if the Inspector field is left unassigned,
        // Graphics.Blit(tex, null) silently blits to the screen backbuffer instead of
        // the VideoQuad material. Video appears on the camera rather than the quad,
        // with no exception and no log entry, making the bug non-obvious to diagnose.
        if (_pendingTexture != null && _videoFeedRT != null)
        {
            Graphics.Blit(_pendingTexture, _videoFeedRT);
            _pendingTexture = null;
        }

        // Drain ICE candidates that arrived on the WebRTC worker thread.
        // SendIceCandidateAsync must be called from the main thread (see
        // SignalingClient XML docs); this loop runs only on the main thread.
        // BUG4 (R2): Use continue (not break) so stale candidates from a dead
        // session are discarded when _signaling is null, rather than left in
        // the queue to corrupt the next session's negotiation.
        while (_pendingIceCandidates.TryDequeue(out var ice))
        {
            var sig = _signaling; // local capture before any thread switch
            if (sig == null) continue; // discard if disconnected
            _ = sig.SendIceCandidateAsync(ice.candidate, ice.sdpMid, ice.sdpMLineIndex)
                .ContinueWith(
                    t => Debug.LogError($"[Controller] ICE send failed: {t.Exception}"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
        }
    }

    private void OnDestroy()
    {
        // BUG2 (R6): WebRTC.Dispose() does not exist as a public API in this package
        // version; DisposeInternal() is called automatically on Application.quitting
        // via ContextManager.Quit(). Only Disconnect() is needed here.
        Disconnect();
    }

    // ── Connect / Disconnect ─────────────────────────────────────────────
    private void OnConnectClicked()
    {
        Debug.Log("[Controller] Button Clicked");
        
        // BUG4: treat _connecting like _connected to block a second coroutine
        // from starting during the async handshake window.
        if (_connected || _connecting) Disconnect();
        else _connectCoroutine = StartCoroutine(Connect());
    }

    private IEnumerator Connect()
    {
        _connecting = true;

        // BUG (R9): _ipField null guard — mirrors the _connectBtn armour added in R7/R8.
        // If any future caller invokes Connect() directly (auto-reconnect, deep-link) and
        // OnEnable exited early leaving _ipField null, _ipField.value throws NRE inside the
        // coroutine before _connecting = false is reached, silently locking the UI:
        // the button shows "Disconnect" but clicks route to Disconnect() (a silent no-op)
        // with no log entry to diagnose. Explicit early-exit matches the R7/R8 pattern and
        // also resets _connecting to prevent the stuck-button state.
        if (_ipField == null)
        {
            Debug.LogError("[Controller] Connect() called but 'ip-field' is null — check UXML");
            _connecting = false;
            yield break;
        }

        string host = string.IsNullOrWhiteSpace(_ipField.value)
            ? "raspberrypi.local"
            : _ipField.value.Trim();

        SetStatus($"Connecting to {host}...");
        // BUG8 (R8): Null-guard for consistency — every other .text assignment on
        // _connectBtn in this file is guarded with `if (_connectBtn != null)`.
        // Connect() is today only reachable via OnConnectClicked (after _connectBtn
        // is confirmed non-null by OnEnable), but a future auto-reconnect path calling
        // Connect() directly, or a scene-unload during a prior WaitUntil suspension,
        // could present a destroyed Button here → MissingReferenceException.
        if (_connectBtn != null) _connectBtn.text = "Disconnect";

        _signaling = new SignalingClient();
        _receiver = new WebRtcReceiver();

        // ConnectAsync fire-and-forgets the TCP handshake internally.
        // connectTask.IsCompleted becomes true almost immediately, long before
        // the socket reaches WebSocketState.Open. SendOfferAsync guards on
        // Open state and drops the SDP silently if called too early.
        // Wait for OnConnected (fired when the socket IS open) instead.
        // BUG3 (R2): Reset volatile flags — a prior session may have left them true.
        _socketOpen = false;
        _socketError = false;

        _signaling.OnConnected += () => _socketOpen = true;
        _signaling.OnError += err =>
        {
            _socketError = true;
            SetStatus($"Error: {err}");
        };

        // BUG5: Subscribe to OnDisconnected so the controller reacts when
        // the Pi reboots or the network drops mid-stream.
        _signaling.OnDisconnected += code =>
        {
            // BUG2 (R11): Mirror Disconnect() — stop any in-progress Connect() coroutine.
            // OnDisconnected is the server-initiated analogue of user-triggered Disconnect().
            // The R7 invariant requires _connectCoroutine to accurately reflect the Connect()
            // lifecycle. The coroutine self-terminates today via the R5/R7 guards, but leaving
            // _connectCoroutine non-null after server disconnect violates the invariant and
            // creates a latent bug for any future coroutine-lifecycle logic.
            if (_connectCoroutine != null)
            {
                StopCoroutine(_connectCoroutine);
                _connectCoroutine = null;
            }

            SetStatus($"Disconnected ({code}) — tap Connect to retry");
            _connected = false;
            _connecting = false;
            if (_connectBtn != null) _connectBtn.text = "Connect";
            _receiver?.Dispose();
            _receiver = null;
            // BUG3 (R5): Mirror Disconnect() — clear stale texture so Update() does not
            // blit the last video frame into the RT after the socket closes.
            _pendingTexture = null;
            // BUG4 (R5): Mirror Disconnect() — drain ICE queue and null _signaling.
            // Without this, Update() dequeues stale candidates and calls
            // SendIceCandidateAsync on a closed socket → OnError fires → "Error:
            // SendIceCandidateAsync failed: socket not open" overwrites "Disconnected".
            // Nulling _signaling also stops DispatchMessages() pumping the dead socket.
            while (_pendingIceCandidates.TryDequeue(out _))
            {
            }

            _signaling = null;
        };

        // Wire signaling → receiver.
        // BUG6 (R2): _receiver may be null if Disconnect() is called while an
        // OnAnswer message is already buffered in DispatchMessages(). The next
        // DispatchMessages() call runs the lambda after _receiver is nulled —
        // guard before dereferencing to avoid NullReferenceException.
        _signaling.OnAnswer += sdp =>
        {
            if (_receiver != null) StartCoroutine(_receiver.HandleAnswer(sdp));
        };
        // BUG1 (R3): Same race as OnAnswer (R2 BUG6) — _receiver may be null when this
        // lambda runs if Disconnect() fires while an ICE candidate is buffered in
        // DispatchMessages(). Guard before dereferencing (identical pattern to OnAnswer).
        _signaling.OnIceCandidate += (c, mid, idx) =>
        {
            if (_receiver != null) _receiver.HandleIceCandidate(c, mid, idx);
        };

        // BUG1+BUG2: OnIceCandidateReady fires on the WebRTC worker thread.
        // The event signature uses int? but SendIceCandidateAsync takes ushort?
        // (CS1503 compile error without an explicit cast). Do NOT call
        // SendIceCandidateAsync here — enqueue and send from Update() instead.
        // BUG3 (R11): The bare (ushort?) cast is unchecked in C#: values > 65535
        // truncate silently (65536 → 0) and negatives wrap (−1 → 65535), routing
        // ICE candidates to the wrong media line with no exception and no log.
        // The RFC 8839 §5.1 "max index is small" argument is an unverifiable runtime
        // claim; a bounds-check drop with a warning is safer than silent data corruption.
        // (uint) reinterpretation handles both too-large and negative int values in
        // one branch: a negative int reinterpreted as uint exceeds ushort.MaxValue.
        _receiver.OnIceCandidateReady += (c, mid, idx) =>
        {
            var sig = _signaling; // capture before any thread switch
            if (sig == null) return;
            ushort? mlineIdx = null;
            if (idx.HasValue)
            {
                if ((uint)idx.Value > ushort.MaxValue)
                {
                    Debug.LogWarning(
                        $"[Controller] SdpMLineIndex {idx.Value} out of ushort range — ICE candidate dropped");
                    return;
                }

                mlineIdx = (ushort)idx.Value;
            }

            _pendingIceCandidates.Enqueue((c, mid, mlineIdx));
        };

        // Render thread → volatile field, consumed in Update()
        _receiver.OnVideoReceived += tex => _pendingTexture = tex;

        // Begin the connection; do NOT await — wait for OnConnected.
        _ = _signaling.ConnectAsync(host);

        // BUG5 (R2): Without a deadline, a silent SYN-drop (firewall, mDNS
        // blocked on enterprise/hotel WiFi) means OnError never fires and
        // WaitUntil spins forever. 10 s covers a slow LAN DNS lookup.
        float deadline = Time.time + 10f;
        yield return new WaitUntil(() => _socketOpen || _socketError || Time.time > deadline);

        // BUG1 (R5): Guard against _receiver/_signaling being nulled while the coroutine
        // is suspended in WaitUntil. With the R7 StopCoroutine fix, Disconnect() now kills
        // this coroutine before it can resume — so this branch is only reachable via the
        // OnDisconnected lambda (e.g. the Pi-side closed the socket immediately after the
        // TCP handshake completed). OnDisconnected nulls both fields and already resets
        // _connecting = false before this coroutine resumes.
        // BUG9 (R8): Do NOT assign _connecting = false here. OnDisconnected already reset
        // it. In the race where socket-open and socket-close drain in the same
        // DispatchMessages() call, a new Connect() coroutine may have started and set
        // _connecting = true before this coroutine resumes from WaitUntil. Overwriting
        // with false here clobbers that new session's double-connect guard, allowing a
        // third button click to spawn yet another coroutine and defeating the protection.
        if (_receiver == null || _signaling == null)
        {
            yield break;
        }

        if (!_socketOpen)
        {
            // Treat timeout the same as an explicit error.
            if (!_socketError) SetStatus("Connection timed out — check IP and WiFi");

            // BUG2 (R2): Dispose resources so the next connect attempt does
            // not overwrite live objects without closing them, leaking a
            // WebSocket and a native RTCPeerConnection each failure cycle.
            // BUG2 (R3): Use ?. — OnDisconnected may fire in the same DispatchMessages()
            // call as OnError, nulling _receiver before this coroutine resumes next frame.
            _receiver?.Dispose();
            // BUG1 (R4): Use ?. — Disconnect() can null _signaling during the WaitUntil
            // window (user clicks Disconnect before the 10-second deadline). Coroutine
            // resumes at deadline with _signaling == null → NRE aborts cleanup block and
            // fires misleading "Connection timed out" status after user already saw
            // "Disconnected". Same adjacent-fix pattern as BUG2 R3 on the line above.
            _ = _signaling?.CloseAsync();
            _receiver = null;
            _signaling = null;
            _connecting = false;
            // BUG2 (R7): Null-guard for consistency with Disconnect() and OnDisconnected.
            // Connect() is today only reachable via OnConnectClicked (after _connectBtn is
            // confirmed non-null), but a future auto-reconnect path calling Connect() directly
            // would hit a silent NRE here with no test failure to catch it.
            if (_connectBtn != null) _connectBtn.text = "Connect";
            yield break;
        }

        _connected = true;
        _connecting = false;
        SetStatus("Connected — negotiating...");

        // BUG6 (R7): StartNegotiation takes Action<string>, so the async lambda is
        // async void (no return type to await). Safety guarantee: StartNegotiation exits
        // via `if (_pc == null) yield break` before calling onOfferCreated if Disconnect()
        // nulls _pc during either yield — the lambda is therefore never invoked with a null
        // _signaling. The guarantee is coroutine determinism + the _pc == null guards in
        // WebRtcReceiver, NOT SendOfferAsync's internal catch block.
        // If StartNegotiation is refactored to remove those _pc guards, add:
        //   if (_signaling == null) return;
        yield return StartCoroutine(_receiver.StartNegotiation(async offerSdp =>
            await _signaling.SendOfferAsync(offerSdp)));

        // BUG2 (R5): StartNegotiation yields twice (CreateOffer, SetLocalDescription).
        // A Disconnect() call during either yield clears _connected and nulls _pc inside
        // WebRtcReceiver, causing StartNegotiation to exit via its _pc == null guard
        // without ever calling onOfferCreated. Connect() resumes unconditionally and
        // emits a false "Offer sent" status one frame after the user saw "Disconnected".
        if (!_connected) yield break;

        SetStatus("Offer sent — waiting for answer");
    }

    private void Disconnect()
    {
        // BUG1 (R7): Stop the Connect() coroutine before nulling fields.
        // Without this, Connect → Disconnect → Connect leaves the first coroutine alive
        // in WaitUntil. Both coroutines resume when _socketOpen flips true, both pass
        // the R5 null-guard (they see the second session's non-null objects), and both
        // call StartNegotiation on the same RTCPeerConnection.
        if (_connectCoroutine != null)
        {
            StopCoroutine(_connectCoroutine);
            _connectCoroutine = null;
        }

        _receiver?.Dispose();
        _ = _signaling?.CloseAsync();
        _receiver = null;
        _signaling = null;
        _connected = false;
        _connecting = false;
        _pendingTexture = null;
        // BUG4 (R2): Drain the ICE queue so stale candidates from the dead
        // session do not survive into the next Connect() call. Without this,
        // Update() sends previous-session ICE to the new server session the
        // moment _signaling is reassigned, corrupting WebRTC negotiation.
        while (_pendingIceCandidates.TryDequeue(out _))
        {
        }

        if (_videoFeedRT != null)
            RenderTexture.active = null;
        // _connectBtn may be null if OnEnable found a missing element
        if (_connectBtn != null)
            _connectBtn.text = "Connect";
        SetStatus("Disconnected");
    }

    private void SetStatus(string msg)
    {
        Debug.Log($"[Controller] {msg}");
        // BUG7: guard in case OnEnable bailed early on a missing element
        if (_statusLabel != null)
            _statusLabel.text = msg;
    }
}