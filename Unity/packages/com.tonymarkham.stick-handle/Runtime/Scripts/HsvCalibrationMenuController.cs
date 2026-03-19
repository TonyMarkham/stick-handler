using System;
using System.Collections;
using StickHandle.Prefabs.TV;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public class HsvCalibrationMenuController : MonoBehaviour
    {
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
        
        [Header("Image Panels")]
        [SerializeField] private StillImagePanelController m_OriginalPanel;
        [SerializeField] private StillImagePanelController m_MaskPanel;
        [SerializeField] private StillImagePanelController m_OverlayPanel;
        
        [Header("Calibration")]
        [SerializeField]private CalibrationModeController m_CalibrationModeController;
        private CalibrationModeController CalibrationModeController
        {
            get
            {
                if(m_CalibrationModeController)
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
                if(m_TelevisionController)
                    return m_TelevisionController;
                
                Debug.LogError("TelevisionController is not set");
                return m_TelevisionController;
            }
        }
        
        [SerializeField] private PanelSettings m_PanelSettings;
        private PanelSettings PanelSettings
        {
            get
            {
                if(m_PanelSettings)
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
                if(m_VisualTreeAsset)
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
                if(m_StyleSheet)
                    return m_StyleSheet;
                
                Debug.LogError("m_StyleSheet is not set");
                return m_StyleSheet;
            }
        }
        
        private UIDocument m_UIDocument;
        private UIDocument UIDocument
        {
            get
            {
                if(m_UIDocument)
                {
                    Debug.LogWarning("UIDocument is already set");
                    return m_UIDocument;
                }
                
                if(!TelevisionController)
                {
                    Debug.LogError("TelevisionController is not set");
                    return m_UIDocument;
                }

                if (TelevisionController.ScreenGameobject is not { } screenGameobject)
                {
                    Debug.LogError("Could not add ScreenGameobject");
                    return m_UIDocument;
                }
                
                if(TelevisionController.BodyGameobject is not { } bodyGameobject)
                {
                    Debug.LogError("Could not add BodyGameobject");
                    return m_UIDocument;
                }

                if(screenGameobject.GetComponent<UIDocument>() is { } uiDocument)
                {
                    Debug.LogWarning("Using Existing UIDocument");
                    return uiDocument;
                }

                if (screenGameobject.AddComponent<UIDocument>() is not { } newUIDocument)
                {
                    Debug.LogError("Could not  add UIDocument");
                    return null;
                }

                if (!Utilities.TryGetPixelPerUnitFromPanelSettings(PanelSettings, out float pixelPerUnit))
                {
                    Debug.LogError("Could not extract pixel per unit from panel settings");
                    return null;
                }

                if (TelevisionController.Colliders is { } colliders)
                {
                    foreach (var collider in colliders)
                    {
                        if (collider is BoxCollider boxCollider)
                        {
                            boxCollider.size *= TelevisionController.Scale;
                            boxCollider.center *= TelevisionController.Scale;
                        }
                    }
                }
                
                bodyGameobject.transform.localScale = Vector3.one * TelevisionController.Scale;
                
                Renderer screenRenderer = screenGameobject.GetComponent<Renderer>();
                screenRenderer.enabled = false;
                newUIDocument.panelSettings = PanelSettings;
                newUIDocument.visualTreeAsset = VisualTreeAsset;
                newUIDocument.rootVisualElement.styleSheets.Add(StyleSheet);
                newUIDocument.worldSpaceSize = new Vector2(pixelPerUnit * TelevisionController.Scale, pixelPerUnit * TelevisionController.Scale);
                m_UIDocument = newUIDocument;
                
                return m_UIDocument;
            }
        }

        #region UI Elements

        private TextField m_ServerNameTextField;
        private TextField ServerNameTextField
        {
            get
            {
                if(m_ServerNameTextField is not null)
                    return m_ServerNameTextField;

                if (UIDocument.rootVisualElement.Q<TextField>(SERVER_NAME_ELEMENT_NAME) is not { } textField)
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
                if(m_BackButton is not null)
                    return m_BackButton;

                if (UIDocument.rootVisualElement.Q<Button>(BACK_BUTTON_NAME) is not { } button)
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
                if(m_CaptureButton is not null)
                    return m_CaptureButton;

                if (UIDocument.rootVisualElement.Q<Button>(CAPTURE_BUTTON_NAME) is not { } button)
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
                if(m_CaptureStatusLabel is not null)
                    return m_CaptureStatusLabel;

                if (UIDocument.rootVisualElement.Q<Label>(CAPTURE_STATUS_LABEL_NAME) is not { } label)
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
                if(m_MinHueSlider is not null)
                    return m_MinHueSlider;

                if (UIDocument.rootVisualElement.Q<SliderInt>(MIN_HUE_SLIDER_NAME) is not { } slider)
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
                if(m_MaxHueSlider is not null)
                    return m_MaxHueSlider;

                if (UIDocument.rootVisualElement.Q<SliderInt>(MAX_HUE_SLIDER_NAME) is not { } slider)
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
                if(m_MinSaturationSlider is not null)
                    return m_MinSaturationSlider;

                if (UIDocument.rootVisualElement.Q<SliderInt>(MIN_SATURATION_SLIDER_NAME) is not { } slider)
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
                if(m_MaxSaturationSlider is not null)
                    return m_MaxSaturationSlider;

                if (UIDocument.rootVisualElement.Q<SliderInt>(MAX_SATURATION_SLIDER_NAME) is not { } slider)
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
                if(m_MinValueSlider is not null)
                    return m_MinValueSlider;

                if (UIDocument.rootVisualElement.Q<SliderInt>(MIN_VALUE_SLIDER_NAME) is not { } slider)
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
                if(m_MaxValueSlider is not null)
                    return m_MaxValueSlider;

                if (UIDocument.rootVisualElement.Q<SliderInt>(MAX_VALUE_SLIDER_NAME) is not { } slider)
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
                if(m_OrangePresetOneButton is not null)
                    return m_OrangePresetOneButton;

                if (UIDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_01_BUTTON_NAME) is not { } button)
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
                if(m_OrangePresetTwoButton is not null)
                    return m_OrangePresetTwoButton;

                if (UIDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_02_BUTTON_NAME) is not { } button)
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
                if(m_OrangePresetThreeButton is not null)
                    return m_OrangePresetThreeButton;

                if (UIDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_03_BUTTON_NAME) is not { } button)
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
                if(m_OrangePresetFourButton is not null)
                    return m_OrangePresetFourButton;

                if (UIDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_04_BUTTON_NAME) is not { } button)
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
                if(m_OrangePresetFiveButton is not null)
                    return m_OrangePresetFiveButton;

                if (UIDocument.rootVisualElement.Q<Button>(ORANGE_PRESET_05_BUTTON_NAME) is not { } button)
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
                if(m_OrangeSetButton is not null)
                    return m_OrangeSetButton;

                if (UIDocument.rootVisualElement.Q<Button>(ORANGE_SET_BUTTON_NAME) is not { } button)
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
                if(m_OrangeBlobCountLabel is not null)
                    return m_OrangeBlobCountLabel;

                if (UIDocument.rootVisualElement.Q<Label>(ORANGE_BLOB_COUNT_LABEL_NAME) is not { } label)
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
                if(m_GreenPresetOneButton is not null)
                    return m_GreenPresetOneButton;

                if (UIDocument.rootVisualElement.Q<Button>(GREEN_PRESET_01_BUTTON_NAME) is not { } button)
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
                if(m_GreenPresetTwoButton is not null)
                    return m_GreenPresetTwoButton;

                if (UIDocument.rootVisualElement.Q<Button>(GREEN_PRESET_02_BUTTON_NAME) is not { } button)
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
                if(m_GreenPresetThreeButton is not null)
                    return m_GreenPresetThreeButton;

                if (UIDocument.rootVisualElement.Q<Button>(GREEN_PRESET_03_BUTTON_NAME) is not { } button)
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
                if(m_GreenPresetFourButton is not null)
                    return m_GreenPresetFourButton;

                if (UIDocument.rootVisualElement.Q<Button>(GREEN_PRESET_04_BUTTON_NAME) is not { } button)
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
                if(m_GreenPresetFiveButton is not null)
                    return m_GreenPresetFiveButton;

                if (UIDocument.rootVisualElement.Q<Button>(GREEN_PRESET_05_BUTTON_NAME) is not { } button)
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
                if(m_GreenSetButton is not null)
                    return m_GreenSetButton;

                if (UIDocument.rootVisualElement.Q<Button>(GREEN_SET_BUTTON_NAME) is not { } button)
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
                if(m_GreenBlobCountLabel is not null)
                    return m_GreenBlobCountLabel;

                if (UIDocument.rootVisualElement.Q<Label>(GREEN_BLOB_COUNT_LABEL_NAME) is not { } label)
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

        private void OnEnable()
        {
            if (ServerNameTextField is { } serverNameTextField
                && CalibrationModeController is { } calibrationModeController
                && calibrationModeController.WorldCalibrationData is { } worldCalibrationData
                && worldCalibrationData.serverHostAddress is { } serverHostAddress)
            {
                serverNameTextField.value = serverHostAddress;
            }

            if (CaptureButton is not null)
            {
                CaptureButton.clicked   += HandleCapture;
            }

            if (!UIDocument.rootVisualElement.styleSheets.Contains(StyleSheet))
            {
                UIDocument.rootVisualElement.styleSheets.Add(StyleSheet);
            }
            
            if(UIDocument)
            {
                TelevisionController.Switch(true);
            }
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
        
        private IEnumerator FetchMaskAndOverlay()
        {
            string host  = Host();
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
                if (CalibrationModeController.WorldCalibrationData != null) CalibrationModeController.WorldCalibrationData.orangeValid = valid;
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
                if (CalibrationModeController.WorldCalibrationData != null) CalibrationModeController.WorldCalibrationData.greenValid = valid;
            }
        }
    }
}
