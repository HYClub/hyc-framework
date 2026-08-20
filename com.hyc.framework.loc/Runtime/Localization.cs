namespace HYC.Framework.Loc
{
    /// <summary>Supported languages. Each maps to a stable code used as the
    /// Excel column header and the StreamingAssets file name.</summary>
    public enum Locale
    {
        Default = 0,
        Chinese = 1,
        English = 2,
        Japanese = 3,
        Korean = 4
    }

    /// <summary>
    /// Stable language-code mapping used across the localization pipeline:
    /// Excel column headers, .lang file names and <see cref="Locale"/>.
    /// </summary>
    public static class LocaleUtil
    {
        /// <summary>Code used for <see cref="Locale.Default"/>.</summary>
        public const string DefaultCode = "en";

        /// <summary>Converts a locale to its stable code.</summary>
        public static string ToCode(Locale locale)
        {
            switch (locale)
            {
                case Locale.Chinese: return "cn";
                case Locale.English: return "en";
                case Locale.Japanese: return "jp";
                case Locale.Korean: return "kr";
                default: return DefaultCode;
            }
        }

        /// <summary>Converts a code to a locale; unknown codes map to
        /// <see cref="Locale.Default"/>.</summary>
        public static Locale FromCode(string code)
        {
            switch ((code ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "cn":
                case "zh":
                case "chs":
                    return Locale.Chinese;
                case "en":
                case "us":
                    return Locale.English;
                case "jp":
                case "ja":
                    return Locale.Japanese;
                case "kr":
                case "ko":
                    return Locale.Korean;
                default:
                    return Locale.Default;
            }
        }

        /// <summary>Normalizes an Excel column header to a stable code so
        /// translators can keep writing localized headers (中文/English/日本語).
        /// Unknown headers are returned trimmed as-is.</summary>
        public static string NormalizeLangHeader(string header)
        {
            switch ((header ?? string.Empty).Trim())
            {
                case "中文":
                case "简体中文":
                case "简中":
                case "cn":
                case "zh":
                case "chs":
                    return "cn";
                case "繁體中文":
                case "繁中":
                case "tc":
                case "cht":
                    return "tc";
                case "English":
                case "en":
                case "us":
                    return "en";
                case "日本語":
                case "日文":
                case "jp":
                case "ja":
                    return "jp";
                case "한국어":
                case "韩文":
                case "kr":
                case "ko":
                    return "kr";
                default:
                    return (header ?? string.Empty).Trim();
            }
        }
    }
}