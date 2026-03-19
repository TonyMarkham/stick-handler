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
        
        private UIDocument m_UIDocument;
        private UIDocument UIDocument
        {
            get
            {
                if(m_UIDocument)
                    return m_UIDocument;
                
                if(!TelevisionController)
                    return m_UIDocument;

                if (TelevisionController.ScreenGameobject is not { } screenGameobject)
                    return m_UIDocument;

                if(screenGameobject.GetComponent<UIDocument>() is { } uiDocument)
                    return uiDocument;

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

                Renderer screenRenderer = screenGameobject.GetComponent<Renderer>();
                screenRenderer.enabled = false;
                newUIDocument.panelSettings = PanelSettings;
                newUIDocument.visualTreeAsset = VisualTreeAsset;
                newUIDocument.rootVisualElement.styleSheets.Add(StyleSheet);
                newUIDocument.worldSpaceSize = new Vector2(pixelPerUnit, pixelPerUnit);
                m_UIDocument = newUIDocument;
                
                return m_UIDocument;
            }
        }
        
        private Button m_VideoButton;
        private Button VideoButton
        {
            get
            {
               if(m_VideoButton is not null)
                   return m_VideoButton;

               if (UIDocument.rootVisualElement.Q<Button>(VIDEO_BUTTON_NAME) is not { } button)
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

                if (UIDocument.rootVisualElement.Q<Button>(HSL_BUTTON_NAME) is not { } button)
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

                if (UIDocument.rootVisualElement.Q<Button>(WORLD_BUTTON_NAME) is not { } button)
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
            if (VideoButton is { } videoButton)
            {
                videoButton.clicked += HandleVideoButtonClicked;
            }
            
            if (HsvButton is { } hsvButton)
            {
                hsvButton.clicked += HandleHsvButtonClicked;
            }
            
            if (WorldButton is { } worldButton)
            {
                worldButton.clicked += HandleWorldButtonClicked;
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

        private void OnDisable()
        {
            if (VideoButton is { } videoButton)
            {
                videoButton.clicked -= HandleVideoButtonClicked;
            }
            
            if (HsvButton is { } hsvButton)
            {
                hsvButton.clicked -= HandleHsvButtonClicked;
            }
            
            if (WorldButton is { } worldButton)
            {
                worldButton.clicked -= HandleWorldButtonClicked;
            }
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
