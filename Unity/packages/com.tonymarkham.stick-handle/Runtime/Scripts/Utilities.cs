using System.Reflection;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public class Utilities
    {
        public static bool TryGetPixelPerUnitFromPanelSettings(PanelSettings panelSettings, out float pixelPerUnit)
        {
            var field = typeof(PanelSettings).GetField(
                "m_PixelsPerUnit",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null)
            {
                pixelPerUnit = (float)field.GetValue(panelSettings);
                return true;
            }

            pixelPerUnit = default;
            return false;
        }
    }
}