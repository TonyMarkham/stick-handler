using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StickHandle
{
    public class CalibrationModeController : MonoBehaviour
    {
        private const string CLASS_NAME = nameof(CalibrationModeController);

        [Header("World Calibration Data")]
        [SerializeField] private WorldCalibrationData m_WorldCalibrationData;
        public WorldCalibrationData WorldCalibrationData => m_WorldCalibrationData;
        
        [Header("Input Actions")]
        [SerializeField] private InputActionReference m_LeftXButtonAction;
        [SerializeField] private InputActionReference m_RightAButtonAction;
        
        [Header("Calibration")]
        [SerializeField] private GameObject m_ConfigurationMenuGameObject;
        [SerializeField] private GameObject m_HsvConfigurationMenuGameObject;
        
        private void OnEnable()
        {
            ActivateMasterCalibrationMenu();

            if (!m_LeftXButtonAction)
            {
                Debug.LogError($"[{CLASS_NAME}] Left X Button Action not set");
            }
            else
            {
                m_LeftXButtonAction.action.Enable();
                m_LeftXButtonAction.action.performed += HandleToggleCalibrationMode;
            }
            
            if (!m_RightAButtonAction)
            {
                Debug.LogError($"[{CLASS_NAME}] Right A Button Action not set");
            }
        }

        private void OnDisable()
        {
            if (m_LeftXButtonAction)
            {
                m_LeftXButtonAction.action.performed -= HandleToggleCalibrationMode;
                m_LeftXButtonAction.action.Disable();
            }
        }

        public void ActivateMasterCalibrationMenu()
        {
            m_ConfigurationMenuGameObject.SetActive(true);
            m_HsvConfigurationMenuGameObject.SetActive(false);
        }

        public void ActivateHslCalibrationMenu()
        {
            m_HsvConfigurationMenuGameObject.SetActive(true);
            m_ConfigurationMenuGameObject.SetActive(false);
        }

        public void HandleToggleCalibrationMode(InputAction.CallbackContext obj)
        {
            m_ConfigurationMenuGameObject.SetActive(!m_ConfigurationMenuGameObject.activeSelf);
        }

        public bool GetRightAButtonAction(out InputActionReference rightAButtonAction)
        {
            rightAButtonAction =  m_RightAButtonAction;
            
            if (m_RightAButtonAction)
                return true;
            
            Debug.LogError($"[{CLASS_NAME}] Right A Button Action not set");
            return false;
        }
    }
}
