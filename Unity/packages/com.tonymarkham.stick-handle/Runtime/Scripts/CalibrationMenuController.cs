using System;
using StickHandle.Prefabs.TV;
using StickHandle.Scripts.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    [RequiredUxmlElement(typeof(Button), VIDEO_BUTTON_NAME)]
    [RequiredUxmlElement(typeof(Button), HSV_BUTTON_NAME)]
    [RequiredUxmlElement(typeof(Button), WORLD_BUTTON_NAME)]
    public class CalibrationMenuController : MonoBehaviour
    {
        private const string CLASS_NAME = nameof(CalibrationMenuController);
        private const string VIDEO_BUTTON_NAME = "video-btn";
        private const string HSV_BUTTON_NAME = "hsv-btn";
        private const string WORLD_BUTTON_NAME = "world-btn";
        
        [Header("Calibration")]
        [RequiredRef, SerializeField] private CalibrationModeController m_CalibrationModeController;
        private CalibrationModeController CalibrationModeController => m_CalibrationModeController;

        [RequiredRef, SerializeField] private TelevisionController m_TelevisionController;
        private TelevisionController TelevisionController => m_TelevisionController;

        [RequiredRef, SerializeField] private PanelSettings m_PanelSettings;
        private PanelSettings PanelSettings => m_PanelSettings;

        [RequiredRef, SerializeField] private VisualTreeAsset m_VisualTreeAsset;
        private VisualTreeAsset VisualTreeAsset => m_VisualTreeAsset;

        [RequiredRef, SerializeField] private StyleSheet m_StyleSheet;
        private StyleSheet StyleSheet => m_StyleSheet;
        
        private UIDocument m_UiDocument;
        private float m_PixelsPerUnit;

        #region UI Elements

        private Button m_VideoButton;
        private Button VideoButton => m_VideoButton;

        private Button m_HsvButton;
        private Button HsvButton => m_HsvButton;

        private Button m_WorldButton;
        private Button WorldButton => m_WorldButton;

        #endregion

        private void ResolveElements()
        {
            var root = m_UiDocument.rootVisualElement;

            m_VideoButton = root.Q<Button>(VIDEO_BUTTON_NAME);
            if (m_VideoButton is null) throw new InvalidOperationException($"[{CLASS_NAME}] Button [{VIDEO_BUTTON_NAME}] not found");

            m_HsvButton = root.Q<Button>(HSV_BUTTON_NAME);
            if (m_HsvButton is null) throw new InvalidOperationException($"[{CLASS_NAME}] Button [{HSV_BUTTON_NAME}] not found");

            m_WorldButton = root.Q<Button>(WORLD_BUTTON_NAME);
            if (m_WorldButton is null) throw new InvalidOperationException($"[{CLASS_NAME}] Button [{WORLD_BUTTON_NAME}] not found");
        }

        private void Awake()
        {
            if (!m_CalibrationModeController) throw new MissingReferenceException($"[{CLASS_NAME}] CalibrationModeController is not set");
            if (!m_TelevisionController)      throw new MissingReferenceException($"[{CLASS_NAME}] TelevisionController is not set");
            if (!m_PanelSettings)             throw new MissingReferenceException($"[{CLASS_NAME}] PanelSettings is not set");
            if (!m_VisualTreeAsset)           throw new MissingReferenceException($"[{CLASS_NAME}] VisualTreeAsset is not set");
            if (!m_StyleSheet)                throw new MissingReferenceException($"[{CLASS_NAME}] m_StyleSheet is not set");

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

            VideoButton.clicked += HandleVideoButtonClicked;
            HsvButton.clicked   += HandleHsvButtonClicked;
            WorldButton.clicked += HandleWorldButtonClicked;

            TelevisionController.Switch(true);
        }

        private void OnDisable()
        {
            m_VideoButton.clicked -= HandleVideoButtonClicked;
            m_HsvButton.clicked   -= HandleHsvButtonClicked;
            m_WorldButton.clicked -= HandleWorldButtonClicked;

            TelevisionController.Switch(false);

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
            CalibrationModeController.ActivateHsvCalibrationMenu();
        }

        private void HandleWorldButtonClicked()
        {
            CalibrationModeController.ActivateWorldOrientationMenu();
        }
    }
}
