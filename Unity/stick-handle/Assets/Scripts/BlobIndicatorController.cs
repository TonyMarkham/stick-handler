using NativeWebSocket;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Subscribes to ws://serverUrl:8080/tracking and moves this GO to the world-space position
/// computed by applying the calibration homography to each received pixel coordinate.
///
/// Requires: Server-Game-Loop.md tracking WebSocket must be implemented on the server.
/// </summary>
public class BlobIndicatorController : MonoBehaviour
{
    [SerializeField] private WorldCalibrationData _calibrationData;
    [SerializeField] private string _serverUrl = "test-pi";

    private WebSocket _ws;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (_calibrationData == null)
        {
            Debug.LogWarning("[BlobIndicator] No WorldCalibrationData assigned — tracking disabled");
            return;
        }

        _ws = new WebSocket($"ws://{_serverUrl}:8080/tracking");
        _ws.OnMessage += OnTrackingMessage;
        _ = _ws.Connect();
    }

    private void Update()
    {
        _ws?.DispatchMessageQueue();
    }

    private void OnDisable()
    {
        if (_ws == null) return;
        _ws.OnMessage -= OnTrackingMessage;
        _ = _ws.Close();
        _ws = null;
    }

    // ── Message handler ──────────────────────────────────────────────────────

    private void OnTrackingMessage(byte[] data)
    {
        float[] m = _calibrationData.transformMatrix;
        if (m == null || m.Length < 9) return;

        string text = System.Text.Encoding.UTF8.GetString(data);
        try
        {
            var root = JObject.Parse(text);
            float px = root.Value<float>("x");
            float py = root.Value<float>("y");

            // Apply 3x3 homography M * [px, py, 1]ᵀ then normalise
            float wx = m[0] * px + m[1] * py + m[2];
            float wy = m[3] * px + m[4] * py + m[5];
            float wz = m[6] * px + m[7] * py + m[8];

            if (Mathf.Abs(wz) < 1e-6f) return;

            transform.position = new Vector3(wx / wz, 0f, wy / wz);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[BlobIndicator] Parse error: {ex.Message}\nRaw: {text}");
        }
    }
}
