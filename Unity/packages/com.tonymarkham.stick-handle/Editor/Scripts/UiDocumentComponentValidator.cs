using System;
using System.Reflection;
using StickHandle.Scripts.Attributes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Scripts
{
    [InitializeOnLoad]
    public static class UiDocumentComponentValidator
    {
        static UiDocumentComponentValidator()
            => AssemblyReloadEvents.afterAssemblyReload += Validate;

        private static void Validate()
        {
            var markedTypes = TypeCache.GetTypesWithAttribute<RequiredUxmlElementAttribute>();

            foreach (var type in markedTypes)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;

                var instances = UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var instance in instances)
                    ValidateInstance(type, (MonoBehaviour)instance);
            }
        }

        private static void ValidateInstance(Type type, MonoBehaviour mono)
        {
            string goPath = GetGameObjectPath(mono.gameObject);

            VisualTreeAsset primaryVta = null;
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (var field in fields)
            {
                if (field.GetCustomAttribute<RequiredRefAttribute>() == null) continue;

                var value = field.GetValue(mono);
                bool isNull = value == null || (value is UnityEngine.Object obj && !obj);

                if (isNull)
                {
                    Debug.LogError(
                        $"[UiDocumentComponentValidator] {type.Name} on '{goPath}': " +
                        $"required field '{field.Name}' is not assigned.", mono);
                }
                else if (primaryVta == null && field.FieldType == typeof(VisualTreeAsset))
                {
                    primaryVta = (VisualTreeAsset)value;
                }
            }

            if (primaryVta == null) return;

            var tempRoot  = primaryVta.Instantiate();
            var uxmlAttrs = type.GetCustomAttributes<RequiredUxmlElementAttribute>();

            foreach (var attr in uxmlAttrs)
            {
                var element = tempRoot.Q(attr.Name);
                if (element == null || !attr.ElementType.IsInstanceOfType(element))
                {
                    Debug.LogError(
                        $"[UiDocumentComponentValidator] {type.Name} on '{goPath}': " +
                        $"UXML element '{attr.Name}' of type {attr.ElementType.Name} " +
                        $"not found in '{primaryVta.name}'.", mono);
                }
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            var t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t    = t.parent;
            }
            return path;
        }
    }
}
