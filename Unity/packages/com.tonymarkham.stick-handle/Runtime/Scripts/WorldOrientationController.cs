using System;
using System.Collections;
using StickHandle.Prefabs.TV;
using StickHandle.Scripts.Attributes;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace StickHandle.Scripts
{
    [RequiredUxmlElement(typeof(Button), SAVE_BUTTON_NAME)]
    [RequiredUxmlElement(typeof(Button), BACK_BUTTON_NAME)]
    [RequiredUxmlElement(typeof(Label),  ERROR_LABEL_NAME)]
    public class WorldOrientationController : MonoBehaviour
    {
        private const string CLASS_NAME       = nameof(WorldOrientationController);
        private const string SAVE_BUTTON_NAME = "save-btn";
        private const string BACK_BUTTON_NAME = "back-btn";
        private const string ERROR_LABEL_NAME = "error-label";
        private const string ENDPOINT_START   = "/calibration/start";
        private const string ENDPOINT_END     = "/calibration/end";
        private const string ENDPOINT_RECALC  = "/calibration/recalc";

        [Header("Controllers")]
        [RequiredRef, SerializeField] private CalibrationModeController m_CalibrationModeController;
        private CalibrationModeController CalibrationModeController => m_CalibrationModeController;

        [RequiredRef, SerializeField] private WorldCalibrationData m_WorldCalibrationData;
        private WorldCalibrationData WorldCalibrationData => m_WorldCalibrationData;

        [RequiredRef, SerializeField] private TelevisionController m_TelevisionController;
        private TelevisionController TelevisionController => m_TelevisionController;

        [RequiredRef, SerializeField] private PanelSettings m_PanelSettings;
        private PanelSettings PanelSettings => m_PanelSettings;

        [RequiredRef, SerializeField] private VisualTreeAsset m_VisualTreeAsset;
        private VisualTreeAsset VisualTreeAsset => m_VisualTreeAsset;

        [RequiredRef, SerializeField] private StyleSheet m_StyleSheet;
        private StyleSheet StyleSheet => m_StyleSheet;

        [Header("Cylinders")]
        [RequiredRef, SerializeField] private XRGrabInteractable m_CylinderA;
        [RequiredRef, SerializeField] private XRGrabInteractable m_CylinderB;
        [RequiredRef, SerializeField] private XRGrabInteractable m_CylinderC;
        [RequiredRef, SerializeField] private XRGrabInteractable m_CylinderD;

        [Header("Geometry")]
        [SerializeField] private float m_MinSideLength = 0.3f;

        private UIDocument m_UiDocument;
        private float      m_PixelsPerUnit;
        private Coroutine  m_RecalcCoroutine;

        #region UI Elements

        private Button m_SaveButton;
        private Button SaveButton => m_SaveButton;

        private Button m_BackButton;
        private Button BackButton => m_BackButton;

        private Label m_ErrorLabel;
        private Label ErrorLabel => m_ErrorLabel;

        #endregion

        private string BaseUrl => $"http://{WorldCalibrationData.serverHostAddress}";

        // ── Serializable types for HTTP JSON ────────────────────────────────

        [Serializable]
        private class CylinderPoint
        {
            public int   label;
            public float x;
            public float z;
        }

        [Serializable]
        private class RecalcRequest
        {
            public CylinderPoint[] cylinders;
        }

        [Serializable]
        private class RecalcResponse
        {
            public float[] matrix;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (!m_CalibrationModeController) throw new MissingReferenceException($"[{CLASS_NAME}] CalibrationModeController is not set");
            if (!m_WorldCalibrationData)      throw new MissingReferenceException($"[{CLASS_NAME}] WorldCalibrationData is not set");
            if (!m_TelevisionController)      throw new MissingReferenceException($"[{CLASS_NAME}] TelevisionController is not set");
            if (!m_PanelSettings)             throw new MissingReferenceException($"[{CLASS_NAME}] PanelSettings is not set");
            if (!m_VisualTreeAsset)           throw new MissingReferenceException($"[{CLASS_NAME}] VisualTreeAsset is not set");
            if (!m_StyleSheet)               throw new MissingReferenceException($"[{CLASS_NAME}] StyleSheet is not set");
            if (!m_CylinderA)                throw new MissingReferenceException($"[{CLASS_NAME}] CylinderA is not set");
            if (!m_CylinderB)                throw new MissingReferenceException($"[{CLASS_NAME}] CylinderB is not set");
            if (!m_CylinderC)                throw new MissingReferenceException($"[{CLASS_NAME}] CylinderC is not set");
            if (!m_CylinderD)                throw new MissingReferenceException($"[{CLASS_NAME}] CylinderD is not set");

            m_PixelsPerUnit = PanelSettings.PixelsPerUnitReflection();

            Renderer screenRenderer = TelevisionController.ScreenGameobject.GetComponent<Renderer>();
            screenRenderer.enabled = false;

            m_UiDocument = TelevisionController.ScreenGameobject.GetComponent<UIDocument>()
                ?? TelevisionController.ScreenGameobject.AddComponent<UIDocument>();
            m_UiDocument.panelSettings   = PanelSettings;
            m_UiDocument.visualTreeAsset = VisualTreeAsset;
            m_UiDocument.rootVisualElement.styleSheets.Add(StyleSheet);
            m_UiDocument.worldSpaceSize  = new Vector2(m_PixelsPerUnit, m_PixelsPerUnit);
        }

        private void ResolveElements()
        {
            var root = m_UiDocument.rootVisualElement;

            m_SaveButton = root.Q<Button>(SAVE_BUTTON_NAME);
            if (m_SaveButton is null) throw new InvalidOperationException($"[{CLASS_NAME}] Button [{SAVE_BUTTON_NAME}] not found");

            m_BackButton = root.Q<Button>(BACK_BUTTON_NAME);
            if (m_BackButton is null) throw new InvalidOperationException($"[{CLASS_NAME}] Button [{BACK_BUTTON_NAME}] not found");

            m_ErrorLabel = root.Q<Label>(ERROR_LABEL_NAME);
            if (m_ErrorLabel is null) throw new InvalidOperationException($"[{CLASS_NAME}] Label [{ERROR_LABEL_NAME}] not found");
        }

        private void OnEnable()
        {
            try
            {
                ResolveElements();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Application.Quit(1);
                return;
            }

            if (!m_UiDocument.rootVisualElement.styleSheets.Contains(m_StyleSheet))
                m_UiDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);

            if (WorldCalibrationData.isCalibrated)
            {
                m_CylinderA.transform.position = WorldCalibrationData.cylinderA;
                m_CylinderB.transform.position = WorldCalibrationData.cylinderB;
                m_CylinderC.transform.position = WorldCalibrationData.cylinderC;
                m_CylinderD.transform.position = WorldCalibrationData.cylinderD;
            }

            m_CylinderA.selectExited.AddListener(HandleCylinderReleased);
            m_CylinderB.selectExited.AddListener(HandleCylinderReleased);
            m_CylinderC.selectExited.AddListener(HandleCylinderReleased);
            m_CylinderD.selectExited.AddListener(HandleCylinderReleased);

            SaveButton.clicked += HandleSave;
            BackButton.clicked += HandleBack;

            TelevisionController.Switch(true);
            StartCoroutine(PostCalibrationStart());
        }

        private void OnDisable()
        {
            m_CylinderA.selectExited.RemoveListener(HandleCylinderReleased);
            m_CylinderB.selectExited.RemoveListener(HandleCylinderReleased);
            m_CylinderC.selectExited.RemoveListener(HandleCylinderReleased);
            m_CylinderD.selectExited.RemoveListener(HandleCylinderReleased);

            m_SaveButton.clicked -= HandleSave;
            m_BackButton.clicked -= HandleBack;

            TelevisionController.Switch(false);

            m_SaveButton = null;
            m_BackButton = null;
            m_ErrorLabel = null;

            if (m_RecalcCoroutine != null)
            {
                StopCoroutine(m_RecalcCoroutine);
                m_RecalcCoroutine = null;
            }

            m_CalibrationModeController.StartCoroutine(PostCalibrationEnd());
        }

        // ── Per-frame validation ─────────────────────────────────────────────

        private void Update()
        {
            bool valid = IsQuadValid();
            SaveButton.SetEnabled(valid);
            ErrorLabel.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private bool IsQuadValid()
        {
            Vector3 a = m_CylinderA.transform.position;
            Vector3 b = m_CylinderB.transform.position;
            Vector3 c = m_CylinderC.transform.position;
            Vector3 d = m_CylinderD.transform.position;

            return Vector3.Distance(a, b) >= m_MinSideLength &&
                   Vector3.Distance(a, c) >= m_MinSideLength &&
                   Vector3.Distance(a, d) >= m_MinSideLength &&
                   Vector3.Distance(b, c) >= m_MinSideLength &&
                   Vector3.Distance(b, d) >= m_MinSideLength &&
                   Vector3.Distance(c, d) >= m_MinSideLength;
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        private void HandleSave()
        {
            WorldCalibrationData.cylinderA    = m_CylinderA.transform.position;
            WorldCalibrationData.cylinderB    = m_CylinderB.transform.position;
            WorldCalibrationData.cylinderC    = m_CylinderC.transform.position;
            WorldCalibrationData.cylinderD    = m_CylinderD.transform.position;
            WorldCalibrationData.isCalibrated = true;
            WorldCalibrationData.SaveToDisk();
            Debug.Log($"[{CLASS_NAME}] Calibration saved");
        }

        private void HandleBack()
        {
            CalibrationModeController.ActivateMasterCalibrationMenu();
        }

        private void HandleCylinderReleased(SelectExitEventArgs _)
        {
            if (!IsQuadValid()) return;
            if (m_RecalcCoroutine != null) StopCoroutine(m_RecalcCoroutine);
            m_RecalcCoroutine = StartCoroutine(PostCalibrationRecalc());
        }

        // ── Coroutines ────────────────────────────────────────────────────────

        private IEnumerator PostCalibrationStart()
        {
            using var req = new UnityWebRequest($"{BaseUrl}{ENDPOINT_START}", "POST");
            req.downloadHandler = new DownloadHandlerBuffer();
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[{CLASS_NAME}] {ENDPOINT_START} failed: {req.error}");
            else
                Debug.Log($"[{CLASS_NAME}] Pi entered calibration tracking mode");
        }

        private IEnumerator PostCalibrationEnd()
        {
            using var req = new UnityWebRequest($"{BaseUrl}{ENDPOINT_END}", "POST");
            req.downloadHandler = new DownloadHandlerBuffer();
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[{CLASS_NAME}] {ENDPOINT_END} failed: {req.error}");
            else
                Debug.Log($"[{CLASS_NAME}] Pi returned to setup mode");
        }

        private IEnumerator PostCalibrationRecalc()
        {
            var body = new RecalcRequest
            {
                cylinders = new[]
                {
                    new CylinderPoint { label = 1, x = m_CylinderA.transform.position.x, z = m_CylinderA.transform.position.z },
                    new CylinderPoint { label = 2, x = m_CylinderB.transform.position.x, z = m_CylinderB.transform.position.z },
                    new CylinderPoint { label = 3, x = m_CylinderC.transform.position.x, z = m_CylinderC.transform.position.z },
                    new CylinderPoint { label = 4, x = m_CylinderD.transform.position.x, z = m_CylinderD.transform.position.z },
                }
            };

            byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

            using var req = new UnityWebRequest($"{BaseUrl}{ENDPOINT_RECALC}", "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[{CLASS_NAME}] {ENDPOINT_RECALC} failed: {req.error}");
                yield break;
            }

            var response = JsonUtility.FromJson<RecalcResponse>(req.downloadHandler.text);
            if (response?.matrix != null && response.matrix.Length == 9)
            {
                WorldCalibrationData.transformMatrix = response.matrix;
                Debug.Log($"[{CLASS_NAME}] Homography matrix updated");
            }

            m_RecalcCoroutine = null;
        }
    }
}
