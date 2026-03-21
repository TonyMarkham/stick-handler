using System;
using StickHandle.Scripts.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace StickHandle
{
    [RequiredUxmlElement(typeof(Label), TOP_LABEL_NAME)]
    [RequiredUxmlElement(typeof(Label), LEFT_LABEL_NAME)]
    [RequiredUxmlElement(typeof(Label), RIGHT_NAME)]
    [RequiredUxmlElement(typeof(Label), BOTTOM_LABEL_NAME)]
    
    public class CalibrationCylinderLabelController : MonoBehaviour
    {
        private const string CLASS_NAME = nameof(CalibrationCylinderLabelController);
        
        private const string TOP_LABEL_NAME = "label-top";
        private const string LEFT_LABEL_NAME = "label-left";
        private const string RIGHT_NAME = "label-right";
        private const string BOTTOM_LABEL_NAME = "label-bottom";
        
        [SerializeField] private string m_Label;

        [RequiredRef, SerializeField] private UIDocument m_UiDocument;
        [RequiredRef, SerializeField] private PanelSettings m_PanelSettings;
        [RequiredRef, SerializeField] private VisualTreeAsset m_VisualTreeAsset;
        [RequiredRef, SerializeField] private StyleSheet m_StyleSheet;

        #region UI Elements

        private Label m_TopLabel;
        private Label TopLabel => m_TopLabel;
        
        private Label m_LeftLabel;
        private Label LeftLabel => m_LeftLabel;
        
        private Label m_RightLabel;
        private Label RightLabel => m_RightLabel;
        
        private Label m_BottomLabel;
        private Label BottomLabel => m_BottomLabel;

        #endregion

        private void Awake()
        {
            if (!m_PanelSettings)   throw new MissingReferenceException($"[{CLASS_NAME}] PanelSettings is not set");
            if (!m_VisualTreeAsset) throw new MissingReferenceException($"[{CLASS_NAME}] VisualTreeAsset is not set");
            if (!m_StyleSheet)      throw new MissingReferenceException($"[{CLASS_NAME}] m_StyleSheet is not set");
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
            
            TopLabel.text = m_Label;
            LeftLabel.text = m_Label;
            RightLabel.text = m_Label;
            BottomLabel.text = m_Label;
        }

        private void ResolveElements()
        {
            var root = m_UiDocument.rootVisualElement;
            
            m_TopLabel = root.Q<Label>(TOP_LABEL_NAME);
            if (m_TopLabel is null) throw new InvalidOperationException($"[{CLASS_NAME}] Label [{TOP_LABEL_NAME}] not found");
            
            m_LeftLabel = root.Q<Label>(LEFT_LABEL_NAME);
            if (m_LeftLabel is null) throw new InvalidOperationException($"[{CLASS_NAME}] Label [{LEFT_LABEL_NAME}] not found");
            
            m_RightLabel = root.Q<Label>(RIGHT_NAME);
            if (m_RightLabel is null) throw new InvalidOperationException($"[{CLASS_NAME}] Label [{RIGHT_NAME}] not found");
            
            m_BottomLabel = root.Q<Label>(BOTTOM_LABEL_NAME);
            if (m_BottomLabel is null) throw new InvalidOperationException($"[{CLASS_NAME}] Label [{BOTTOM_LABEL_NAME}] not found");
        }
    }
}
