using StickHandle.Scripts.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StickHandle.Scripts
{
    public class CalibrationModeController : MonoBehaviour
    {
        private const string CLASS_NAME = nameof(CalibrationModeController);

        [Header("World Calibration Data")]
        [RequiredRef, SerializeField] private WorldCalibrationData m_WorldCalibrationData;
        public WorldCalibrationData WorldCalibrationData => m_WorldCalibrationData;
        
        [Header("Input Actions")]
        [RequiredRef, SerializeField] private InputActionReference m_LeftXButtonAction;
        [RequiredRef, SerializeField] private InputActionReference m_RightAButtonAction;
        
        [Header("Calibration")]
        [RequiredRef, SerializeField] private GameObject m_ConfigurationMenuGameObject;
        [RequiredRef, SerializeField] private GameObject m_HsvConfigurationMenuGameObject;
        [RequiredRef, SerializeField] private GameObject m_WorldOrientationGameObject;

        private void Awake()
        {
            if (!m_WorldCalibrationData)           throw new MissingReferenceException($"[{CLASS_NAME}] WorldCalibrationData is not set");
            if (!m_ConfigurationMenuGameObject)    throw new MissingReferenceException($"[{CLASS_NAME}] ConfigurationMenuGameObject is not set");
            if (!m_HsvConfigurationMenuGameObject) throw new MissingReferenceException($"[{CLASS_NAME}] HsvConfigurationMenuGameObject is not set");
            if (!m_WorldOrientationGameObject)     throw new MissingReferenceException($"[{CLASS_NAME}] WorldOrientationGameObject is not set");
            if (!m_LeftXButtonAction)              throw new MissingReferenceException($"[{CLASS_NAME}] LeftXButtonAction is not set");
            if (!m_RightAButtonAction)             throw new MissingReferenceException($"[{CLASS_NAME}] RightAButtonAction is not set");
        }

        private void OnEnable()
        {
            ActivateMasterCalibrationMenu();

            m_LeftXButtonAction.action.Enable();
            m_LeftXButtonAction.action.performed += HandleToggleCalibrationMode;

            m_RightAButtonAction.action.Enable();
        }

        private void OnDisable()
        {
            m_LeftXButtonAction.action.performed -= HandleToggleCalibrationMode;
            m_LeftXButtonAction.action.Disable();

            m_RightAButtonAction.action.Disable();
        }

        public void ActivateMasterCalibrationMenu()
        {
            m_ConfigurationMenuGameObject.SetActive(true);
            m_HsvConfigurationMenuGameObject.SetActive(false);
            m_WorldOrientationGameObject.SetActive(false);
        }

        public void ActivateHsvCalibrationMenu()
        {
            m_HsvConfigurationMenuGameObject.SetActive(true);
            m_ConfigurationMenuGameObject.SetActive(false);
            m_WorldOrientationGameObject.SetActive(false);
        }

        public void ActivateWorldOrientationMenu()
        {
            m_WorldOrientationGameObject.SetActive(true);
            m_ConfigurationMenuGameObject.SetActive(false);
            m_HsvConfigurationMenuGameObject.SetActive(false);
        }

        public void HandleToggleCalibrationMode(InputAction.CallbackContext obj)
        {
            m_ConfigurationMenuGameObject.SetActive(!m_ConfigurationMenuGameObject.activeSelf);
        }

        public bool GetRightAButtonAction(out InputActionReference rightAButtonAction)
        {
            rightAButtonAction = m_RightAButtonAction;
            return true;
        }
    }
}
