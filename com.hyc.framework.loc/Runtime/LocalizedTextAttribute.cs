using UnityEngine;

namespace HYC.Framework.Loc
{
    /// <summary>
    /// Marks a string field as a localization key. Provides a key-selector
    /// dropdown and text preview in the inspector.
    /// </summary>
    public class LocalizedTextAttribute : PropertyAttribute
    {
        /// <summary>Whether to show the localized text preview in the inspector.</summary>
        public bool Preview = true;

        public LocalizedTextAttribute() { }

        public LocalizedTextAttribute(bool preview) => Preview = preview;
    }
}