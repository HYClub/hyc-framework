using TMPro;
using Unity.Entities;

namespace HYC.Framework.Loc
{
    /// <summary>Convenience extension methods for localized text on TMP_Text and BlobString.</summary>
    public static class LocalizationExtension
    {
        /// <summary>Localized text for <paramref name="key"/> in the current language.</summary>
        public static string ToLocal(this string key)
            => LocalizationManager.GetText(key);

        /// <summary>Localized text with {number} placeholders filled via string.Format.</summary>
        public static string ToLocal(this string key, params object[] args)
            => LocalizationManager.GetText(key, args);

        /// <summary>Localized text for <paramref name="key"/> in a specific language code.</summary>
        public static string ToLocalByLang(this string key, string lang)
            => LocalizationManager.GetTextByLang(key, lang);

        /// <summary>Sets a TMP_Text's localized text for <paramref name="key"/>.</summary>
        public static void SetKey(this TMP_Text tf, string key, params object[] args)
            => tf.text = LocalizationManager.GetText(key, args);

        /// <summary>Sets a TMP_Text's localized text from a BlobString key.</summary>
        public static void SetKey(this TMP_Text tf, ref BlobString key, params object[] args)
            => tf.text = LocalizationManager.GetText(key.ToUtf8String(), args);

        /// <summary>Converts a BlobString to a managed UTF-8 string.</summary>
        public static string ToUtf8String(this ref BlobString blobString)
        {
            if (blobString.Length <= 0)
                return string.Empty;

            return blobString.ToString();
        }
    }
}