using System;
using UnityEngine;

namespace StickHandle.Scripts.Attributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RequiredRefAttribute : PropertyAttribute { }
}
