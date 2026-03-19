using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Sets the "cylinder-label" element's text on the sibling UIDocument from an Inspector field.
/// Attach to each Label_A/B/C/D GO alongside the UIDocument (CylinderLabel.uxml).
/// </summary>
public class CylinderLabelController : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private string _text = "1";

    private void OnEnable()
    {
        var label = _uiDocument?.rootVisualElement?.Q<Label>("cylinder-label");
        if (label != null)
            label.text = _text;
    }
}
