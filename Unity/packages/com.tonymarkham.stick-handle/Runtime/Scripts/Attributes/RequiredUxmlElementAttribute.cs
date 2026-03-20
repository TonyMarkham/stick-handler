using System;

namespace StickHandle.Scripts.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class RequiredUxmlElementAttribute : Attribute
    {
        public readonly Type   ElementType;
        public readonly string Name;

        public RequiredUxmlElementAttribute(Type elementType, string name)
        {
            ElementType = elementType;
            Name        = name;
        }
    }
}
