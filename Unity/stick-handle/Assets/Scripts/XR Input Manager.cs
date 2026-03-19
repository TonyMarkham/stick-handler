using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class XRInputManager : MonoBehaviour
{
    [SerializeField] private InputActionReference menuToggleAction;
    [SerializeField] private InputActionReference rightA;
    [SerializeField] private GameObject menuGameObject;
    
    [Header("Calibration")]
    [SerializeField] private GameObject m_CalibrationGameObject;
    [SerializeField] private UIDocument m_CalibrationUiDocument;
    [SerializeField] private CalibrationMenuController m_CalibrationMenuController;

    private void OnEnable()
    {
        menuToggleAction.action.performed += HandleMenuToggle;
    }
    
    private void OnDisable()
    {
        menuToggleAction.action.performed -= HandleMenuToggle;
    }

    private void HandleMenuToggle(InputAction.CallbackContext obj)
    {
        menuGameObject.SetActive(!menuGameObject.activeSelf);
    }

    public InputActionReference GetRightA()
    {
        return rightA;
    }
}
