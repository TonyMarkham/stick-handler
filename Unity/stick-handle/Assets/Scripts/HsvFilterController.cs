using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UIElements;

public class HsvFilterController : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private string _defaultHost = "test-pi";
    [SerializeField] private WorldCalibrationData _calibrationData;

    [Header("Image Panels")]
    [SerializeField] private StillImagePanelController _originalPanel;
    [SerializeField] private StillImagePanelController _maskPanel;
    [SerializeField] private StillImagePanelController _overlayPanel;

    private TextField _ipField;
    private Button    _captureBtn;
    private Button    _orangeSetBtn;
    private Button    _greenSetBtn;
    private Label     _statusLabel;
    private SliderInt _hMin, _hMax, _sMin, _sMax, _vMin, _vMax;
    private Label     _orangeBlobCountLabel;
    private Label     _greenBlobCountLabel;

    private bool      _hasCaptured;
    private bool      _useDetected;
    private Coroutine _pendingRefresh;
    private string    _activeBank; // "orange" or "green" — last bank targeted by Set

    private InputActionReference _aAction;
    private HsvPreset            _hoveredPreset;
    private SavingOverlay        _hoveredOverlay;
    private Button               _hoveredBtn;
    private float                _aHoldTime;
    private float                _saveCompleteCooldown;

    private const float SaveHoldDuration = 3f;

    // ── Preset JSON types ────────────────────────────────────────────────────

    [System.Serializable]
    private class HsvPreset
    {
        public int hMin, hMax, sMin, sMax, vMin, vMax;
    }

    [System.Serializable]
    private class PresetBank
    {
        public string      name;
        public HsvPreset[] presets;
    }

    [System.Serializable]
    private class PresetConfig
    {
        public PresetBank[] banks;
    }

    private const string PresetFileName = "hsv_presets.json";
    private PresetConfig _presetConfig;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _presetConfig = LoadPresets();
        _aAction = FindObjectOfType<XRInputManager>()?.GetRightA();

        var root = _uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[HsvFilter] rootVisualElement is null — assign Panel Settings in Inspector");
            return;
        }

        _ipField      = root.Q<TextField>("ip-field");
        _captureBtn   = root.Q<Button>("capture-btn");
        _orangeSetBtn = root.Q<Button>("orange-set-btn");
        _greenSetBtn  = root.Q<Button>("green-set-btn");
        _statusLabel  = root.Q<Label>("status-label");
        _hMin        = root.Q<SliderInt>("h-min");
        _hMax        = root.Q<SliderInt>("h-max");
        _sMin        = root.Q<SliderInt>("s-min");
        _sMax        = root.Q<SliderInt>("s-max");
        _vMin        = root.Q<SliderInt>("v-min");
        _vMax        = root.Q<SliderInt>("v-max");

        if (_ipField      == null) { Debug.LogError("[HsvFilter] 'ip-field' not found");      return; }
        if (_captureBtn   == null) { Debug.LogError("[HsvFilter] 'capture-btn' not found");   return; }
        if (_orangeSetBtn == null) { Debug.LogError("[HsvFilter] 'orange-set-btn' not found"); return; }
        if (_greenSetBtn  == null) { Debug.LogError("[HsvFilter] 'green-set-btn' not found");  return; }
        if (_statusLabel  == null) { Debug.LogError("[HsvFilter] 'status-label' not found");  return; }
        if (_hMin == null || _hMax == null ||
            _sMin == null || _sMax == null ||
            _vMin == null || _vMax == null)
        {
            Debug.LogError("[HsvFilter] One or more HSV sliders not found in UXML");
            return;
        }

        _orangeBlobCountLabel = root.Q<Label>("orange-blob-count-label");
        _greenBlobCountLabel  = root.Q<Label>("green-blob-count-label");

        _ipField.value = _defaultHost;
        _captureBtn.clicked   += HandleCapture;
        _orangeSetBtn.clicked += HandleSetOrange;
        _greenSetBtn.clicked  += HandleSetGreen;

        _originalPanel?.EnableToggle();
        if (_originalPanel != null)
            _originalPanel.OnToggleChanged += OnDetectedToggleChanged;

        _hMin.RegisterValueChangedCallback(OnSliderChanged);
        _hMax.RegisterValueChangedCallback(OnSliderChanged);
        _sMin.RegisterValueChangedCallback(OnSliderChanged);
        _sMax.RegisterValueChangedCallback(OnSliderChanged);
        _vMin.RegisterValueChangedCallback(OnSliderChanged);
        _vMax.RegisterValueChangedCallback(OnSliderChanged);

        WirePresetBank(root, "orange", 0);
        WirePresetBank(root, "green",  1);
    }

    private void OnDisable()
    {
        if (_captureBtn   != null) _captureBtn.clicked   -= HandleCapture;
        if (_orangeSetBtn != null) _orangeSetBtn.clicked -= HandleSetOrange;
        if (_greenSetBtn  != null) _greenSetBtn.clicked  -= HandleSetGreen;
        if (_originalPanel != null) _originalPanel.OnToggleChanged -= OnDetectedToggleChanged;

        if (_hMin != null) _hMin.UnregisterValueChangedCallback(OnSliderChanged);
        if (_hMax != null) _hMax.UnregisterValueChangedCallback(OnSliderChanged);
        if (_sMin != null) _sMin.UnregisterValueChangedCallback(OnSliderChanged);
        if (_sMax != null) _sMax.UnregisterValueChangedCallback(OnSliderChanged);
        if (_vMin != null) _vMin.UnregisterValueChangedCallback(OnSliderChanged);
        if (_vMax != null) _vMax.UnregisterValueChangedCallback(OnSliderChanged);

        StopAllCoroutines();
        _pendingRefresh = null;
    }

    // ── A-button hold to save ────────────────────────────────────────────────

    private void Update()
    {
        if (_saveCompleteCooldown > 0)
        {
            _saveCompleteCooldown -= Time.deltaTime;
            if (_saveCompleteCooldown <= 0)
                _hoveredOverlay?.Hide();
            return;
        }

        if (_hoveredPreset == null || _aAction == null)
        {
            _aHoldTime = 0;
            return;
        }

        if (_aAction.action.IsPressed())
        {
            if (!_hoveredOverlay.visible) _hoveredOverlay.Show();
            _aHoldTime += Time.deltaTime;
            _hoveredOverlay.Progress = Mathf.Clamp01(_aHoldTime / SaveHoldDuration);

            if (_aHoldTime >= SaveHoldDuration)
            {
                _hoveredPreset.hMin = _hMin.value;
                _hoveredPreset.hMax = _hMax.value;
                _hoveredPreset.sMin = _sMin.value;
                _hoveredPreset.sMax = _sMax.value;
                _hoveredPreset.vMin = _vMin.value;
                _hoveredPreset.vMax = _vMax.value;

                SavePresets();
                _hoveredBtn.style.backgroundColor = new StyleColor(MedianHsvColor(_hoveredPreset));

                _aHoldTime            = 0;
                _saveCompleteCooldown = 0.5f;
            }
        }
        else if (_aHoldTime > 0)
        {
            _aHoldTime = 0;
            _hoveredOverlay.Hide();
        }
    }

    // ── Detected toggle ──────────────────────────────────────────────────────

    private void OnDetectedToggleChanged(bool useDetected)
    {
        _useDetected = useDetected;
        if (!_hasCaptured) return;

        StopAllCoroutines();
        _pendingRefresh = null;
        StartCoroutine(FetchOriginal());
    }

    private IEnumerator FetchOriginal()
    {
        string endpoint = _useDetected ? "detected" : "original";
        yield return StartCoroutine(FetchImage($"http://{Host()}:8080/still/{endpoint}", _originalPanel));
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

        string originalEndpoint = _useDetected ? "detected" : "original";
        yield return StartCoroutine(FetchImage($"http://{host}:8080/still/{originalEndpoint}", _originalPanel));
        yield return StartCoroutine(FetchMaskAndOverlay());

        SetStatus("Ready — adjust sliders to refine");
        _captureBtn.SetEnabled(true);
    }

    private void HandleSetOrange() { _activeBank = "orange"; StartCoroutine(PutHsvFilter("orange")); }
    private void HandleSetGreen()  { _activeBank = "green";  StartCoroutine(PutHsvFilter("green")); }

    private IEnumerator PutHsvFilter(string color)
    {
        string json = $"{{\"h_min\":{_hMin.value},\"h_max\":{_hMax.value}," +
                      $"\"s_min\":{_sMin.value},\"s_max\":{_sMax.value}," +
                      $"\"v_min\":{_vMin.value},\"v_max\":{_vMax.value}}}";

        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest($"http://{Host()}:8080/hsv/{color}", "PUT");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            SetStatus($"Set {color} failed: {req.error}");
        else
            SetStatus($"{color} filter updated");
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

        yield return StartCoroutine(FetchImage($"http://{host}:8080/still/mask?{query}", _maskPanel));
        yield return StartCoroutine(FetchOverlayWithCount($"http://{host}:8080/still/overlay?{query}", _overlayPanel));

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

    private IEnumerator FetchOverlayWithCount(string url, StillImagePanelController panel)
    {
        if (panel == null) yield break;

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[HsvFilter] FetchOverlayWithCount failed for {url}: {req.error}");
            yield break;
        }

        panel.SetTexture(DownloadHandlerTexture.GetContent(req));

        string countHeader = req.GetResponseHeader("X-Blob-Count");
        if (_activeBank != null && int.TryParse(countHeader, out int count))
            UpdateBlobCount(_activeBank, count);
    }

    private void UpdateBlobCount(string bank, int count)
    {
        if (bank == "orange")
        {
            bool valid = count == 4;
            if (_orangeBlobCountLabel != null)
            {
                _orangeBlobCountLabel.text = valid ? "4 blobs ✓" : $"{count} blobs ✗ — must be 4";
                _orangeBlobCountLabel.style.color = valid
                    ? new StyleColor(new Color(0.2f, 0.8f, 0.2f))
                    : new StyleColor(new Color(1f, 0.3f, 0.3f));
            }
            if (_calibrationData != null) _calibrationData.orangeValid = valid;
        }
        else if (bank == "green")
        {
            bool valid = count == 1;
            if (_greenBlobCountLabel != null)
            {
                _greenBlobCountLabel.text = valid ? "1 blob ✓" : $"{count} blobs ✗ — must be 1";
                _greenBlobCountLabel.style.color = valid
                    ? new StyleColor(new Color(0.2f, 0.8f, 0.2f))
                    : new StyleColor(new Color(1f, 0.3f, 0.3f));
            }
            if (_calibrationData != null) _calibrationData.greenValid = valid;
        }
    }

    // ── Presets ──────────────────────────────────────────────────────────────

    private static PresetConfig LoadPresets()
    {
        string srcPath = Path.Combine(Application.streamingAssetsPath, PresetFileName);

#if UNITY_EDITOR
        // In the editor always read StreamingAssets directly so edits take effect immediately
        if (!File.Exists(srcPath))
        {
            Debug.LogError($"[HsvFilter] Preset file not found at {srcPath}");
            return null;
        }
        string json = File.ReadAllText(srcPath);
        Debug.Log($"[HsvFilter] Loaded presets from {srcPath}");
#else
        // On device: copy from StreamingAssets to persistentDataPath once, then load from there
        // (allows on-device edits via adb to persist across sessions)
        string destPath = Path.Combine(Application.persistentDataPath, PresetFileName);
        if (!File.Exists(destPath))
        {
            if (!File.Exists(srcPath))
            {
                Debug.LogError($"[HsvFilter] Default preset file not found at {srcPath}");
                return null;
            }
            File.Copy(srcPath, destPath);
            Debug.Log($"[HsvFilter] Copied default presets to {destPath}");
        }
        string json = File.ReadAllText(destPath);
        Debug.Log($"[HsvFilter] Loaded presets from {destPath}");
#endif

        return JsonUtility.FromJson<PresetConfig>(json);
    }

    private void WirePresetBank(VisualElement root, string bankKey, int bankIndex)
    {
        if (_presetConfig?.banks == null || bankIndex >= _presetConfig.banks.Length) return;

        PresetBank bank = _presetConfig.banks[bankIndex];

        for (int i = 0; i < 5; i++)
        {
            int capturedIndex = i;
            var btn = root.Q<Button>($"{bankKey}-preset-{i + 1}");
            if (btn == null)
            {
                Debug.LogWarning($"[HsvFilter] Button '{bankKey}-preset-{i + 1}' not found in UXML");
                continue;
            }

            if (capturedIndex >= bank.presets.Length)
            {
                Debug.LogWarning($"[HsvFilter] No preset data for '{bankKey}' index {capturedIndex}");
                continue;
            }

            HsvPreset preset = bank.presets[capturedIndex];
            btn.style.backgroundColor = new StyleColor(MedianHsvColor(preset));

            var overlay = new SavingOverlay();
            btn.Add(overlay);

            string originalText = btn.text;
            overlay.OnShow += () => btn.text = "";
            overlay.OnHide += () => btn.text = originalText;

            btn.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _hoveredPreset  = preset;
                _hoveredOverlay = overlay;
                _hoveredBtn     = btn;
            });

            btn.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_hoveredPreset != preset) return;
                _aHoldTime      = 0;
                _hoveredPreset  = null;
                _hoveredOverlay = null;
                _hoveredBtn     = null;
                overlay.Hide();
            });

            btn.RegisterCallback<PointerUpEvent>(_ => ApplyPreset(preset));
        }
    }

    private void SavePresets()
    {
        string json = JsonUtility.ToJson(_presetConfig, true);
#if UNITY_EDITOR
        File.WriteAllText(Path.Combine(Application.streamingAssetsPath, PresetFileName), json);
#else
        File.WriteAllText(Path.Combine(Application.persistentDataPath, PresetFileName), json);
#endif
        Debug.Log("[HsvFilter] Presets saved");
    }

    private void ApplyPreset(HsvPreset p)
    {
        _hMin.value = p.hMin;
        _hMax.value = p.hMax;
        _sMin.value = p.sMin;
        _sMax.value = p.sMax;
        _vMin.value = p.vMin;
        _vMax.value = p.vMax;
    }

    private static Color MedianHsvColor(HsvPreset p) =>
        Color.HSVToRGB(
            ((p.hMin + p.hMax) / 2f) / 179f,
            ((p.sMin + p.sMax) / 2f) / 255f,
            ((p.vMin + p.vMax) / 2f) / 255f);

    // ── SavingOverlay ────────────────────────────────────────────────────────

    private class SavingOverlay : VisualElement
    {
        private float        _progress;
        private readonly Label _label;

        public float Progress
        {
            get => _progress;
            set { _progress = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public SavingOverlay()
        {
            style.position       = Position.Absolute;
            style.left           = 0; style.right  = 0;
            style.top            = 0; style.bottom = 0;
            style.alignItems     = Align.Center;
            style.justifyContent = Justify.Center;
            pickingMode          = PickingMode.Ignore;

            _label = new Label("SAVING") { pickingMode = PickingMode.Ignore };
            _label.style.color                   = Color.white;
            _label.style.fontSize                = 7;
            _label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _label.style.position                = Position.Absolute;
            Add(_label);

            generateVisualContent += Draw;
            visible = false;
        }

        public event System.Action OnShow;
        public event System.Action OnHide;

        public void Show() { visible = true;  Progress = 0; OnShow?.Invoke(); }
        public void Hide() { visible = false; Progress = 0; OnHide?.Invoke(); }

        private void Draw(MeshGenerationContext ctx)
        {
            if (_progress <= 0) return;

            float cx     = layout.width  / 2f;
            float cy     = layout.height / 2f;
            float radius = Mathf.Min(cx, cy) - 3f;

            var p2d = ctx.painter2D;
            p2d.lineWidth = 3f;

            // Faint background ring
            p2d.strokeColor = new Color(1f, 1f, 1f, 0.25f);
            p2d.BeginPath();
            p2d.Arc(new Vector2(cx, cy), radius, 0f, 360f, ArcDirection.Clockwise);
            p2d.Stroke();

            // Filling arc, clockwise from top
            p2d.strokeColor = Color.white;
            p2d.BeginPath();
            p2d.Arc(new Vector2(cx, cy), radius, -90f, -90f + _progress * 360f, ArcDirection.Clockwise);
            p2d.Stroke();
        }
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
