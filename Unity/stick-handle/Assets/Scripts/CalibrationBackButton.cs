using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Attach to any UIDocument GO that contains a "back-btn" element.
/// Calls CalibrationMenuController.ShowMenu() on the root Calibration GO
/// via GetComponentInParent — no Inspector wiring needed.
/// </summary>
public class CalibrationBackButton : MonoBehaviour
{
    [SerializeField] private CalibrationMenuController _CalibMenuController;
    [SerializeField] private UIDocument _uiDocument;
    private Button _backBtn;

    private void OnEnable()
    {
        var root = _uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[BackButton] rootVisualElement is null — assign Panel Settings in Inspector");
            return;
        }

        _backBtn = root.Q<Button>("back-btn");
        if (_backBtn == null)
        {
            Debug.LogError("[BackButton] 'back-btn' not found in UXML");
            return;
        }

        _backBtn.clicked += HandleBack;
    }

    private void OnDisable()
    {
        if (_backBtn != null) _backBtn.clicked -= HandleBack;
    }

    private void HandleBack()
    {
        // CalibrationMenuController lives on a sibling GO (CalibrationMenu), not a direct
        // ancestor, so search from the Calibration root downward.
        var menu = _CalibMenuController;
        if (menu == null)
        {
            Debug.LogError("[BackButton] No CalibrationMenuController found under parent GO");
            return;
        }

        menu.ShowMenu();
    }
}
