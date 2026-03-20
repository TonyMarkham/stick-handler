using System;
using System.Collections;
using StickHandle.Prefabs.TV;
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
    ///     An optional <c>Toggle</c> that surfaces a boolean detection state to listeners via
    ///     <see cref="OnToggleChanged"/>. Hidden by default; call <see cref="EnableToggle"/> to show it.
    ///   </description></item>
    /// </list>
    /// </remarks>
    public class StillImagePanelController : MonoBehaviour
    {
        private const string IMAGE_DISPLAY_ELEMENT_NAME = "image-display";
        private const string LABEL_NAME = "panel-label";
        private const string TOGGLE_NAME = "detected-toggle";
        
        // ── Inspector ────────────────────────────────────────────────────────────
        
        [SerializeField] private TelevisionController m_TelevisionController;
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

        // ── Private state ────────────────────────────────────────────────────────
        
        [Header("These Populate Automatically - DO NOT POULATE")]
        [SerializeField] private HsvCalibrationMenuController m_HsvCalibrationMenuController;
        [SerializeField] private UIDocument m_UiDocument;
        [SerializeField] private PanelSettings m_PanelSettings; 
        [SerializeField] private VisualTreeAsset m_VisualTreeAsset;
        [SerializeField] private StyleSheet m_StyleSheet;

        /// <summary>
        /// The most recent texture pushed via <see cref="SetTexture"/>.
        /// Cached so it can be applied to the visual tree after the deferred init
        /// coroutine runs, in case <see cref="SetTexture"/> was called before
        /// <see cref="OnEnable"/> completed.
        /// </summary>
        private Texture2D m_CurrentTexture;
        
        // ── UI Elements ──────────────────────────────────────────────────────────
        
        private VisualElement m_ImageDisplayElement;
        public VisualElement ImageDisplayElement
        {
            get
            {
                if(m_ImageDisplayElement is not null)
                    return m_ImageDisplayElement;

                if (m_UiDocument.rootVisualElement.Q<VisualElement>(IMAGE_DISPLAY_ELEMENT_NAME) is not { } element)
                {
                    Debug.LogError($"VisualElement [{IMAGE_DISPLAY_ELEMENT_NAME}] is not set");
                    return null;
                }
               
                m_ImageDisplayElement = element;
                return m_ImageDisplayElement;
            }
        }
        
        private Label m_LabelElement;
        public Label LabelElement
        {
            get
            {
                if(m_LabelElement is not null)
                    return m_LabelElement;

                if (m_UiDocument.rootVisualElement.Q<Label>(LABEL_NAME) is not { } label)
                {
                    Debug.LogError($"Label [{LABEL_NAME}] is not set");
                    return null;
                }
               
                m_LabelElement = label;
                return m_LabelElement;
            }
        }
        
        private Toggle m_ToggleElement;
        public Toggle ToggleElement
        {
            get
            {
                if(m_ToggleElement is not null)
                    return m_ToggleElement;

                if (m_UiDocument.rootVisualElement.Q<Toggle>(TOGGLE_NAME) is not { } toggle)
                {
                    Debug.LogError($"Toggle [{TOGGLE_NAME}] is not set");
                    return null;
                }
               
                m_ToggleElement = toggle;
                return m_ToggleElement;
            }
        }
        
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
            if (GetComponentInParent<HsvCalibrationMenuController>() is not { } hsvCalibrationMenuController)
            {
                Debug.LogError($"[{GetType().Name}] HsvCalibrationMenuController not found in any parent");
                return;
            }

            m_HsvCalibrationMenuController = hsvCalibrationMenuController;
            m_PanelSettings = m_HsvCalibrationMenuController.PanelSettings;
            m_VisualTreeAsset = m_HsvCalibrationMenuController.ImagePanelUxml;
            m_StyleSheet = m_HsvCalibrationMenuController.ImagePanelStyleSheet;
            float pixelsPerUnit = m_PanelSettings.PixelsPerUnitReflection();
            
            if (TelevisionController.ScreenGameobject.GetComponent<UIDocument>() is not { } uiDocument)
            {
                uiDocument = TelevisionController.ScreenGameobject.AddComponent<UIDocument>();
            }
            
            TelevisionController.DisableScreen();
            
            m_UiDocument = uiDocument;
            m_UiDocument.panelSettings = m_PanelSettings;
            m_UiDocument.worldSpaceSize = new Vector2(pixelsPerUnit, pixelsPerUnit);
            m_UiDocument.visualTreeAsset = m_VisualTreeAsset;
            m_UiDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);
        }

        private void Start()
        {
            StartCoroutine(Init());
        }

        private IEnumerator Init()
        {
            yield return StartCoroutine(InitNextFrame());
        }

        /// <summary>
        /// Waits one frame for the <see cref="UIDocument"/> to finish rebuilding its
        /// visual tree, then queries the required elements and applies the current
        /// label text and texture.
        /// </summary>
        public IEnumerator InitNextFrame()
        {
            yield return null;

            LabelElement.text = m_Label;

            if (ToggleEnabled
                && ToggleElement is not null)
            {
                ToggleElement.RegisterValueChangedCallback(OnDetectedToggleValueChanged);

                // If EnableToggle() was called before the tree was ready, honour it now.
                if (m_ToggleEnabled)
                    ToggleElement.style.display = DisplayStyle.Flex;
            }

            // Apply any texture that was pushed before the tree was ready.
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
            if(!m_UiDocument.rootVisualElement.styleSheets.Contains(m_StyleSheet))
                m_UiDocument.rootVisualElement.styleSheets.Add(m_StyleSheet);
            
            TelevisionController.Switch(true);

            StartCoroutine(InitNextFrame());
        }

        private void OnDisable()
        {
            // Wrapped in a try block in case it ever it's enabled in the Scene before playing 
            try
            {
                ToggleElement.UnregisterValueChangedCallback(OnDetectedToggleValueChanged);
            }
            catch (Exception)
            {
                // Ignore
            }
            
            m_ImageDisplayElement = null;
            m_LabelElement = null;
            m_ToggleElement = null;
            
            TelevisionController.Switch(false);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Updates the image displayed in the panel's <c>image-display</c> element.
        /// </summary>
        /// <param name="tex">
        /// The texture to display. Pass <c>null</c> to keep the current texture
        /// (the background-image USS property will not be updated).
        /// </param>
        /// <remarks>
        /// Safe to call at any time. If the visual tree is not yet ready the texture
        /// is cached and applied once <see cref="InitNextFrame"/> completes.
        /// </remarks>
        public void SetTexture(Texture2D tex)
        {
            m_CurrentTexture = tex;
            ImageDisplayElement.style.backgroundImage = new StyleBackground(tex);
        }
    }
}

