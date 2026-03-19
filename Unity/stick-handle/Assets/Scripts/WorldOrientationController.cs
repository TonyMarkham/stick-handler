using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Manages the World Orientation calibration panel.
/// Drives 4 floor-constrained cylinders, POSTs their positions to /calibration/recalc on each
/// release, and saves the resulting homography matrix to WorldCalibrationData on Save.
/// </summary>
public class WorldOrientationController : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private WorldCalibrationData _calibrationData;
    [SerializeField] private CalibrationMenuController _menuController;
    [SerializeField] private string _serverUrl = "test-pi";
    [SerializeField] private float _minSideLength = 0.3f;

    [Header("Cylinders")]
    [SerializeField] private FloorConstrainedGrabInteractable _cylinderA;
    [SerializeField] private FloorConstrainedGrabInteractable _cylinderB;
    [SerializeField] private FloorConstrainedGrabInteractable _cylinderC;
    [SerializeField] private FloorConstrainedGrabInteractable _cylinderD;

    private Button _backBtn;
    private Button _saveBtn;
    private Label  _errorLabel;

    private string BaseUrl => $"http://{_serverUrl}:8080";

    // ── Serializable types for HTTP JSON ────────────────────────────────────

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

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        var root = _uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[WorldOrientation] rootVisualElement is null");
            return;
        }

        _backBtn    = root.Q<Button>("back-btn");
        _saveBtn    = root.Q<Button>("save-btn");
        _errorLabel = root.Q<Label>("error-label");

        if (_backBtn == null) { Debug.LogError("[WorldOrientation] 'back-btn' not found");  return; }
        if (_saveBtn == null) { Debug.LogError("[WorldOrientation] 'save-btn' not found");  return; }

        _backBtn.clicked += HandleBack;
        _saveBtn.clicked += SavePositions;

        // Restore saved cylinder positions before showing
        _calibrationData.LoadFromDisk();
        if (_calibrationData.isCalibrated)
        {
            _cylinderA.transform.position = _calibrationData.cylinderA;
            _cylinderB.transform.position = _calibrationData.cylinderB;
            _cylinderC.transform.position = _calibrationData.cylinderC;
            _cylinderD.transform.position = _calibrationData.cylinderD;
        }

        _cylinderA.selectExited.AddListener(OnCylinderReleased);
        _cylinderB.selectExited.AddListener(OnCylinderReleased);
        _cylinderC.selectExited.AddListener(OnCylinderReleased);
        _cylinderD.selectExited.AddListener(OnCylinderReleased);

        StartCoroutine(PostCalibrationStart());
    }

    private void OnDisable()
    {
        if (_backBtn != null) _backBtn.clicked -= HandleBack;
        if (_saveBtn != null) _saveBtn.clicked -= SavePositions;

        if (_cylinderA != null) _cylinderA.selectExited.RemoveListener(OnCylinderReleased);
        if (_cylinderB != null) _cylinderB.selectExited.RemoveListener(OnCylinderReleased);
        if (_cylinderC != null) _cylinderC.selectExited.RemoveListener(OnCylinderReleased);
        if (_cylinderD != null) _cylinderD.selectExited.RemoveListener(OnCylinderReleased);

        // StartCoroutine is unavailable on a disabled component; delegate to the menu controller
        // which remains active throughout the calibration session.
        if (_menuController != null)
            _menuController.StartCoroutine(PostCalibrationEnd());
    }

    // ── Per-frame validation ─────────────────────────────────────────────────

    private void Update()
    {
        if (_saveBtn == null) return;

        bool valid = IsQuadrilateralValid();
        _saveBtn.SetEnabled(valid);

        if (_errorLabel != null)
        {
            _errorLabel.text = valid ? "" : "Spread cylinders further apart";
            _errorLabel.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private bool IsQuadrilateralValid()
    {
        Vector3 a = _cylinderA.transform.position;
        Vector3 b = _cylinderB.transform.position;
        Vector3 c = _cylinderC.transform.position;
        Vector3 d = _cylinderD.transform.position;

        return Vector3.Distance(a, b) >= _minSideLength &&
               Vector3.Distance(a, c) >= _minSideLength &&
               Vector3.Distance(a, d) >= _minSideLength &&
               Vector3.Distance(b, c) >= _minSideLength &&
               Vector3.Distance(b, d) >= _minSideLength &&
               Vector3.Distance(c, d) >= _minSideLength;
    }

    // ── Cylinder release ─────────────────────────────────────────────────────

    private void OnCylinderReleased(SelectExitEventArgs args)
    {
        if (!IsQuadrilateralValid()) return;
        StartCoroutine(Recalc());
    }

    private IEnumerator Recalc()
    {
        var body = new RecalcRequest
        {
            cylinders = new[]
            {
                new CylinderPoint { label = 1, x = _cylinderA.transform.position.x, z = _cylinderA.transform.position.z },
                new CylinderPoint { label = 2, x = _cylinderB.transform.position.x, z = _cylinderB.transform.position.z },
                new CylinderPoint { label = 3, x = _cylinderC.transform.position.x, z = _cylinderC.transform.position.z },
                new CylinderPoint { label = 4, x = _cylinderD.transform.position.x, z = _cylinderD.transform.position.z },
            }
        };

        byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

        using var req = new UnityWebRequest($"{BaseUrl}/calibration/recalc", "POST");
        req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[WorldOrientation] Recalc failed: {req.error}");
            yield break;
        }

        var response = JsonUtility.FromJson<RecalcResponse>(req.downloadHandler.text);
        if (response?.matrix != null && response.matrix.Length == 9)
        {
            _calibrationData.transformMatrix = response.matrix;
            Debug.Log("[WorldOrientation] Homography matrix updated");
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private void SavePositions()
    {
        _calibrationData.cylinderA = _cylinderA.transform.position;
        _calibrationData.cylinderB = _cylinderB.transform.position;
        _calibrationData.cylinderC = _cylinderC.transform.position;
        _calibrationData.cylinderD = _cylinderD.transform.position;
        _calibrationData.isCalibrated = true;
        _calibrationData.SaveToDisk();
        Debug.Log("[WorldOrientation] Calibration saved");
    }

    // ── Back ─────────────────────────────────────────────────────────────────

    private void HandleBack()
    {
        _menuController.ShowMenu();
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────────

    private IEnumerator PostCalibrationStart()
    {
        using var req = new UnityWebRequest($"{BaseUrl}/calibration/start", "POST");
        req.downloadHandler = new DownloadHandlerBuffer();
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[WorldOrientation] /calibration/start failed: {req.error}");
        else
            Debug.Log("[WorldOrientation] Pi entered calibration tracking mode");
    }

    private IEnumerator PostCalibrationEnd()
    {
        using var req = new UnityWebRequest($"{BaseUrl}/calibration/end", "POST");
        req.downloadHandler = new DownloadHandlerBuffer();
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[WorldOrientation] /calibration/end failed: {req.error}");
        else
            Debug.Log("[WorldOrientation] Pi returned to setup mode");
    }
}
