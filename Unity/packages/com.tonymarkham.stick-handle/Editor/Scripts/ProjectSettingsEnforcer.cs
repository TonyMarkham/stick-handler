using UnityEditor;
using UnityEngine;

namespace Editor.Scripts
{
    [InitializeOnLoad]
    public static class ProjectSettingsEnforcer
    {
        static ProjectSettingsEnforcer()
        {
            if (SessionState.GetBool("ProjectSettingsEnforced", false))
                return;

            EnforceSettings();
            SessionState.SetBool("ProjectSettingsEnforced", true);
        }

        private static void EnforceSettings()
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            Debug.Log("[ProjectSettingsEnforcer] insecureHttpOption set to AlwaysAllowed");
        }
    }
}