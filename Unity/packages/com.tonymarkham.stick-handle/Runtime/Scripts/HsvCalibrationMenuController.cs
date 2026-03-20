using System;
using System.Collections;
using System.IO;
using StickHandle.Prefabs.TV;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public partial class HsvCalibrationMenuController : MonoBehaviour
    {
        private static string CLASS_NAME = nameof(HsvCalibrationMenuController);

        private const string SERVER_NAME_ELEMENT_NAME = "ip-field";
        private const string BACK_BUTTON_NAME = "back-btn";
        private const string CAPTURE_BUTTON_NAME = "capture-btn";
        private const string CAPTURE_STATUS_LABEL_NAME = "status-label";

        private const string MIN_HUE_SLIDER_NAME = "h-min";
        private const string MAX_HUE_SLIDER_NAME = "h-max";
        private const string MIN_SATURATION_SLIDER_NAME = "s-min";
        private const string MAX_SATURATION_SLIDER_NAME = "s-max";
        private const string MIN_VALUE_SLIDER_NAME = "v-min";
        private const string MAX_VALUE_SLIDER_NAME = "v-max";

        private const string ORANGE_PRESET_01_BUTTON_NAME = "orange-preset-1";
        private const string ORANGE_PRESET_02_BUTTON_NAME = "orange-preset-2";
        private const string ORANGE_PRESET_03_BUTTON_NAME = "orange-preset-3";
        private const string ORANGE_PRESET_04_BUTTON_NAME = "orange-preset-4";
        private const string ORANGE_PRESET_05_BUTTON_NAME = "orange-preset-5";
        private const string ORANGE_SET_BUTTON_NAME = "orange-set-btn";
        private const string ORANGE_BLOB_COUNT_LABEL_NAME = "orange-blob-count-label";

        private const string GREEN_PRESET_01_BUTTON_NAME = "green-preset-1";
        private const string GREEN_PRESET_02_BUTTON_NAME = "green-preset-2";
        private const string GREEN_PRESET_03_BUTTON_NAME = "green-preset-3";
        private const string GREEN_PRESET_04_BUTTON_NAME = "green-preset-4";
        private const string GREEN_PRESET_05_BUTTON_NAME = "green-preset-5";
        private const string GREEN_SET_BUTTON_NAME = "green-set-btn";
        private const string GREEN_BLOB_COUNT_LABEL_NAME = "green-blob-count-label";

        private const string PRESET_FILE_NAME = "hsv_presets.json";

        [Header("Calibration")] [SerializeField]
        private CalibrationModeController m_CalibrationModeController;

        private CalibrationModeController CalibrationModeController
        {
            get
            {
                if (m_CalibrationModeController)
                    return m_CalibrationModeController;

                Debug.LogError("CalibrationModeController is not set");
                return m_CalibrationModeController;
            }
        }

        [SerializeField] private TelevisionController m_TelevisionController;

        private TelevisionController TelevisionController
        {
            get
            {
                if (m_TelevisionController)
                    return m_TelevisionController;

                Debug.LogError("TelevisionController is not set");
                return m_TelevisionController;
            }
        }

        [SerializeField] private PanelSettings m_PanelSettings;

        public PanelSettings PanelSettings
        {
            get
            {
                if (m_PanelSettings)
                    return m_PanelSettings;

                Debug.LogError("PanelSettings is not set");
                return m_PanelSettings;
            }
        }

        [SerializeField] private VisualTreeAsset m_VisualTreeAsset;

        private VisualTreeAsset VisualTreeAsset
        {
            get
            {
                if (m_VisualTreeAsset)
                    return m_VisualTreeAsset;

                Debug.LogError("VisualTreeAsset is not set");
                return m_VisualTreeAsset;
            }
        }

        [SerializeField] private StyleSheet m_StyleSheet;

        private StyleSheet StyleSheet
        {
            get
            {
                if (m_StyleSheet)
                    return m_StyleSheet;

                Debug.LogError("m_StyleSheet is not set");
                return m_StyleSheet;
            }
        }

        private UIDocument m_UiDocument;

        [Header("Common Image Panel")] [SerializeField]
        private VisualTreeAsset m_ImagePanelUxml;

        public VisualTreeAsset ImagePanelUxml
        {
            get
            {
                if (m_ImagePanelUxml)
                    return m_ImagePanelUxml;

                Debug.LogError("ImagePanelUxml is not set");
                return m_ImagePanelUxml;
            }
        }

        [SerializeField] private StyleSheet m_ImagePanelStyleSheet;

        public StyleSheet ImagePanelStyleSheet
        {
            get
            {
                if (m_ImagePanelStyleSheet)
                    return m_ImagePanelStyleSheet;

                Debug.LogError("ImagePanelStyleSheet is not set");
                return m_ImagePanelStyleSheet;
            }
        }

        [Header("Original Image Panel")] [SerializeField]
        private StillImagePanelController m_OriginalPanel;

        [Header("Mask Image Panel")] [SerializeField]
        private StillImagePanelController m_MaskPanel;

        [Header("Overlay Image Panel")] [SerializeField]
        private StillImagePanelController m_OverlayPanel;

        private PresetConfig m_PresetConfig;

        [Header("Preset Save Action")] [SerializeField]
        private InputActionReference m_InputActionReference;
        private HsvPreset m_HoveredPreset;
        private SavingOverlay m_HoveredOverlay;
        private Button m_HoveredBtn;
        private float m_AHoldTime;
        private float m_SaveCompleteCooldown;
        private const float SAVE_HOLD_DURATION = 3f;

        #region UI Elements

        private TextField m_ServerNameTextField;

        private TextField ServerNameTextField
        {
            get
            {
                if (m_ServerNameTextField is not null)
                    return m_ServerNameTextField;

                if (m_UiDocument.rootVisualElement.Q<TextField>(SERVER_NAME_ELEMENT_NAME) is not { } textField)
                {
                    Debug.LogError($"TextField [{SERVER_NAME_ELEMENT_NAME}] is not set");
                    return null;
                }

                m_ServerNameTextField = textField;
                return m_ServerNameTextField;
            }
        }

        private Button m_BackButton;

        private Button BackButton
        {
            get
            {
                if (m_BackButton is not null)
                    return m_BackButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(BACK_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{BACK_BUTTON_NAME}] is not set");
                    return null;
                }

                m_BackButton = button;
                return m_BackButton;
            }
        }

        private Button m_CaptureButton;

        private Button CaptureButton
        {
            get
            {
                if (m_CaptureButton is not null)
                    return m_CaptureButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(CAPTURE_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{CAPTURE_BUTTON_NAME}] is not set");
                    return null;
                }

                m_CaptureButton = button;
                return m_CaptureButton;
            }
        }

        private Label m_CaptureStatusLabel;

        private Label CaptureStatusLabel
        {
            get
            {
                if (m_CaptureStatusLabel is not null)
                    return m_CaptureStatusLabel;

                if (m_UiDocument.rootVisualElement.Q<Label>(CAPTURE_STATUS_LABEL_NAME) is not { } label)
                {
                    Debug.LogError($"Label [{CAPTURE_STATUS_LABEL_NAME}] is not set");
                    return null;
                }

                m_CaptureStatusLabel = label;
                return m_CaptureStatusLabel;
            }
        }

        private SliderInt m_MinHueSlider;

        private SliderInt MinHueSlider
        {
            get
            {
                if (m_MinHueSlider is not null)
                    return m_MinHueSlider;

                if (m_UiDocument.rootVisualElement.Q<SliderInt>(MIN_HUE_SLIDER_NAME) is not { } slider)
                {
                    Debug.LogError($"SliderInt [{MIN_HUE_SLIDER_NAME}] is not set");
                    return null;
                }

                m_MinHueSlider = slider;
                return m_MinHueSlider;
            }
        }

        private SliderInt m_MaxHueSlider;

        private SliderInt MaxHueSlider
        {
            get
            {
                if (m_MaxHueSlider is not null)
                    return m_MaxHueSlider;

                if (m_UiDocument.rootVisualElement.Q<SliderInt>(MAX_HUE_SLIDER_NAME) is not { } slider)
                {
                    Debug.LogError($"SliderInt [{MAX_HUE_SLIDER_NAME}] is not set");
                    return null;
                }

                m_MaxHueSlider = slider;
                return m_MaxHueSlider;
            }
        }

        private SliderInt m_MinSaturationSlider;

        private SliderInt MinSaturationSlider
        {
            get
            {
                if (m_MinSaturationSlider is not null)
                    return m_MinSaturationSlider;

                if (m_UiDocument.rootVisualElement.Q<SliderInt>(MIN_SATURATION_SLIDER_NAME) is not { } slider)
                {
                    Debug.LogError($"SliderInt [{MIN_SATURATION_SLIDER_NAME}] is not set");
                    return null;
                }

                m_MinSaturationSlider = slider;
                return m_MinSaturationSlider;
            }
        }

        private SliderInt m_MaxSaturationSlider;

        private SliderInt MaxSaturationSlider
        {
            get
            {
                if (m_MaxSaturationSlider is not null)
                    return m_MaxSaturationSlider;

                if (m_UiDocument.rootVisualElement.Q<SliderInt>(MAX_SATURATION_SLIDER_NAME) is not { } slider)
                {
                    Debug.LogError($"SliderInt [{MAX_SATURATION_SLIDER_NAME}] is not set");
                    return null;
                }

                m_MaxSaturationSlider = slider;
                return m_MaxSaturationSlider;
            }
        }

        private SliderInt m_MinValueSlider;

        private SliderInt MinValueSlider
        {
            get
            {
                if (m_MinValueSlider is not null)
                    return m_MinValueSlider;

                if (m_UiDocument.rootVisualElement.Q<SliderInt>(MIN_VALUE_SLIDER_NAME) is not { } slider)
                {
                    Debug.LogError($"SliderInt [{MIN_VALUE_SLIDER_NAME}] is not set");
                    return null;
                }

                m_MinValueSlider = slider;
                return m_MinValueSlider;
            }
        }

        private SliderInt m_MaxValueSlider;

        private SliderInt MaxValueSlider
        {
            get
            {
                if (m_MaxValueSlider is not null)
                    return m_MaxValueSlider;

                if (m_UiDocument.rootVisualElement.Q<SliderInt>(MAX_VALUE_SLIDER_NAME) is not { } slider)
                {
                    Debug.LogError($"SliderInt [{MAX_VALUE_SLIDER_NAME}] is not set");
                    return null;
                }

                m_MaxValueSlider = slider;
                return m_MaxValueSlider;
            }
        }

        private Button m_OrangePresetOneButton;

        private Button OrangePresetOneButton
        {
            get
            {
                if (m_OrangePresetOneButton is not null)
                    return m_OrangePresetOneButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_01_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{ORANGE_PRESET_01_BUTTON_NAME}] is not set");
                    return null;
                }

                m_OrangePresetOneButton = button;
                return m_OrangePresetOneButton;
            }
        }

        private Button m_OrangePresetTwoButton;

        private Button OrangePresetTwoButton
        {
            get
            {
                if (m_OrangePresetTwoButton is not null)
                    return m_OrangePresetTwoButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_02_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{ORANGE_PRESET_02_BUTTON_NAME}] is not set");
                    return null;
                }

                m_OrangePresetTwoButton = button;
                return m_OrangePresetTwoButton;
            }
        }

        private Button m_OrangePresetThreeButton;

        private Button OrangePresetThreeButton
        {
            get
            {
                if (m_OrangePresetThreeButton is not null)
                    return m_OrangePresetThreeButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_03_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{ORANGE_PRESET_03_BUTTON_NAME}] is not set");
                    return null;
                }

                m_OrangePresetThreeButton = button;
                return m_OrangePresetThreeButton;
            }
        }

        private Button m_OrangePresetFourButton;

        private Button OrangePresetFourButton
        {
            get
            {
                if (m_OrangePresetFourButton is not null)
                    return m_OrangePresetFourButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_04_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{ORANGE_PRESET_04_BUTTON_NAME}] is not set");
                    return null;
                }

                m_OrangePresetFourButton = button;
                return m_OrangePresetFourButton;
            }
        }

        private Button m_OrangePresetFiveButton;

        private Button OrangePresetFiveButton
        {
            get
            {
                if (m_OrangePresetFiveButton is not null)
                    return m_OrangePresetFiveButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_05_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{ORANGE_PRESET_05_BUTTON_NAME}] is not set");
                    return null;
                }

                m_OrangePresetFiveButton = button;
                return m_OrangePresetFiveButton;
            }
        }

        private Button m_OrangeSetButton;

        private Button OrangeSetButton
        {
            get
            {
                if (m_OrangeSetButton is not null)
                    return m_OrangeSetButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(ORANGE_SET_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{ORANGE_SET_BUTTON_NAME}] is not set");
                    return null;
                }

                m_OrangeSetButton = button;
                return m_OrangeSetButton;
            }
        }

        private Label m_OrangeBlobCountLabel;

        private Label OrangeBlobCountLabel
        {
            get
            {
                if (m_OrangeBlobCountLabel is not null)
                    return m_OrangeBlobCountLabel;

                if (m_UiDocument.rootVisualElement.Q<Label>(ORANGE_BLOB_COUNT_LABEL_NAME) is not { } label)
                {
                    Debug.LogError($"Label [{ORANGE_BLOB_COUNT_LABEL_NAME}] is not set");
                    return null;
                }

                m_OrangeBlobCountLabel = label;
                return m_OrangeBlobCountLabel;
            }
        }

        private Button m_GreenPresetOneButton;

        private Button GreenPresetOneButton
        {
            get
            {
                if (m_GreenPresetOneButton is not null)
                    return m_GreenPresetOneButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(GREEN_PRESET_01_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{GREEN_PRESET_01_BUTTON_NAME}] is not set");
                    return null;
                }

                m_GreenPresetOneButton = button;
                return m_GreenPresetOneButton;
            }
        }

        private Button m_GreenPresetTwoButton;

        private Button GreenPresetTwoButton
        {
            get
            {
                if (m_GreenPresetTwoButton is not null)
                    return m_GreenPresetTwoButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(GREEN_PRESET_02_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{GREEN_PRESET_02_BUTTON_NAME}] is not set");
                    return null;
                }

                m_GreenPresetTwoButton = button;
                return m_GreenPresetTwoButton;
            }
        }

        private Button m_GreenPresetThreeButton;

        private Button GreenPresetThreeButton
        {
            get
            {
                if (m_GreenPresetThreeButton is not null)
                    return m_GreenPresetThreeButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(GREEN_PRESET_03_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{GREEN_PRESET_03_BUTTON_NAME}] is not set");
                    return null;
                }

                m_GreenPresetThreeButton = button;
                return m_GreenPresetThreeButton;
            }
        }

        private Button m_GreenPresetFourButton;

        private Button GreenPresetFourButton
        {
            get
            {
                if (m_GreenPresetFourButton is not null)
                    return m_GreenPresetFourButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(GREEN_PRESET_04_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{GREEN_PRESET_04_BUTTON_NAME}] is not set");
                    return null;
                }

                m_GreenPresetFourButton = button;
                return m_GreenPresetFourButton;
            }
        }

        private Button m_GreenPresetFiveButton;

        private Button GreenPresetFiveButton
        {
            get
            {
                if (m_GreenPresetFiveButton is not null)
                    return m_GreenPresetFiveButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(GREEN_PRESET_05_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{GREEN_PRESET_05_BUTTON_NAME}] is not set");
                    return null;
                }

                m_GreenPresetFiveButton = button;
                return m_GreenPresetFiveButton;
            }
        }

        private Button m_GreenSetButton;

        private Button GreenSetButton
        {
            get
            {
                if (m_GreenSetButton is not null)
                    return m_GreenSetButton;

                if (m_UiDocument.rootVisualElement.Q<Button>(GREEN_SET_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{GREEN_SET_BUTTON_NAME}] is not set");
                    return null;
                }

                m_GreenSetButton = button;
                return m_GreenSetButton;
            }
        }

        private Label m_GreenBlobCountLabel;

        private Label GreenBlobCountLabel
        {
            get
            {
                if (m_GreenBlobCountLabel is not null)
                    return m_GreenBlobCountLabel;

                if (m_UiDocument.rootVisualElement.Q<Label>(GREEN_BLOB_COUNT_LABEL_NAME) is not { } label)
                {
                    Debug.LogError($"Label [{GREEN_BLOB_COUNT_LABEL_NAME}] is not set");
                    return null;
                }

                m_GreenBlobCountLabel = label;
                return m_GreenBlobCountLabel;
            }
        }

        #endregion

        #region Coroutines

        private Coroutine m_PendingRefresh;

        #endregion

        private void Awake()
        {
            float pixelPerUnit = m_PanelSettings.PixelsPerUnitReflection();

            // Scale the Settings UI TV
            TelevisionController.BodyGameobject.transform.localScale = Vector3.one * TelevisionController.Scale;

            // Scale the Colliders of the Setting UI TV Screen Manipulators
            foreach (var boxCollider in TelevisionController.Colliders)
            {
                boxCollider.size *= TelevisionController.Scale;
                boxCollider.center *= TelevisionController.Scale;
            }

            // Turn off the MeshRenderer of the Setting UI TV Screen
            Renderer screenRenderer = TelevisionController.ScreenGameobject.GetComponent<Renderer>();
            screenRenderer.enabled = false;

            // Add a UIDocument to the Setting UI TV
            if (TelevisionController.ScreenGameobject.GetComponent<UIDocument>() is { } uiDocument)
            {
                m_UiDocument = uiDocument;
            }

            m_UiDocument = TelevisionController.ScreenGameobject.AddComponent<UIDocument>();
            m_UiDocument.panelSettings = PanelSettings;
            m_UiDocument.visualTreeAsset = VisualTreeAsset;
            m_UiDocument.rootVisualElement.styleSheets.Add(StyleSheet);
            m_UiDocument.worldSpaceSize = new Vector2(pixelPerUnit * TelevisionController.Scale,
                pixelPerUnit * TelevisionController.Scale);
        }

        private void OnEnable()
        {
            if (ServerNameTextField is not null
                && CalibrationModeController?.WorldCalibrationData != null
                && CalibrationModeController.WorldCalibrationData.serverHostAddress is not null)
            {
                ServerNameTextField.value = CalibrationModeController.WorldCalibrationData.serverHostAddress;
            }

            if (!m_UiDocument.rootVisualElement.styleSheets.Contains(m_StyleSheet))
                m_UiDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);

            CaptureButton.clicked += HandleCapture;
            BackButton.clicked += HandleBack;

            MinHueSlider.RegisterValueChangedCallback(OnSliderChanged);
            MaxHueSlider.RegisterValueChangedCallback(OnSliderChanged);
            MinSaturationSlider.RegisterValueChangedCallback(OnSliderChanged);
            MaxSaturationSlider.RegisterValueChangedCallback(OnSliderChanged);
            MinValueSlider.RegisterValueChangedCallback(OnSliderChanged);
            MaxValueSlider.RegisterValueChangedCallback(OnSliderChanged);

            m_OriginalPanel.OnToggleChanged += OnDetectedToggleChanged;

            TelevisionController.Switch(true);

            m_PresetConfig = LoadPresets();

            var root = m_UiDocument.rootVisualElement;
            WirePresetBank(root, "orange", 0);
            WirePresetBank(root, "green",  1);
        }

        private void OnDisable()
        {
            CaptureButton.clicked -= HandleCapture;
            BackButton.clicked -= HandleBack;

            m_MinHueSlider.UnregisterValueChangedCallback(OnSliderChanged);
            m_MaxHueSlider.UnregisterValueChangedCallback(OnSliderChanged);
            m_MinSaturationSlider.UnregisterValueChangedCallback(OnSliderChanged);
            m_MaxSaturationSlider.UnregisterValueChangedCallback(OnSliderChanged);
            m_MinValueSlider.UnregisterValueChangedCallback(OnSliderChanged);
            m_MaxValueSlider.UnregisterValueChangedCallback(OnSliderChanged);

            m_OriginalPanel.OnToggleChanged -= OnDetectedToggleChanged;

            m_ServerNameTextField = null;
            m_BackButton = null;
            m_CaptureButton = null;
            m_CaptureStatusLabel = null;
            m_MinHueSlider = null;
            m_MaxHueSlider = null;
            m_MinSaturationSlider = null;
            m_MaxSaturationSlider = null;
            m_MinValueSlider = null;
            m_MaxValueSlider = null;
            m_OrangePresetOneButton = null;
            m_OrangePresetTwoButton = null;
            m_OrangePresetThreeButton = null;
            m_OrangePresetFourButton = null;
            m_OrangePresetFiveButton = null;
            m_OrangeSetButton = null;
            m_OrangeBlobCountLabel = null;
            m_GreenPresetOneButton = null;
            m_GreenPresetTwoButton = null;
            m_GreenPresetThreeButton = null;
            m_GreenPresetFourButton = null;
            m_GreenPresetFiveButton = null;
            m_GreenSetButton = null;
            m_GreenBlobCountLabel = null;

            TelevisionController.Switch(false);

            m_HoveredPreset  = null;
            m_HoveredOverlay = null;
            m_HoveredBtn     = null;
            m_AHoldTime      = 0;
            m_SaveCompleteCooldown = 0;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private bool m_HasCaptured;
        private bool m_UseDetected;
        private string m_ActiveBank; // "orange" or "green" — last bank targeted by Set

        private string Host()
        {
            if (ServerNameTextField is null
                || CalibrationModeController is null
                || CalibrationModeController.WorldCalibrationData is null
                || CalibrationModeController.WorldCalibrationData.serverHostAddress is null
                || string.IsNullOrWhiteSpace(CalibrationModeController.WorldCalibrationData.serverHostAddress))
            {
                Debug.LogWarning("Default Server Host Address is not set");
            }

            return string.IsNullOrWhiteSpace(ServerNameTextField?.value)
                ? CalibrationModeController.WorldCalibrationData.serverHostAddress
                : ServerNameTextField.value.Trim();
        }

        private string HsvQuery() =>
            $"h_min={MinHueSlider.value}&h_max={MaxHueSlider.value}" +
            $"&s_min={MinSaturationSlider.value}&s_max={MaxSaturationSlider.value}" +
            $"&v_min={MinValueSlider.value}&v_max={MaxValueSlider.value}";

        private void SetStatus(string msg)
        {
            Debug.Log($"[HsvFilter] {msg}");
            if (CaptureStatusLabel != null) CaptureStatusLabel.text = msg;
        }

        private void HandleBack()
        {
            CalibrationModeController.ActivateMasterCalibrationMenu();
        }

        // ── Capture ─────────────────────────────────────────────────────────────

        private void HandleCapture()
        {
            StopAllCoroutines();
            m_PendingRefresh = null;
            StartCoroutine(CaptureAndFetch());
        }

        private IEnumerator CaptureAndFetch()
        {
            CaptureButton.SetEnabled(false);
            SetStatus("Capturing...");

            string host = Host();

            using var captureReq = new UnityWebRequest($"http://{host}/still/capture", "POST");
            captureReq.downloadHandler = new DownloadHandlerBuffer();
            yield return captureReq.SendWebRequest();

            if (captureReq.result != UnityWebRequest.Result.Success)
            {
                SetStatus($"Capture failed: {captureReq.error}");
                CaptureButton.SetEnabled(true);
                yield break;
            }

            m_HasCaptured = true;
            SetStatus("Fetching images...");

            string originalEndpoint = m_UseDetected ? "detected" : "original";
            yield return StartCoroutine(FetchImage($"http://{host}/still/{originalEndpoint}", m_OriginalPanel));
            yield return StartCoroutine(FetchMaskAndOverlay());

            SetStatus("Ready — adjust sliders to refine");
            CaptureButton.SetEnabled(true);
        }

        // ── Image fetching ───────────────────────────────────────────────────────

        private IEnumerator FetchImage(string url, StillImagePanelController panel)
        {
            if (panel == null)
            {
                Debug.LogError($"[{GetType().Name}] Could not find {panel.LabelElement.text}");
                yield break;
            }

            ;

            using var req = UnityWebRequestTexture.GetTexture(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[{GetType().Name}] FetchImage failed for {url}: {req.error}");
                yield break;
            }

            panel.SetTexture(DownloadHandlerTexture.GetContent(req));
        }

        private IEnumerator FetchMaskAndOverlay()
        {
            string host = Host();
            string query = HsvQuery();

            SetStatus("Updating...");

            yield return StartCoroutine(FetchImage($"http://{host}/still/mask?{query}", m_MaskPanel));
            yield return StartCoroutine(FetchOverlayWithCount($"http://{host}/still/overlay?{query}", m_OverlayPanel));

            SetStatus("Ready — adjust sliders to refine");
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
            if (m_ActiveBank != null && int.TryParse(countHeader, out int count))
                UpdateBlobCount(m_ActiveBank, count);
        }

        private void UpdateBlobCount(string bank, int count)
        {
            if (bank == "orange")
            {
                bool valid = count == 4;
                if (OrangeBlobCountLabel != null)
                {
                    OrangeBlobCountLabel.text = valid ? "4 blobs ✓" : $"{count} blobs ✗ — must be 4";
                    OrangeBlobCountLabel.style.color = valid
                        ? new StyleColor(new Color(0.2f, 0.8f, 0.2f))
                        : new StyleColor(new Color(1f, 0.3f, 0.3f));
                }

                if (CalibrationModeController.WorldCalibrationData != null)
                    CalibrationModeController.WorldCalibrationData.orangeValid = valid;
            }
            else if (bank == "green")
            {
                bool valid = count == 1;
                if (GreenBlobCountLabel != null)
                {
                    GreenBlobCountLabel.text = valid ? "1 blob ✓" : $"{count} blobs ✗ — must be 1";
                    GreenBlobCountLabel.style.color = valid
                        ? new StyleColor(new Color(0.2f, 0.8f, 0.2f))
                        : new StyleColor(new Color(1f, 0.3f, 0.3f));
                }

                if (CalibrationModeController.WorldCalibrationData != null)
                    CalibrationModeController.WorldCalibrationData.greenValid = valid;
            }
        }

        // ── Slider debounce ──────────────────────────────────────────────────────

        private void OnSliderChanged(ChangeEvent<int> _)
        {
            if (!m_HasCaptured) return;

            if (m_PendingRefresh != null) StopCoroutine(m_PendingRefresh);
            m_PendingRefresh = StartCoroutine(DebouncedRefresh());
        }

        private IEnumerator DebouncedRefresh()
        {
            yield return new WaitForSeconds(0.25f);
            yield return StartCoroutine(FetchMaskAndOverlay());
            m_PendingRefresh = null;
        }

        // ── Detected toggle ──────────────────────────────────────────────────────

        private void OnDetectedToggleChanged(bool useDetected)
        {
            m_UseDetected = useDetected;
            if (!m_HasCaptured) return;

            StopAllCoroutines();
            m_PendingRefresh = null;
            StartCoroutine(FetchOriginal());
        }

        private IEnumerator FetchOriginal()
        {
            string endpoint = m_UseDetected ? "detected" : "original";
            yield return StartCoroutine(FetchImage($"http://{Host()}/still/{endpoint}", m_OriginalPanel));
        }

        // ── A-button hold-to-save ────────────────────────────────────────────────

        private void Update()
        {
            if (m_SaveCompleteCooldown > 0)
            {
                m_SaveCompleteCooldown -= Time.deltaTime;
                if (m_SaveCompleteCooldown <= 0)
                    m_HoveredOverlay?.Hide();
                return;
            }

            if (m_HoveredPreset == null || m_InputActionReference == null)
            {
                m_AHoldTime = 0;
                return;
            }

            if (m_InputActionReference.action.IsPressed())
            {
                if (!m_HoveredOverlay.visible) m_HoveredOverlay.Show();
                m_AHoldTime += Time.deltaTime;
                m_HoveredOverlay.Progress = Mathf.Clamp01(m_AHoldTime / SAVE_HOLD_DURATION);

                if (m_AHoldTime >= SAVE_HOLD_DURATION)
                {
                    m_HoveredPreset.hMin = MinHueSlider.value;
                    m_HoveredPreset.hMax = MaxHueSlider.value;
                    m_HoveredPreset.sMin = MinSaturationSlider.value;
                    m_HoveredPreset.sMax = MaxSaturationSlider.value;
                    m_HoveredPreset.vMin = MinValueSlider.value;
                    m_HoveredPreset.vMax = MaxValueSlider.value;

                    SavePresets();
                    m_HoveredBtn.style.backgroundColor = new StyleColor(MedianHsvColor(m_HoveredPreset));

                    m_AHoldTime            = 0;
                    m_SaveCompleteCooldown = 0.5f;
                }
            }
            else if (m_AHoldTime > 0)
            {
                m_AHoldTime = 0;
                m_HoveredOverlay.Hide();
            }
        }

        // ── Presets ──────────────────────────────────────────────────────────────

        private static PresetConfig LoadPresets()
        {
            string srcPath = Path.Combine(Application.streamingAssetsPath, PRESET_FILE_NAME);

#if UNITY_EDITOR
            // In the editor always read StreamingAssets directly so edits take effect immediately
            if (!File.Exists(srcPath))
            {
                Debug.LogError($"[{CLASS_NAME}] Preset file not found at {srcPath}");
                return null;
            }

            string json = File.ReadAllText(srcPath);
            Debug.Log($"[{CLASS_NAME}] Loaded presets from {srcPath}");
#else
        // On device: copy from StreamingAssets to persistentDataPath once, then load from there
        // (allows on-device edits via adb to persist across sessions)
        string destPath = Path.Combine(Application.persistentDataPath, PRESET_FILE_NAME);
        if (!File.Exists(destPath))
        {
            if (!File.Exists(srcPath))
            {
                Debug.LogError($"[{CLASS_NAME}] Default preset file not found at {srcPath}");
                return null;
            }
            File.Copy(srcPath, destPath);
            Debug.Log($"[{CLASS_NAME}] Copied default presets to {destPath}");
        }
        string json = File.ReadAllText(destPath);
        Debug.Log($"[{CLASS_NAME}] Loaded presets from {destPath}");
#endif

            return JsonUtility.FromJson<PresetConfig>(json);
        }

        private void WirePresetBank(VisualElement root, string bankKey, int bankIndex)
        {
            if (m_PresetConfig?.banks == null || bankIndex >= m_PresetConfig.banks.Length) return;

            PresetBank bank = m_PresetConfig.banks[bankIndex];

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
                    m_HoveredPreset = preset;
                    m_HoveredOverlay = overlay;
                    m_HoveredBtn = btn;
                });

                btn.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (m_HoveredPreset != preset) return;
                    m_HoveredPreset = null;
                    m_HoveredOverlay = null;
                    m_HoveredBtn = null;
                    overlay.Hide();
                });

                btn.RegisterCallback<PointerUpEvent>(_ => ApplyPreset(preset));
            }
        }

        private void SavePresets()
        {
            string json = JsonUtility.ToJson(m_PresetConfig, true);
#if UNITY_EDITOR
            File.WriteAllText(Path.Combine(Application.streamingAssetsPath, PRESET_FILE_NAME), json);
#else
        File.WriteAllText(Path.Combine(Application.persistentDataPath, PRESET_FILE_NAME), json);
#endif
            Debug.Log("[HsvFilter] Presets saved");
        }

        private void ApplyPreset(HsvPreset p)
        {
            MinHueSlider.value = p.hMin;
            MaxHueSlider.value = p.hMax;
            MinSaturationSlider.value = p.sMin;
            MaxSaturationSlider.value = p.sMax;
            MinValueSlider.value = p.vMin;
            MaxValueSlider.value = p.vMax;
        }

        private static Color MedianHsvColor(HsvPreset p) =>
            Color.HSVToRGB(
                ((p.hMin + p.hMax) / 2f) / 179f,
                ((p.sMin + p.sMax) / 2f) / 255f,
                ((p.vMin + p.vMax) / 2f) / 255f);
    }
}
