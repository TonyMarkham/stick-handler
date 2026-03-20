using UnityEngine;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public partial class HsvCalibrationMenuController
    {
        private class SavingOverlay : VisualElement
        {
            private float _progress;
            private readonly Label _label;

            public float Progress
            {
                get => _progress;
                set
                {
                    _progress = Mathf.Clamp01(value);
                    MarkDirtyRepaint();
                }
            }

            public SavingOverlay()
            {
                style.position = Position.Absolute;
                style.left = 0;
                style.right = 0;
                style.top = 0;
                style.bottom = 0;
                style.alignItems = Align.Center;
                style.justifyContent = Justify.Center;
                pickingMode = PickingMode.Ignore;

                _label = new Label("SAVING") { pickingMode = PickingMode.Ignore };
                _label.style.color = Color.white;
                _label.style.fontSize = 7;
                _label.style.unityFontStyleAndWeight = FontStyle.Bold;
                _label.style.position = Position.Absolute;
                Add(_label);

                generateVisualContent += Draw;
                visible = false;
            }

            public event System.Action OnShow;
            public event System.Action OnHide;

            public void Show()
            {
                visible = true;
                Progress = 0;
                OnShow?.Invoke();
            }

            public void Hide()
            {
                visible = false;
                Progress = 0;
                OnHide?.Invoke();
            }

            private void Draw(MeshGenerationContext ctx)
            {
                if (_progress <= 0) return;

                float cx = layout.width / 2f;
                float cy = layout.height / 2f;
                float radius = Mathf.Min(cx, cy) - 3f;

                var p2d = ctx.painter2D;
                p2d.lineWidth = 3f;

                // Faint background ring
                p2d.strokeColor = new Color(1f, 1f, 1f, 0.25f);
                p2d.BeginPath();
                p2d.Arc(new Vector2(cx, cy), radius, 0f, 360f, ArcDirection.Clockwise);
                p2d.Stroke();

                // Filling arc, clockwise from top
                p2d.strokeColor = Color.white;
                p2d.BeginPath();
                p2d.Arc(new Vector2(cx, cy), radius, -90f, -90f + _progress * 360f, ArcDirection.Clockwise);
                p2d.Stroke();
            }
        }
    }
}