using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Sits on the Calibration root GO. Manages which child panel is visible
/// and wires the top-level menu buttons. VideoTools and HsvFilter GOs should
/// be inactive by default in the scene — OnEnable enforces the correct state.
/// </summary>
public class CalibrationMenuController : MonoBehaviour
{
    [SerializeField] private GameObject calibrationMenuGO;
    [SerializeField] private GameObject videoToolsGO;
    [SerializeField] private GameObject hsvFilterGO;
    
    private UIDocument doc;

    private Button _videoBtn;
    private Button _hsvBtn;

    private void Awake()
    {
        doc = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        // var doc = calibrationMenuGO.GetComponent<UIDocument>();
        if (doc == null)
        {
            Debug.LogError("[CalibrationMenu] No UIDocument on calibrationMenuGO");
            return;
        }

        var root = doc.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[CalibrationMenu] rootVisualElement is null — assign Panel Settings in Inspector");
            return;
        }

        _videoBtn = root.Q<Button>("video-btn");
        _hsvBtn   = root.Q<Button>("hsv-btn");

        if (_videoBtn == null) { Debug.LogError("[CalibrationMenu] 'video-btn' not found in UXML"); return; }
        if (_hsvBtn   == null) { Debug.LogError("[CalibrationMenu] 'hsv-btn' not found in UXML");   return; }

        _videoBtn.clicked += ShowVideoTools;
        _hsvBtn.clicked   += ShowHsvFilter;

        ShowMenu();
    }

    private void OnDisable()
    {
        if (_videoBtn != null) _videoBtn.clicked -= ShowVideoTools;
        if (_hsvBtn   != null) _hsvBtn.clicked   -= ShowHsvFilter;
    }

    public void ShowMenu()
    {
        calibrationMenuGO.SetActive(true);
        videoToolsGO.SetActive(false);
        hsvFilterGO.SetActive(false);
    }

    public void ShowVideoTools()
    {
        calibrationMenuGO.SetActive(false);
        videoToolsGO.SetActive(true);
        hsvFilterGO.SetActive(false);
    }

    public void ShowHsvFilter()
    {
        calibrationMenuGO.SetActive(false);
        videoToolsGO.SetActive(false);
        hsvFilterGO.SetActive(true);
    }
}
