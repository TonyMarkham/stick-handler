#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class SignalingClient
{
    // ── Events ──────────────────────────────────────────────────────────
    // All five events are declared nullable (?) because with <Nullable>enable</Nullable>
    // non-nullable event fields that are not initialised in an explicit constructor generate
    // CS8618. SignalingClient has no explicit constructor, so all five produce CS8618 without ?.
    // All existing ?.Invoke() call sites are already correct for nullable events — no further
    // changes are needed at call sites.
    public event Action<string>? OnAnswer; // sdp string

    // sdpMid is string? because Option<String> on the Rust side serialises as JSON null.
    public event Action<string, string?, ushort?>? OnIceCandidate; // candidate, sdpMid (nullable), sdpMLineIndex

    public event Action<string>? OnError;

    // Fired when the WebSocket closes (Pi reboot, network drop, server restart).
    // Callers (e.g. WebRtcReceiver) must subscribe to restart ICE negotiation.
    public event Action<WebSocketCloseCode>? OnDisconnected;

    // Fired when the WebSocket handshake completes and the socket is open.
    // Callers must wait for this event before calling SendOfferAsync — sending on a
    // socket not yet open hits the WebSocketState.Open guard in SendOfferAsync and
    // fires OnError with a diagnostic message.
    public event Action? OnConnected;

    // ── Thread-safety contract ───────────────────────────────────────────
    // All five public events fire on the Unity main thread.
    // OnAnswer, OnIceCandidate  — via NativeWebSocket's DispatchMessageQueue().
    // OnConnected, OnDisconnected — NativeWebSocket fires these on its internal
    //   background thread (the receive Task launched by Connect()); they are enqueued into
    //   _mainThreadQueue and drained by DispatchMessages() each frame.
    // OnError — two paths:
    //   (a) NativeWebSocket background thread (connection/receive errors): enqueued via _mainThreadQueue.
    //   (b) OnRawMessage parse failure: fired directly — OnRawMessage itself runs on the Unity
    //       main thread (dispatched by _ws.DispatchMessageQueue() inside DispatchMessages()),
    //       so direct invocation is safe. Do NOT call OnRawMessage from a background thread.
    // All subscribers can safely call any Unity main-thread-only API from any event handler.
    private WebSocket? _ws;

    // _mainThreadQueue marshals OnConnected / OnError / OnDisconnected from NativeWebSocket's
    // background thread onto the Unity main thread. Drained in DispatchMessages() each frame.
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

    // Stored delegate references so the handlers can be unsubscribed on reconnect and in CloseAsync.
    // Anonymous lambdas cannot be removed with -= because each lambda creates a new delegate instance.
    // Field types must match NativeWebSocket's custom named delegate types exactly — C# delegates
    // are nominally typed; Action / Action<T> are not assignable to WebSocketXxxEventHandler even
    // though their signatures are identical, producing CS0029 compile errors on every += / -=.
    private WebSocketOpenEventHandler? _wsOnOpen;
    private WebSocketErrorEventHandler? _wsOnError;

    private WebSocketCloseEventHandler? _wsOnClose;

    // Per-socket CancellationTokenSource. Cancelled in CloseAsync before _ws.Close() so
    // the ContinueWith(OnlyOnFaulted) fault continuation — which captures the per-socket
    // CTS local `cts` from ConnectAsync — does not enqueue OnError when the receive-loop
    // Task is faulted as a side effect of an intentional close (ObjectDisposedException or
    // OperationCanceledException). A new instance is created in ConnectAsync for each new
    // socket so the guard is scoped to one socket lifetime and cannot race with a subsequent
    // reconnect resetting a shared field. CancellationToken.IsCancellationRequested is
    // thread-safe (volatile-read internally) — no volatile keyword required.
    private CancellationTokenSource _socketCts = new CancellationTokenSource();

    // ── Connection ───────────────────────────────────────────────────────
    /// <remarks>
    /// On close-failure during reconnect, this method BOTH faults the returned
    /// <see cref="Task"/> AND fires <see cref="OnError"/> on the next
    /// <see cref="DispatchMessages"/> call. Callers must use one notification
    /// path only — do not both <c>await</c> this method and subscribe to
    /// <see cref="OnError"/>, or the failure is handled twice.
    /// </remarks>
    public async Task ConnectAsync(string host)
    {
        // Close and unsubscribe the old socket before replacing it to avoid
        // leaking server connection slots and duplicate event callbacks on reconnect.
        if (_ws != null)
        {
            _ws.OnOpen -= _wsOnOpen;
            _ws.OnMessage -= OnRawMessage;
            _ws.OnError -= _wsOnError;
            _ws.OnClose -= _wsOnClose;
            // Use try/catch/finally so _ws is always nulled even if Close() throws
            // (e.g. transport error during close handshake). Without finally, a thrown
            // exception leaves _ws pointing at a dead socket; the next reconnect attempt
            // finds _ws != null, tries to unsubscribe already-removed handlers, and
            // calls Close() on a failed socket — producing a second exception.
            // The catch block enqueues OnError AND rethrows — dual notification:
            // fire-and-forget callers get the OnError event one frame later; awaiting
            // callers see the thrown exception. A caller that BOTH awaits ConnectAsync
            // AND subscribes to OnError will receive this failure twice — choose one
            // error-handling strategy: await the Task, or subscribe OnError, not both.
            try
            {
                await _ws.Close();
            }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() =>
                    OnError?.Invoke($"[Signaling] Close failed during reconnect: {ex.Message}"));
                throw;
            }
            finally
            {
                _ws = null;
            }
        }

        // Cancel the guard for the outgoing socket before creating the new one.
        // The old ContinueWith lambda captured the old CTS local; cancelling _socketCts
        // here arms that lambda's guard without a shared-field race. A fresh CTS is then
        // captured as a new local for the new socket's ContinueWith, scoping the guard to
        // this socket lifetime. Do NOT Dispose the old CTS here — the old ContinueWith may
        // still be queued and will read IsCancellationRequested; Dispose is safe once the
        // old Task has completed, but tracking that lifetime is not worth the complexity;
        // GC cleanup is correct here.
        _socketCts.Cancel();
        var cts = new CancellationTokenSource();
        _socketCts = cts;
        string url = $"ws://{host}:8080/ws";
        _ws = new WebSocket(url);

        // Store as a field for symmetric unsubscribe in ConnectAsync (reconnect) and CloseAsync.
        // Anonymous lambdas cannot be removed with -= because each lambda creates a new delegate
        // instance; _wsOnOpen keeps the pattern consistent with _wsOnError/_wsOnClose.
        // Enqueue into _mainThreadQueue instead of invoking directly — NativeWebSocket fires
        // OnOpen, OnError, and OnClose on its background thread; DispatchMessages() drains the
        // queue on the Unity main thread each frame so subscribers can safely call any Unity API.
        _wsOnOpen = () => _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());
        _ws.OnOpen += _wsOnOpen;
        _wsOnError = err => _mainThreadQueue.Enqueue(() => OnError?.Invoke(err));
        // Surface the disconnect to callers so WebRtcReceiver can restart ICE negotiation
        // when the Pi reboots or the network drops. Without this, the app silently hangs.
        _wsOnClose = code => _mainThreadQueue.Enqueue(() =>
        {
            Debug.Log($"[Signaling] WebSocket closed: {code}");
            OnDisconnected?.Invoke(code);
        });

        _ws.OnMessage += OnRawMessage;
        _ws.OnError += _wsOnError;
        _ws.OnClose += _wsOnClose;

        // Fire-and-forget the receive loop. On Android / Meta Quest 3, NativeWebSocket's
        // non-WebGL path runs _ws.Connect() as:
        //   await m_Socket.ConnectAsync(uri, token);  // handshake
        //   OnOpen?.Invoke();
        //   await Receive();                          // blocks until socket closes
        // Awaiting Connect() directly would block ConnectAsync() for the entire session
        // lifetime, so control never returns to the caller after the handshake completes.
        // Callers must subscribe OnConnected and wait for it before sending the offer.
        // ContinueWith(OnlyOnFaulted) routes any unobserved exception to OnError via
        // _mainThreadQueue. The explicit 4-argument overload is required — the 2-argument
        // overload uses TaskScheduler.Current, which inside Unity's UnitySynchronizationContext
        // may not be TaskScheduler.Default.
        _ = _ws.Connect().ContinueWith(
            t =>
            {
                if (!cts.Token.IsCancellationRequested)
                    _mainThreadQueue.Enqueue(() =>
                        OnError?.Invoke($"[Signaling] Receive loop faulted: {t.Exception}"));
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    /// <summary>Must be called every frame from MonoBehaviour.Update().</summary>
    public void DispatchMessages()
    {
        // Phase 1: Drain inbound data messages (OnAnswer, OnIceCandidate) FIRST via
        // NativeWebSocket's own queue. Data events must be processed before lifecycle
        // events so that a same-frame disconnect scenario does not fire OnDisconnected —
        // causing WebRtcReceiver to reset the peer connection — before OnAnswer is
        // delivered for SetRemoteDescription.
        try
        {
            _ws?.DispatchMessageQueue();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        // Phase 2: Drain lifecycle events (OnConnected, OnError, OnDisconnected)
        // enqueued from NativeWebSocket's background thread onto the Unity main thread.
        // Each action() is wrapped in try/catch so a subscriber exception does not
        // break the loop early and leave remaining lifecycle callbacks undelivered.
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    public async Task CloseAsync()
    {
        if (_ws != null)
        {
            // Unsubscribe all handlers before Close() — NativeWebSocket may deliver buffered
            // messages during the TCP close handshake, firing stale events at a caller who has torn down.
            _ws.OnOpen -= _wsOnOpen;
            _ws.OnMessage -= OnRawMessage;
            _ws.OnError -= _wsOnError;
            _ws.OnClose -= _wsOnClose;
            // Cancel the per-socket CTS to signal the ContinueWith(OnlyOnFaulted) fault
            // continuation that this close is intentional.
            _socketCts.Cancel();
            // try/catch/finally: _ws is always nulled (finally) and Close() failures are logged (catch).
            // CloseAsync is typically called from MonoBehaviour.OnDestroy — the caller cannot
            // meaningfully handle a close failure and usually fire-and-forgets the Task.
            try
            {
                await _ws.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Signaling] CloseAsync failed: {ex.Message}");
            }
            finally
            {
                _ws = null;
            }
        }
    }

    // ── Inbound ──────────────────────────────────────────────────────────
    private void OnRawMessage(byte[] data)
    {
        string text = System.Text.Encoding.UTF8.GetString(data);

        // ── Parsing phase ────────────────────────────────────────────────
        // Extract typed values from the JSON frame. Event invocations are
        // intentionally kept outside this try-catch so that a runtime fault
        // in a subscriber is never caught here and mislabeled as "Parse error:".
        string? type;
        string? sdp = null;
        string? candidate = null;
        string? sdpMid = null;
        ushort? sdpMLineIndex = null;
        string? errorMsg = null;

        try
        {
            var root = JObject.Parse(text);
            // root.Value<string?>("type") for consistency with all other fields.
            // root["type"]?.ToString() returns "" for a JSON-null token —
            // Value<string?>("type") returns C# null for both absent and JSON-null cases.
            type = root.Value<string?>("type");

            switch (type)
            {
                case "answer":
                    // root.Value<string>() maps a JSON-null JToken to C# null;
                    // root["sdp"]?.ToString() returns "" for a JSON-null token,
                    // bypassing the ?? throw guard. Value<string>() is the uniform pattern.
                    sdp = root.Value<string>("sdp")
                          ?? throw new InvalidOperationException("Answer message missing 'sdp' field");
                    break;

                case "ice-candidate":
                    candidate = root.Value<string>("candidate")
                                ?? throw new InvalidOperationException(
                                    "ice-candidate message missing 'candidate' field");
                    // root.Value<string?>() correctly maps a JSON-null token to C# null.
                    // root["sdp_mid"]?.ToString() returns "" for JSON null — passing ""
                    // to addIceCandidate() instead of null misroutes ICE candidates.
                    sdpMid = root.Value<string?>("sdp_mid");
                    sdpMLineIndex = root.Value<ushort?>("sdp_mline_index");
                    break;

                case "error":
                    errorMsg = root.Value<string?>("message") ?? "(no message)";
                    break;

                default:
                    Debug.LogWarning($"[Signaling] Unknown message type: {type}");
                    return;
            }
        }
        catch (Exception ex)
        {
            string errMsg = $"Parse error: {ex.Message}";
            Debug.LogError($"[Signaling] {errMsg}\nRaw: {text}");
            try
            {
                OnError?.Invoke(errMsg);
            }
            catch (Exception subEx)
            {
                Debug.LogException(subEx);
            }

            return;
        }

        // ── Invocation phase ─────────────────────────────────────────────
        // Fire events after the JSON parse try-catch exits cleanly so a
        // subscriber exception cannot be mislabeled as "Parse error:".
        // Each invocation is individually wrapped: NativeWebSocket's
        // DispatchMessageQueue() processes all messages in one loop — one
        // unprotected subscriber exception drops every subsequent message
        // queued for that frame (in trickle-ICE: answer arrives, WebRtcReceiver
        // throws, all ICE candidates for that frame silently lost).
        switch (type)
        {
            case "answer":
                try
                {
                    OnAnswer?.Invoke(sdp!);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                break;
            case "ice-candidate":
                try
                {
                    OnIceCandidate?.Invoke(candidate!, sdpMid, sdpMLineIndex);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                break;
            case "error":
                try
                {
                    OnError?.Invoke($"[Server] {errorMsg}");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                break;
        }
    }

    // ── Outbound ─────────────────────────────────────────────────────────

    // Explicit serializer settings with DefaultContractResolver lock the field name behaviour
    // independent of any global JsonConvert.DefaultSettings. If a Unity package (Firebase SDK,
    // Addressables, etc.) installs a ContractResolver via DefaultSettings, using
    // JsonConvert.SerializeObject(obj) without explicit settings would silently apply that
    // resolver's renaming logic. CamelCasePropertyNamesContractResolver specifically does NOT
    // rename snake_case fields such as sdp_mid — ToCamelCase only lowercases an uppercase-leading
    // initial character, so "sdp_mid" (starts with 's') is returned unchanged. However, a more
    // aggressive custom resolver that strips underscores could rename sdp_mid to "sdpMid",
    // which the Rust serde cannot match and would deserialise as None, causing trickle-ICE to
    // fail. Explicit DefaultContractResolver ensures the JSON keys exactly match the anonymous
    // type property names as written, regardless of any global settings.
    private static readonly Newtonsoft.Json.JsonSerializerSettings s_jsonSettings =
        new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver()
        };

    /// <summary>Sends the SDP offer to the Rust server.</summary>
    /// <remarks>
    /// Must be called from the Unity main thread. <see cref="OnError"/> fires synchronously
    /// on the calling thread — if called from a background thread (e.g. <c>Task.Run</c> or a
    /// <c>ContinueWith</c> without a captured <c>SynchronizationContext</c>), subscribers
    /// receive the event on a non-main thread, violating the class threading contract and
    /// risking crashes when subscribers call Unity APIs.
    /// </remarks>
    public async Task SendOfferAsync(string sdp)
    {
        // Guard against null AND non-Open states (e.g. Connecting, Closing).
        // SendText on a non-Open socket either throws or silently no-ops depending on platform.
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            // Invoke OnError so the caller has a programmatic signal — not just a Unity console
            // log. Without this, the returned Task completes as RanToCompletion while the offer
            // was never sent; WebRTC negotiation never starts and the caller has no signal.
            // Wrapped in its own try/catch: a throwing subscriber must not propagate out of the
            // send method, replacing a clean "socket not ready" return with a subscriber exception.
            string errMsg = $"SendOfferAsync failed: socket not open ({_ws?.State.ToString() ?? "null"})";
            Debug.LogWarning($"[Signaling] {errMsg}");
            try
            {
                OnError?.Invoke(errMsg);
            }
            catch (Exception subEx)
            {
                Debug.LogException(subEx);
            }

            return;
        }

        var msg = new
        {
            type = "offer",
            sdp = sdp
        };
        // SerializeObject is inside the try block so serialization failures route to OnError
        // via the catch block. If placed outside, a JsonSerializationException would propagate
        // raw out of the method without firing OnError — inconsistent with every other failure
        // path in this class, where all errors route to OnError.
        try
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(msg, s_jsonSettings);
            await _ws.SendText(json);
            Debug.Log("[Signaling] Sent offer");
        }
        catch (Exception ex)
        {
            // Route to OnError so the caller (WebRtcReceiver) can react programmatically.
            // Wrapped in its own try/catch: a throwing subscriber inside a catch block replaces
            // the original SendText exception with the subscriber's exception.
            string errMsg = $"SendOfferAsync failed: {ex.Message}";
            Debug.LogError($"[Signaling] {errMsg}");
            try
            {
                OnError?.Invoke(errMsg);
            }
            catch (Exception subEx)
            {
                Debug.LogException(subEx);
            }
        }
    }

    // sdpMid is string? because the server's Option<String> serialises as JSON null.
    /// <summary>Sends a trickle-ICE candidate to the Rust server.</summary>
    /// <remarks>
    /// Must be called from the Unity main thread. <see cref="OnError"/> fires synchronously
    /// on the calling thread — if called from a background thread (e.g. <c>Task.Run</c> or a
    /// <c>ContinueWith</c> without a captured <c>SynchronizationContext</c>), subscribers
    /// receive the event on a non-main thread, violating the class threading contract and
    /// risking crashes when subscribers call Unity APIs.
    /// </remarks>
    public async Task SendIceCandidateAsync(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            // ICE candidates trickle in continuously; if the socket closes mid-negotiation,
            // guard skips accumulate silently as RanToCompletion while the caller has no error signal.
            string errMsg = $"SendIceCandidateAsync failed: socket not open ({_ws?.State.ToString() ?? "null"})";
            Debug.LogWarning($"[Signaling] {errMsg}");
            try
            {
                OnError?.Invoke(errMsg);
            }
            catch (Exception subEx)
            {
                Debug.LogException(subEx);
            }

            return;
        }

        var msg = new
        {
            type = "ice-candidate",
            candidate = candidate,
            sdp_mid = sdpMid,
            sdp_mline_index = sdpMLineIndex
        };
        try
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(msg, s_jsonSettings);
            await _ws.SendText(json);
            Debug.Log("[Signaling] Sent ICE candidate");
        }
        catch (Exception ex)
        {
            string errMsg = $"SendIceCandidateAsync failed: {ex.Message}";
            Debug.LogError($"[Signaling] {errMsg}");
            try
            {
                OnError?.Invoke(errMsg);
            }
            catch (Exception subEx)
            {
                Debug.LogException(subEx);
            }
        }
    }
}