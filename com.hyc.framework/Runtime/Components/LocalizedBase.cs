using UnityEngine;

namespace HYC.Framework.Loc
{
    /// <summary>
    /// Base component for localized text displays. Holds a localization key and
    /// refreshes automatically on language change or re-enable.
    /// </summary>
    public abstract class LocalizedBase : MonoBehaviour
    {
        [InspectorName("本地化ID"), LocalizedText]
        public string Key;

        /// <summary>Sets the key and refreshes immediately.</summary>
        public void SetKey(string key)
        {
            Key = key;
            Refresh();
        }

        /// <summary>Applies <see cref="Key"/> through the localized text for the current language.</summary>
        public void Refresh()
        {
            if (!string.IsNullOrEmpty(Key))
                Refresh(LocalizationManager.GetText(Key));
        }

        /// <summary>Applies already-resolved <paramref name="text"/> to the target component.</summary>
        public abstract void Refresh(string text);

        protected void OnEnable()
        {
            LocalizationManager.onLanguageChanged -= Refresh;
            LocalizationManager.onLanguageChanged += Refresh;
            Refresh();
        }

        protected void OnDisable()
            => LocalizationManager.onLanguageChanged -= Refresh;

        protected virtual void OnValidate() => Refresh();
    }
}