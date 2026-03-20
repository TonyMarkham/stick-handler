using UnityEngine;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    [UxmlElement]
    public partial class SavingOverlay : VisualElement
    {
        private const string USS_CLASS_NAME        = "saving-overlay";
        private const float  LINE_WIDTH            = 3f;
        private const float  RADIUS_INSET          = 3f;
        private const float  BACKGROUND_ALPHA      = 0.25f;
        private const float  ARC_BACKGROUND_START  = 0f;
        private const float  ARC_START_ANGLE       = -90f;
        private const float  FULL_CIRCLE_DEGREES   = 360f;

        private static readonly Color BACKGROUND_RING_COLOR = new Color(1f, 1f, 1f, BACKGROUND_ALPHA);

        private float _progress;

        public float Progress
        {
            get => _progress;
            set { _progress = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public SavingOverlay()
        {
            AddToClassList(USS_CLASS_NAME);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            visible = false;
        }

        public void Show() { visible = true; Progress = 0; }
        public void Hide() { visible = false; Progress = 0; }

        private void Draw(MeshGenerationContext ctx)
        {
            if (_progress <= 0) return;
            float cx     = layout.width  / 2f;
            float cy     = layout.height / 2f;
            float radius = Mathf.Min(cx, cy) - RADIUS_INSET;
            var   p2d    = ctx.painter2D;
            p2d.lineWidth = LINE_WIDTH;
            p2d.strokeColor = BACKGROUND_RING_COLOR;
            p2d.BeginPath();
            p2d.Arc(new Vector2(cx, cy), radius, ARC_BACKGROUND_START, FULL_CIRCLE_DEGREES, ArcDirection.Clockwise);
            p2d.Stroke();
            p2d.strokeColor = Color.white;
            p2d.BeginPath();
            p2d.Arc(new Vector2(cx, cy), radius, ARC_START_ANGLE, ARC_START_ANGLE + _progress * FULL_CIRCLE_DEGREES, ArcDirection.Clockwise);
            p2d.Stroke();
        }
    }
}
