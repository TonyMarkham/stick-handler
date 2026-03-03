use crate::error::{ServerResult, webrtc_error};

use bytes::Bytes;
use signal_server::SignalMessage;
use std::{sync::Arc, time::Duration};
use tokio::sync::{Notify, broadcast, mpsc};
use webrtc::{
    api::{
        APIBuilder,
        interceptor_registry::register_default_interceptors,
        media_engine::{MIME_TYPE_H264, MediaEngine},
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
        sdp_fmtp_line: "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f"
            .to_owned(),
        rtcp_feedback: vec![],
    }
}

pub async fn create_peer_connection(
    stun_urls: Vec<String>,
) -> ServerResult<(Arc<RTCPeerConnection>, Arc<TrackLocalStaticSample>)> {
    let mut media_engine = MediaEngine::default();

    media_engine
        .register_codec(
            RTCRtpCodecParameters {
                capability: h264_codec_capability(),
                payload_type: 96,
                ..Default::default()
            },
            RTPCodecType::Video,
        )
        .map_err(|e| webrtc_error(format!("register H264 codec: {e}")))?;

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
        h264_codec_capability(),
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
    mut nal_rx: broadcast::Receiver<Bytes>,
    stun_urls: Vec<String>,
) -> ServerResult<()> {
    let (pc, video_track) = create_peer_connection(stun_urls).await?;

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
                Err(e) => tracing::warn!("ice candidate to_json error: {e}"),
            }
        })
    }));

    pc.on_peer_connection_state_change(Box::new(move |state| {
        let done = Arc::clone(&done_clone);
        Box::pin(async move {
            tracing::info!("peer connection state: {state}");
            match state {
                RTCPeerConnectionState::Failed
                | RTCPeerConnectionState::Disconnected
                | RTCPeerConnectionState::Closed => done.notify_one(),
                _ => {}
            }
        })
    }));

    // Wait for SDP offer from browser
    let offer_sdp = loop {
        match ws_rx.recv().await {
            Some(SignalMessage::Offer { sdp }) => break sdp,
            Some(other) => tracing::debug!("ignoring pre-offer message: {other:?}"),
            None => return Err(webrtc_error("WebSocket closed before offer")),
        }
    };

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
        loop {
            tokio::select! {
                result = nal_rx.recv() => match result {
                    Ok(nal_bytes) => {
                        let sample = Sample {
                            data: nal_bytes,
                            duration: FRAME_DURATION,
                            ..Default::default()
                        };
                        if let Err(e) = track.write_sample(&sample).await {
                            tracing::warn!("write_sample error: {e}");
                            done_camera.notify_one();
                            break;
                        }
                    }
                    Err(broadcast::error::RecvError::Lagged(n)) => {
                        tracing::warn!("camera broadcast lagged {n} frames");
                    }
                    Err(broadcast::error::RecvError::Closed) => {
                        tracing::info!("camera broadcast closed");
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
    Ok(())
}
