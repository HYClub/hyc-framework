using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>
    /// Marks a serialized field as read-only in the Inspector (displayed but
    /// not editable).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyAttribute : PropertyAttribute
    {
    }
}
