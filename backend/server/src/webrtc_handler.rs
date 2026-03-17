use crate::{
    app_state::AppState,
    camera,
    camera::VideoCodec,
    error::{ServerResult, webrtc_error},
};

use signal_server::SignalMessage;
use std::{sync::Arc, time::Duration};
use tokio::sync::{Notify, broadcast, mpsc};
use tracing::{debug, info, trace, warn};
use webrtc::{
    api::{
        APIBuilder,
        interceptor_registry::register_default_interceptors,
        media_engine::{MIME_TYPE_H264, MIME_TYPE_VP8, MediaEngine},
    },
    ice_transport::{ice_candidate::RTCIceCandidateInit, ice_server::RTCIceServer},
    interceptor::registry::Registry,
    media::Sample,
    peer_connection::{
        RTCPeerConnection, configuration::RTCConfiguration,
        peer_connection_state::RTCPeerConnectionState,
        sdp::session_description::RTCSessionDescription,
    },
    rtp_transceiver::rtp_codec::{RTCRtpCodecCapability, RTCRtpCodecParameters, RTPCodecType},
    track::track_local::{TrackLocal, track_local_static_sample::TrackLocalStaticSample},
};

const FRAME_DURATION: Duration = Duration::from_millis(33); // ~30 fps

fn h264_codec_capability() -> RTCRtpCodecCapability {
    RTCRtpCodecCapability {
        mime_type: MIME_TYPE_H264.to_owned(),
        clock_rate: 90_000,
        channels: 0,
        sdp_fmtp_line: "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=640c28"
            .to_owned(),
        rtcp_feedback: vec![],
    }
}

fn vp8_codec_capability() -> RTCRtpCodecCapability {
    RTCRtpCodecCapability {
        mime_type: MIME_TYPE_VP8.to_owned(),
        clock_rate: 90_000,
        channels: 0,
        sdp_fmtp_line: String::new(),
        rtcp_feedback: vec![],
    }
}

async fn create_peer_connection(
    stun_urls: Vec<String>,
    codec: VideoCodec,
) -> ServerResult<(Arc<RTCPeerConnection>, Arc<TrackLocalStaticSample>)> {
    let mut media_engine = MediaEngine::default();

    let (capability, payload_type) = match codec {
        VideoCodec::H264 => (h264_codec_capability(), 96u8),
        VideoCodec::Vp8 => (vp8_codec_capability(), 97u8),
    };

    media_engine
        .register_codec(
            RTCRtpCodecParameters {
                capability: capability.clone(),
                payload_type,
                ..Default::default()
            },
            RTPCodecType::Video,
        )
        .map_err(|e| webrtc_error(format!("register codec: {e}")))?;

    let mut registry = Registry::new();
    registry = register_default_interceptors(registry, &mut media_engine)
        .map_err(|e| webrtc_error(format!("register interceptors: {e}")))?;

    let api = APIBuilder::new()
        .with_media_engine(media_engine)
        .with_interceptor_registry(registry)
        .build();

    let config = RTCConfiguration {
        ice_servers: vec![RTCIceServer {
            urls: stun_urls,
            ..Default::default()
        }],
        ..Default::default()
    };

    let peer_connection = Arc::new(
        api.new_peer_connection(config)
            .await
            .map_err(|e| webrtc_error(format!("new_peer_connection: {e}")))?,
    );

    let video_track = Arc::new(TrackLocalStaticSample::new(
        capability,
        "video".to_owned(),
        "camera-stream".to_owned(),
    ));

    peer_connection
        .add_track(Arc::clone(&video_track) as Arc<dyn TrackLocal + Send + Sync>)
        .await
        .map_err(|e| webrtc_error(format!("add_track: {e}")))?;

    Ok((peer_connection, video_track))
}

