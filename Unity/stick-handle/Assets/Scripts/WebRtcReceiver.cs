#pragma warning disable CS8601 // Possible null reference assignment.

using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;

public class WebRtcReceiver : IDisposable
{
    // ── Public Events ────────────────────────────────────────────────────
    /// <summary>
    /// Fires on Unity's render thread when a decoded video frame is ready.
    /// Do NOT assign rawImage.texture directly here — use a volatile field
    /// and assign on the main thread in MonoBehaviour.Update().
    /// </summary>
    public event Action<Texture>? OnVideoReceived;

    /// <summary>
    /// Fires when a local ICE candidate is ready to be forwarded to the server.
    /// sdpMid is nullable — RTCIceCandidate.SdpMid is string? in the Unity WebRTC SDK.
    /// </summary>
    public event Action<string, string?, int?>? OnIceCandidateReady;

    // ── Private State ────────────────────────────────────────────────────
    private RTCPeerConnection? _pc;
    private VideoStreamTrack? _videoTrack;

    // ── Constructor ──────────────────────────────────────────────────────
    public WebRtcReceiver()
    {
        var config = new RTCConfiguration
        {
            iceServers = new[]
            {
                new RTCIceServer
                {
                    urls = new[] { "stun:stun.l.google.com:19302" }
                }
            }
        };
        _pc = new RTCPeerConnection(ref config);

        // Forward local ICE candidates to the signaling layer.
        // The C# SDK types RTCIceCandidate as non-nullable. Gathering-complete is
        // signaled via OnIceGatheringStateChange → Complete, not via a null candidate
        // (that is the JavaScript WebRTC API behaviour, not the Unity C# SDK).
        _pc.OnIceCandidate = candidate =>
            OnIceCandidateReady?.Invoke(
                candidate.Candidate,
                candidate.SdpMid,
                candidate.SdpMLineIndex);

        // Extract VideoStreamTrack when the server's video track arrives.
        // Guard against track replacement within one peer connection: unsubscribe
        // the previous track's event before overwriting _videoTrack so Dispose()
        // can always cleanly remove exactly one subscription.
        _pc.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack vt)
            {
                if (_videoTrack != null)
                    _videoTrack.OnVideoReceived -= OnVideoTrackReceived;
                _videoTrack = vt;
                _videoTrack.OnVideoReceived += OnVideoTrackReceived;
            }
        };

        _pc.OnConnectionStateChange = state =>
            Debug.Log($"[WebRTC] Connection state: {state}");

        _pc.OnIceConnectionChange = state =>
            Debug.Log($"[WebRTC] ICE connection state: {state}");
    }

    // ── Private Helpers ──────────────────────────────────────────────────
    private void OnVideoTrackReceived(Texture tex) => OnVideoReceived?.Invoke(tex);

    // ── Negotiation ──────────────────────────────────────────────────────
    /// <summary>
    /// Entry point for WebRTC negotiation. Call via StartCoroutine(receiver.StartNegotiation(...)).
    /// Adds a RecvOnly video transceiver, creates the SDP offer, sets local description,
    /// then fires onOfferCreated so the controller can send it via SignalingClient.
    /// </summary>
    /// <remarks>
    /// MUST be started with StartCoroutine — calling directly returns an enumerator object
    /// but executes zero lines of the body. SetLocalDescription is never called and the
    /// connection silently fails with no exception or error log.
    /// </remarks>
    public IEnumerator StartNegotiation(Action<string> onOfferCreated)
    {
        if (_pc == null) yield break; // Disposed before first tick — abort cleanly

        // 1. Add RecvOnly transceiver BEFORE CreateOffer.
        //    Without this, the SDP has no video m-line and the Pi server sends no video.
        var transceiverInit = new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.RecvOnly
        };
        _pc.AddTransceiver(TrackKind.Video, transceiverInit);

        // 2. Create offer — yield until the async operation completes.
        var offerOp = _pc.CreateOffer();
        yield return offerOp;
        if (_pc == null) yield break; // Dispose() called while suspended — abort cleanly
        if (offerOp.IsError)
        {
            Debug.LogError($"[WebRTC] CreateOffer failed: {offerOp.Error.message}");
            yield break;
        }

        // 3. Set local description — yield until the async operation completes.
        var desc = offerOp.Desc;
        var setLocalOp = _pc.SetLocalDescription(ref desc);
        yield return setLocalOp;
        if (_pc == null) yield break; // Dispose() called while suspended — abort cleanly
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[WebRTC] SetLocalDescription failed: {setLocalOp.Error.message}");
            yield break;
        }

        Debug.Log("[WebRTC] Offer created — sending to server");
        onOfferCreated?.Invoke(desc.sdp);
    }

    // ── Inbound Signaling ─────────────────────────────────────────────────
    /// <summary>
    /// Sets the remote description from the server's SDP answer.
    /// MUST be started via StartCoroutine — calling directly returns an IEnumerator
    /// object but executes zero lines of the body, so SetRemoteDescription is never
    /// called and the connection silently fails with no exception.
    /// </summary>
    public IEnumerator HandleAnswer(string answerSdp)
    {
        if (_pc == null) yield break; // Disposed before first tick — discard late answer

        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = answerSdp
        };

        var setRemoteOp = _pc.SetRemoteDescription(ref desc);
        yield return setRemoteOp;
        if (_pc == null) yield break; // Dispose() called while suspended — abort cleanly

        if (setRemoteOp.IsError)
            Debug.LogError($"[WebRTC] SetRemoteDescription failed: {setRemoteOp.Error.message}");
        else
            Debug.Log("[WebRTC] Remote description set — ICE negotiation underway");
    }

    /// <summary>
    /// Feeds a trickle-ICE candidate from the server into the peer connection.
    /// Synchronous — do NOT wrap in a coroutine.
    /// </summary>
    public void HandleIceCandidate(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        if (_pc == null) return; // Disposed — discard late ICE candidates silently

        var init = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex
        };
        if (!_pc.AddIceCandidate(new RTCIceCandidate(init)))
            Debug.LogWarning($"[WebRTC] AddIceCandidate rejected: mid={sdpMid} idx={sdpMLineIndex}");
    }

    // ── Disposal ─────────────────────────────────────────────────────────
    public void Dispose()
    {
        // Unsubscribe video track event before nulling the field.
        if (_videoTrack != null)
        {
            _videoTrack.OnVideoReceived -= OnVideoTrackReceived;
            _videoTrack = null;
        }

        if (_pc != null)
        {
            // Null all callbacks BEFORE Close() so WebRTC.Update() callbacks
            // that are already queued cannot fire on the frame after disposal.
            _pc.OnTrack = null!;
            _pc.OnIceCandidate = null!;
            _pc.OnConnectionStateChange = null!;
            _pc.OnIceConnectionChange = null!;
            _pc.Close();
            _pc.Dispose();
            _pc = null;
        }
    }
}