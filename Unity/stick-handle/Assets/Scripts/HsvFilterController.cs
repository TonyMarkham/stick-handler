using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

public class HsvFilterController : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private string _defaultHost = "test-pi";

    [Header("Image Panels")]
    [SerializeField] private StillImagePanelController _originalPanel;
    [SerializeField] private StillImagePanelController _maskPanel;
    [SerializeField] private StillImagePanelController _overlayPanel;

    private TextField _ipField;
    private Button    _captureBtn;
    private Label     _statusLabel;
    private SliderInt _hMin, _hMax, _sMin, _sMax, _vMin, _vMax;

    private bool      _hasCaptured;
    private Coroutine _pendingRefresh;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        var root = _uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[HsvFilter] rootVisualElement is null — assign Panel Settings in Inspector");
            return;
        }

        _ipField     = root.Q<TextField>("ip-field");
        _captureBtn  = root.Q<Button>("capture-btn");
        _statusLabel = root.Q<Label>("status-label");
        _hMin        = root.Q<SliderInt>("h-min");
        _hMax        = root.Q<SliderInt>("h-max");
        _sMin        = root.Q<SliderInt>("s-min");
        _sMax        = root.Q<SliderInt>("s-max");
        _vMin        = root.Q<SliderInt>("v-min");
        _vMax        = root.Q<SliderInt>("v-max");

        if (_ipField     == null) { Debug.LogError("[HsvFilter] 'ip-field' not found");     return; }
        if (_captureBtn  == null) { Debug.LogError("[HsvFilter] 'capture-btn' not found");  return; }
        if (_statusLabel == null) { Debug.LogError("[HsvFilter] 'status-label' not found"); return; }
        if (_hMin == null || _hMax == null ||
            _sMin == null || _sMax == null ||
            _vMin == null || _vMax == null)
        {
            Debug.LogError("[HsvFilter] One or more HSV sliders not found in UXML");
            return;
        }

        _ipField.value = _defaultHost;
        _captureBtn.clicked += HandleCapture;

        _hMin.RegisterValueChangedCallback(OnSliderChanged);
        _hMax.RegisterValueChangedCallback(OnSliderChanged);
        _sMin.RegisterValueChangedCallback(OnSliderChanged);
        _sMax.RegisterValueChangedCallback(OnSliderChanged);
        _vMin.RegisterValueChangedCallback(OnSliderChanged);
        _vMax.RegisterValueChangedCallback(OnSliderChanged);
    }

    private void OnDisable()
    {
        if (_captureBtn != null) _captureBtn.clicked -= HandleCapture;

        if (_hMin != null) _hMin.UnregisterValueChangedCallback(OnSliderChanged);
        if (_hMax != null) _hMax.UnregisterValueChangedCallback(OnSliderChanged);
        if (_sMin != null) _sMin.UnregisterValueChangedCallback(OnSliderChanged);
        if (_sMax != null) _sMax.UnregisterValueChangedCallback(OnSliderChanged);
        if (_vMin != null) _vMin.UnregisterValueChangedCallback(OnSliderChanged);
        if (_vMax != null) _vMax.UnregisterValueChangedCallback(OnSliderChanged);

        StopAllCoroutines();
        _pendingRefresh = null;
    }

    // ── Capture ─────────────────────────────────────────────────────────────

    private void HandleCapture()
    {
        StopAllCoroutines();
        _pendingRefresh = null;
        StartCoroutine(CaptureAndFetch());
    }

    private IEnumerator CaptureAndFetch()
    {
        _captureBtn.SetEnabled(false);
        SetStatus("Capturing...");

        string host = Host();

        using var captureReq = new UnityWebRequest($"http://{host}:8080/still/capture", "POST");
        captureReq.downloadHandler = new DownloadHandlerBuffer();
        yield return captureReq.SendWebRequest();

        if (captureReq.result != UnityWebRequest.Result.Success)
        {
            SetStatus($"Capture failed: {captureReq.error}");
            _captureBtn.SetEnabled(true);
            yield break;
        }

        _hasCaptured = true;
        SetStatus("Fetching images...");

        yield return StartCoroutine(FetchImage($"http://{host}:8080/still/original", _originalPanel));
        yield return StartCoroutine(FetchMaskAndOverlay());

        SetStatus("Ready — adjust sliders to refine");
        _captureBtn.SetEnabled(true);
    }

    // ── Slider debounce ──────────────────────────────────────────────────────

    private void OnSliderChanged(ChangeEvent<int> _)
    {
        if (!_hasCaptured) return;

        if (_pendingRefresh != null) StopCoroutine(_pendingRefresh);
        _pendingRefresh = StartCoroutine(DebouncedRefresh());
    }

    private IEnumerator DebouncedRefresh()
    {
        yield return new WaitForSeconds(0.25f);
        yield return StartCoroutine(FetchMaskAndOverlay());
        _pendingRefresh = null;
    }

    // ── Image fetching ───────────────────────────────────────────────────────

    private IEnumerator FetchMaskAndOverlay()
    {
        string host  = Host();
        string query = HsvQuery();

        SetStatus("Updating...");

        yield return StartCoroutine(FetchImage($"http://{host}:8080/still/mask?{query}",    _maskPanel));
        yield return StartCoroutine(FetchImage($"http://{host}:8080/still/overlay?{query}", _overlayPanel));

        SetStatus("Ready — adjust sliders to refine");
    }

    private IEnumerator FetchImage(string url, StillImagePanelController panel)
    {
        if (panel == null) yield break;

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[HsvFilter] FetchImage failed for {url}: {req.error}");
            yield break;
        }

        panel.SetTexture(DownloadHandlerTexture.GetContent(req));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string Host() =>
        string.IsNullOrWhiteSpace(_ipField?.value) ? _defaultHost : _ipField.value.Trim();

    private string HsvQuery() =>
        $"h_min={_hMin.value}&h_max={_hMax.value}" +
        $"&s_min={_sMin.value}&s_max={_sMax.value}" +
        $"&v_min={_vMin.value}&v_max={_vMax.value}";

    private void SetStatus(string msg)
    {
        Debug.Log($"[HsvFilter] {msg}");
        if (_statusLabel != null) _statusLabel.text = msg;
    }
}
