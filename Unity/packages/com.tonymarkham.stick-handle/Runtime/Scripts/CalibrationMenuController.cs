using System;
using StickHandle.Prefabs.TV;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public class CalibrationMenuController : MonoBehaviour
    {
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
        private Button VideoButton
        {
            get
            {
               if(m_VideoButton is not null)
                   return m_VideoButton;

               if (UiDocument.rootVisualElement.Q<Button>(VIDEO_BUTTON_NAME) is not { } button)
               {
                   Debug.LogError($"Button [{VIDEO_BUTTON_NAME}] is not set");
                   return null;
               }
               
               m_VideoButton = button;
               return m_VideoButton;
            }
        }
        
        private Button m_HsvButton;
        private Button HsvButton
        {
            get
            {
                if(m_HsvButton is not null)
                    return m_HsvButton;

                if (UiDocument.rootVisualElement.Q<Button>(HSL_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{HSL_BUTTON_NAME}] is not set");
                    return null;
                }
               
                m_HsvButton = button;
                return m_HsvButton;
            }
        }
        
        private Button m_WorldButton;
        private Button WorldButton
        {
            get
            {
                if(m_WorldButton is not null)
                    return m_WorldButton;

                if (UiDocument.rootVisualElement.Q<Button>(WORLD_BUTTON_NAME) is not { } button)
                {
                    Debug.LogError($"Button [{WORLD_BUTTON_NAME}] is not set");
                    return null;
                }
               
                m_WorldButton = button;
                return m_WorldButton;
            }
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
            
            VideoButton.clicked += HandleVideoButtonClicked;
            HsvButton.clicked += HandleHsvButtonClicked;
            WorldButton.clicked += HandleWorldButtonClicked;
            
            TelevisionController.Switch(true);
        }

        private void OnDisable()
        {
            VideoButton.clicked -= HandleVideoButtonClicked;
            HsvButton.clicked -= HandleHsvButtonClicked;
            WorldButton.clicked -= HandleWorldButtonClicked;
            
            TelevisionController.Switch(false);
            
            m_UiDocument = null;
            m_VideoButton = null;
            m_HsvButton = null;
            m_WorldButton = null;
        }

        private void HandleVideoButtonClicked()
        {
            Debug.LogWarning("Video button clicked");
        }

        private void HandleHsvButtonClicked()
        {
            CalibrationModeController.ActivateHslCalibrationMenu();
        }

        private void HandleWorldButtonClicked()
        {
            Debug.LogWarning("World button clicked");
        }
    }
}
