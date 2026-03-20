using System;
using StickHandle.Prefabs.TV;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public class CalibrationMenuController : MonoBehaviour
    {
        private const string CLASS_NAME = nameof(CalibrationMenuController);
        private const string VIDEO_BUTTON_NAME = "video-btn";
        private const string HSL_BUTTON_NAME = "hsv-btn";
        private const string WORLD_BUTTON_NAME = "world-btn";
        
        [Header("Calibration")]
        [SerializeField]private CalibrationModeController m_CalibrationModeController;
        private CalibrationModeController CalibrationModeController
        {
            get
            {
                if(m_CalibrationModeController)
                    return m_CalibrationModeController;
                
                Debug.LogError($"[{CLASS_NAME}] CalibrationModeController is not set");
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
                
                Debug.LogError($"[{CLASS_NAME}] TelevisionController is not set");
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
                
                Debug.LogError($"[{CLASS_NAME}] PanelSettings is not set");
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
                
                Debug.LogError($"[{CLASS_NAME}] VisualTreeAsset is not set");
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
                
                Debug.LogError($"[{CLASS_NAME}] m_StyleSheet is not set");
                return m_StyleSheet;
            }
        }
        
        private UIDocument m_UiDocument;
        private UIDocument UiDocument
        {
            get
            {
                if(m_UiDocument)
                    return m_UiDocument;
                
                if (TelevisionController.ScreenGameobject.GetComponent<UIDocument>() is { } uiDocument)
                {
                    m_UiDocument = uiDocument;
                    return m_UiDocument;
                }
                
                m_UiDocument = TelevisionController.ScreenGameobject.AddComponent<UIDocument>();
                return m_UiDocument;
            }
        }
        
        private Button m_VideoButton;
        private Button VideoButton => m_VideoButton;

        private Button m_HsvButton;
        private Button HsvButton => m_HsvButton;

        private Button m_WorldButton;
        private Button WorldButton => m_WorldButton;

        private bool TryResolveElements()
        {
            var root = UiDocument.rootVisualElement;
            bool ok = true;

            m_VideoButton = root.Q<Button>(VIDEO_BUTTON_NAME);
            if (m_VideoButton is null) { Debug.LogError($"[{CLASS_NAME}] Button [{VIDEO_BUTTON_NAME}] not found"); ok = false; }

            m_HsvButton = root.Q<Button>(HSL_BUTTON_NAME);
            if (m_HsvButton is null) { Debug.LogError($"[{CLASS_NAME}] Button [{HSL_BUTTON_NAME}] not found"); ok = false; }

            m_WorldButton = root.Q<Button>(WORLD_BUTTON_NAME);
            if (m_WorldButton is null) { Debug.LogError($"[{CLASS_NAME}] Button [{WORLD_BUTTON_NAME}] not found"); ok = false; }

            return ok;
        }

        private void OnEnable()
        {
            float pixelPerUnit = PanelSettings.PixelsPerUnitReflection();

            Renderer screenRenderer = TelevisionController.ScreenGameobject.GetComponent<Renderer>();
            screenRenderer.enabled = false;

            UiDocument.panelSettings = PanelSettings;
            UiDocument.visualTreeAsset = VisualTreeAsset;
            UiDocument.rootVisualElement.styleSheets.Add(StyleSheet);
            UiDocument.worldSpaceSize = new Vector2(pixelPerUnit, pixelPerUnit);

            if (!TryResolveElements()) return;

            VideoButton.clicked += HandleVideoButtonClicked;
            HsvButton.clicked   += HandleHsvButtonClicked;
            WorldButton.clicked += HandleWorldButtonClicked;

            TelevisionController.Switch(true);
        }

        private void OnDisable()
        {
            if (m_VideoButton != null) m_VideoButton.clicked -= HandleVideoButtonClicked;
            if (m_HsvButton != null)   m_HsvButton.clicked   -= HandleHsvButtonClicked;
            if (m_WorldButton != null) m_WorldButton.clicked  -= HandleWorldButtonClicked;

            TelevisionController.Switch(false);

            m_UiDocument  = null;
            m_VideoButton = null;
            m_HsvButton   = null;
            m_WorldButton = null;
        }

        private void HandleVideoButtonClicked()
        {
            Debug.LogWarning($"[{CLASS_NAME}] Video button clicked");
        }

        private void HandleHsvButtonClicked()
        {
            CalibrationModeController.ActivateHslCalibrationMenu();
        }

        private void HandleWorldButtonClicked()
        {
            Debug.LogWarning($"[{CLASS_NAME}] World button clicked");
        }
    }
}