pub async fn handle_peer_session(
    mut ws_rx: mpsc::UnboundedReceiver<SignalMessage>,
    ws_tx: mpsc::UnboundedSender<SignalMessage>,
    state: Arc<AppState>,
) -> ServerResult<()> {
    // Wait for the SDP offer first — the codec is detected from the offer.
    let offer_sdp = loop {
        match ws_rx.recv().await {
            Some(SignalMessage::Offer { sdp }) => break sdp,
            Some(other) => debug!("ignoring pre-offer message: {other:?}"),
            None => return Err(webrtc_error("WebSocket closed before offer")),
        }
    };

    // H.264 clients (Quest 3, NVIDIA Windows) list "H264" in the offer.
    // AMD/Intel Windows clients list only VP8/VP9/AV1 — fall back to VP8.
    let codec = if offer_sdp.contains("H264") {
        VideoCodec::H264
    } else {
        VideoCodec::Vp8
    };
    info!("Detected codec from offer: {codec:?}");

    // Start camera and store the handle in AppState so stop_active_pipeline can
    // kill it if a mode transition happens while the session is live.
    let (camera_handle, _camera_task) = camera::start_camera(codec)?;
    let mut nal_rx = camera_handle.subscribe();
    *state.webrtc_camera.lock().await = Some(camera_handle);

    let (pc, video_track) = create_peer_connection(state.stun_urls.clone(), codec).await?;

    // Notify when the peer connection is done (failed, disconnected, or closed)
    let done = Arc::new(Notify::new());
    let done_clone = Arc::clone(&done);

    let ws_tx_ice = ws_tx.clone();
    pc.on_ice_candidate(Box::new(move |candidate| {
        let ws_tx = ws_tx_ice.clone();
        Box::pin(async move {
            let Some(c) = candidate else { return };
            match c.to_json() {
                Ok(init) => {
                    let msg = SignalMessage::IceCandidate {
                        candidate: init.candidate,
                        sdp_mid: init.sdp_mid,
                        sdp_mline_index: init.sdp_mline_index,
                    };
                    let _ = ws_tx.send(msg);
                }
                Err(e) => warn!("ice candidate to_json error: {e}"),
            }
        })
    }));

    pc.on_peer_connection_state_change(Box::new(move |state| {
        let done = Arc::clone(&done_clone);
        Box::pin(async move {
            info!("peer connection state: {state}");
            match state {
                RTCPeerConnectionState::Failed | RTCPeerConnectionState::Closed => {
                    done.notify_one()
                }
                _ => {}
            }
        })
    }));

    let offer = RTCSessionDescription::offer(offer_sdp)
        .map_err(|e| webrtc_error(format!("parse offer SDP: {e}")))?;

    pc.set_remote_description(offer)
        .await
        .map_err(|e| webrtc_error(format!("set_remote_description: {e}")))?;

    let answer = pc
        .create_answer(None)
        .await
        .map_err(|e| webrtc_error(format!("create_answer: {e}")))?;

    // set_local_description starts ICE gathering; on_ice_candidate fires after this
    pc.set_local_description(answer.clone())
        .await
        .map_err(|e| webrtc_error(format!("set_local_description: {e}")))?;

    let _ = ws_tx.send(SignalMessage::Answer { sdp: answer.sdp });

    // Spawn camera sample writing task
    let track = Arc::clone(&video_track);
    let done_camera = Arc::clone(&done);
    tokio::spawn(async move {
        let mut frame_count: u64 = 0;
        loop {
            tokio::select! {
                result = nal_rx.recv() => match result {
                    Ok(frame_bytes) => {
                        let sample = Sample {
                            data: frame_bytes,
                            duration: FRAME_DURATION,
                            ..Default::default()
                        };
                        if let Err(e) = track.write_sample(&sample).await {
                            warn!("write_sample error: {e}");
                        } else {
                            frame_count += 1;
                            if frame_count.is_multiple_of(30) {
                                trace!("sent {frame_count} frames");
                            }
                        }
                    }
                    Err(broadcast::error::RecvError::Lagged(n)) => {
                        warn!("camera broadcast lagged {n} frames");
                    }
                    Err(broadcast::error::RecvError::Closed) => {
                        info!("camera broadcast closed");
                        done_camera.notify_one();
                        break;
                    }
                },
                _ = done_camera.notified() => {
                    done_camera.notify_one(); // re-notify so peer loop also exits
                    break;
                }
            }
        }
    });

    // Process incoming ICE candidates until connection ends
    loop {
        tokio::select! {
            msg = ws_rx.recv() => match msg {
                Some(SignalMessage::IceCandidate { candidate, sdp_mid, sdp_mline_index }) => {
                    let init = RTCIceCandidateInit {
                        candidate,
                        sdp_mid,
                        sdp_mline_index,
                        username_fragment: None,
                    };
                    if let Err(e) = pc.add_ice_candidate(init).await {
                        tracing::warn!("add_ice_candidate error: {e}");
                    }
                }
                Some(other) => tracing::debug!("ignoring message: {other:?}"),
                None => break, // WebSocket closed
            },
            _ = done.notified() => break,
        }
    }

    let _ = pc.close().await;

    // Clear the camera handle from AppState. If stop_active_pipeline already took it,
    // take() returns None and we skip the stop (handle was already killed externally).
    if let Some(handle) = state.webrtc_camera.lock().await.take() {
        handle.stop();
    }

    Ok(())
}
