using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace StickHandle.Scripts
{
    public static class PanelSettingsExtensionMethods
    {
        public static float PixelsPerUnitReflection(this PanelSettings panelSettings)
        {
            return TryGetPixelPerUnitFromPanelSettings(panelSettings, out float pixelPerUnit) ? pixelPerUnit : default;
        }
        
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