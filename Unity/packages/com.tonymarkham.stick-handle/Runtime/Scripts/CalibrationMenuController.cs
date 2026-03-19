using System;
using StickHandle.Prefabs.TV;
using UnityEngine;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public class CalibrationMenuController : MonoBehaviour
    {
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
                
                if (screenGameobject.AddComponent<BoxCollider>() is not { } boxCollider)
                {
                    Debug.LogError("Could not  add BoxCollider");
                    return null;
                }

                if (!Utilities.TryGetPixelPerUnitFromPanelSettings(PanelSettings, out float pixelPerUnit))
                {
                    Debug.LogError("Could not extract pixel per unit from panel settings");
                    return null;
                }

                // boxCollider.size = new Vector3(screenGameobject.transform.localScale.x, screenGameobject.transform.localScale.y, 1);
                // boxCollider.center = Vector3.zero;
                
                Renderer screenRenderer = screenGameobject.GetComponent<Renderer>();
                screenRenderer.enabled = false;
                newUIDocument.panelSettings = m_PanelSettings;
                newUIDocument.visualTreeAsset = m_VisualTreeAsset;
                newUIDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);
                newUIDocument.worldSpaceSize = new Vector2(pixelPerUnit, pixelPerUnit);
                m_UIDocument = newUIDocument;
                
                return m_UIDocument;
            }
        }

        private void OnEnable()
        {
            if(UIDocument)
            {
                TelevisionController.Switch(true);
            }
            
        }
    }
}
