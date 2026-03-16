using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class StillImagePanelController : MonoBehaviour
{
    [SerializeField] private string _label = "Image";

    private UIDocument   _doc;
    private Texture2D    _currentTexture;

    private void Awake()
    {
        _doc = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        // UIDocument rebuilds its visual tree in OnEnable. If we query elements
        // in the same OnEnable call we may get a stale tree. Defer one frame so
        // the live tree is ready before we apply label and texture.
        StartCoroutine(InitNextFrame());
    }

    private IEnumerator InitNextFrame()
    {
        yield return null;

        var root = _doc.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("[StillImagePanel] rootVisualElement is null — assign Panel Settings in Inspector");
            yield break;
        }

        var imageDisplay = root.Q<VisualElement>("image-display");
        var panelLabel   = root.Q<Label>("panel-label");

        if (imageDisplay == null) { Debug.LogError("[StillImagePanel] 'image-display' not found in UXML"); yield break; }
        if (panelLabel   == null) { Debug.LogError("[StillImagePanel] 'panel-label' not found in UXML");   yield break; }

        panelLabel.text = _label;

        if (_currentTexture != null)
            imageDisplay.style.backgroundImage = new StyleBackground(_currentTexture);
    }

    public void SetTexture(Texture2D tex)
    {
        _currentTexture = tex;

        var root = _doc?.rootVisualElement;
        if (root == null) return;

        var imageDisplay = root.Q<VisualElement>("image-display");
        if (imageDisplay != null)
            imageDisplay.style.backgroundImage = new StyleBackground(tex);
    }
}
