using System;
using System.Collections;
using StickHandle.Prefabs.TV;
using StickHandle.Scripts.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    /// <summary>
    /// Controls a UI Toolkit panel that displays a single still image with an optional
    /// "Detected" toggle.
    /// </summary>
    /// <remarks>
    /// The panel is driven by a UXML document attached to the sibling <see cref="UIDocument"/>
    /// component. It expects the following named elements in that UXML:
    /// <list type="bullet">
    ///   <item><term>image-display</term><description>
    ///     A <c>VisualElement</c> whose <c>background-image</c> is set to the current texture.
    ///   </description></item>
    ///   <item><term>panel-label</term><description>
    ///     A <c>Label</c> whose text is set to <see cref="m_Label"/>.
    ///   </description></item>
    ///   <item><term>detected-toggle</term><description>
    ///     A <c>Toggle</c> that surfaces a boolean detection state to listeners via
    ///     <see cref="OnToggleChanged"/>. Hidden by default; call <see cref="EnableToggle"/> to show it.
    ///   </description></item>
    /// </list>
    /// </remarks>
    [RequiredUxmlElement(typeof(VisualElement), IMAGE_DISPLAY_ELEMENT_NAME)]
    [RequiredUxmlElement(typeof(Label),         LABEL_NAME)]
    [RequiredUxmlElement(typeof(Toggle),        TOGGLE_NAME)]
    public class StillImagePanelController : MonoBehaviour
    {
        private const string CLASS_NAME = nameof(StillImagePanelController);
        private const string IMAGE_DISPLAY_ELEMENT_NAME = "image-display";
        private const string LABEL_NAME = "panel-label";
        private const string TOGGLE_NAME = "detected-toggle";

        // ── Inspector ────────────────────────────────────────────────────────────

        [RequiredRef, SerializeField] private TelevisionController m_TelevisionController;
        public TelevisionController TelevisionController => m_TelevisionController;

        /// <summary>
        /// Text displayed in the <c>panel-label</c> element of the UXML document.
        /// Configurable per-panel in the Inspector so that multiple instances of this
        /// prefab can be distinguished (e.g. "Raw", "Masked", "HSV").
        /// </summary>
        [SerializeField] private string m_Label = "Image";

        /// <summary>
        /// Tracks whether <see cref="EnableToggle"/> has been called before the
        /// visual tree was ready, so the toggle can be made visible during deferred
        /// initialisation.
        /// </summary>
        [SerializeField] private bool m_ToggleEnabled;
        public bool ToggleEnabled => m_ToggleEnabled;

        [Header("Panel Settings")]
        [RequiredRef, SerializeField] private PanelSettings m_PanelSettings;
        [RequiredRef, SerializeField] private VisualTreeAsset m_VisualTreeAsset;
        [RequiredRef, SerializeField] private StyleSheet m_StyleSheet;

        /// <summary>
        /// The most recent texture pushed via <see cref="SetTexture"/>.
        /// Cached so it can be applied to the visual tree after the deferred init
        /// coroutine runs, in case <see cref="SetTexture"/> was called before
        /// <see cref="OnEnable"/> completed.
        /// </summary>
        private Texture2D m_CurrentTexture;
        private UIDocument m_UiDocument;

        // ── UI Elements ──────────────────────────────────────────────────────────

        private VisualElement m_ImageDisplayElement;
        private VisualElement ImageDisplayElement => m_ImageDisplayElement;

        private Label m_LabelElement;
        private Label LabelElement => m_LabelElement;

        private Toggle m_ToggleElement;
        public Toggle ToggleElement => m_ToggleElement;

        // ── Events ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised whenever the user changes the value of the "Detected" toggle.
        /// The <c>bool</c> argument is the new toggle value (<c>true</c> = on).
        /// Only fires when the toggle is present in the UXML and has been enabled
        /// via <see cref="EnableToggle"/>.
        /// </summary>
        public event Action<bool> OnToggleChanged;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (!m_TelevisionController) throw new MissingReferenceException($"[{CLASS_NAME}] TelevisionController is not set");
            if (!m_PanelSettings)        throw new MissingReferenceException($"[{CLASS_NAME}] PanelSettings is not set");
            if (!m_VisualTreeAsset)      throw new MissingReferenceException($"[{CLASS_NAME}] VisualTreeAsset is not set");
            if (!m_StyleSheet)           throw new MissingReferenceException($"[{CLASS_NAME}] StyleSheet is not set");

            float pixelsPerUnit = m_PanelSettings.PixelsPerUnitReflection();

            m_UiDocument = TelevisionController.ScreenGameobject.GetComponent<UIDocument>()
                ?? TelevisionController.ScreenGameobject.AddComponent<UIDocument>();

            TelevisionController.DisableScreen();

            m_UiDocument.panelSettings   = m_PanelSettings;
            m_UiDocument.worldSpaceSize  = new Vector2(pixelsPerUnit, pixelsPerUnit);
            m_UiDocument.visualTreeAsset = m_VisualTreeAsset;
            m_UiDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);
        }

        private void ResolveElements()
        {
            var root = m_UiDocument.rootVisualElement;

            m_ImageDisplayElement = root.Q<VisualElement>(IMAGE_DISPLAY_ELEMENT_NAME);
            if (m_ImageDisplayElement is null) throw new InvalidOperationException($"[{CLASS_NAME}] VisualElement [{IMAGE_DISPLAY_ELEMENT_NAME}] not found");

            m_LabelElement = root.Q<Label>(LABEL_NAME);
            if (m_LabelElement is null) throw new InvalidOperationException($"[{CLASS_NAME}] Label [{LABEL_NAME}] not found");

            m_ToggleElement = root.Q<Toggle>(TOGGLE_NAME);
            if (m_ToggleElement is null) throw new InvalidOperationException($"[{CLASS_NAME}] Toggle [{TOGGLE_NAME}] not found");
        }

        /// <summary>
        /// Waits one frame for the <see cref="UIDocument"/> to finish rebuilding its
        /// visual tree, then resolves elements and applies the current label text and texture.
        /// </summary>
        private IEnumerator InitNextFrame()
        {
            yield return null;

            ResolveElements();

            LabelElement.text = m_Label;

            if (ToggleEnabled && ToggleElement is not null)
            {
                ToggleElement.RegisterValueChangedCallback(OnDetectedToggleValueChanged);

                if (m_ToggleEnabled)
                    ToggleElement.style.display = DisplayStyle.Flex;
            }

            if (m_CurrentTexture != null)
                ImageDisplayElement.style.backgroundImage = new StyleBackground(m_CurrentTexture);
        }

        /// <summary>
        /// Forwards toggle value-change events to <see cref="OnToggleChanged"/> subscribers.
        /// </summary>
        private void OnDetectedToggleValueChanged(ChangeEvent<bool> evt) =>
            OnToggleChanged?.Invoke(evt.newValue);

        private void OnEnable()
        {
            if (!m_UiDocument.rootVisualElement.styleSheets.Contains(m_StyleSheet))
                m_UiDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);

            TelevisionController.Switch(true);

            StartCoroutine(InitNextFrame());
        }

        private void OnDisable()
        {
            m_ToggleElement?.UnregisterValueChangedCallback(OnDetectedToggleValueChanged);

            TelevisionController.Switch(false);

            m_ImageDisplayElement = null;
            m_LabelElement        = null;
            m_ToggleElement       = null;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Updates the image displayed in the panel's <c>image-display</c> element.
        /// </summary>
        public void SetTexture(Texture2D tex)
        {
            m_CurrentTexture = tex;
            ImageDisplayElement.style.backgroundImage = new StyleBackground(tex);
        }

#if UNITY_EDITOR
        [ContextMenu("Auto Populate")]
        private void AutoPopulate()
        {
            if (GetComponentInParent<HsvCalibrationMenuController>(true) is not { } parent)
            {
                Debug.LogError($"[{CLASS_NAME}] HsvCalibrationMenuController not found in any parent");
                return;
            }

            m_PanelSettings    = parent.PanelSettings;
            m_VisualTreeAsset  = parent.ImagePanelUxml;
            m_StyleSheet       = parent.ImagePanelStyleSheet;

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[{CLASS_NAME}] Auto Populate complete");
        }
#endif
    }
}
