using UnityEngine;
using UnityEngine.InputSystem;

public class XRInputManager : MonoBehaviour
{
    [SerializeField] private InputActionReference menuToggleAction;
    [SerializeField] private InputActionReference rightA;
    [SerializeField] private GameObject menuGameObject;

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
