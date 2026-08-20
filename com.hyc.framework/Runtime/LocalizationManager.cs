using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace HYC.Framework.Loc
{
    /// <summary>
    /// Blob-driven localization manager. Reads <c>id</c>/<c>lang</c>/<c>filter</c>
    /// files plus one <c>{lang}.lang</c> blob per language from a folder and
    /// serves key lookups. Decoupled QK port of the source LocalizationManager
    /// (blob structs moved in-package, config namespace dependency removed).
    /// </summary>
    public static class LocalizationManager
    {
        public const string ID_FILE_NAME = "id";
        public const string DEF_LANG_FILE_NAME = "lang";
        public const string SENSITIVE_WORD_FILE_NAME = "filter";
        public const string LANG_FILE_EXT = ".lang";

#if UNITY_EDITOR
        public const string DEBUG_ID_FILE_NAME = "debug_ids";
        public const string DEBUG_NAME_FILE_NAME = "debug_names";
#endif

        private static readonly int[] EmptyInts = Array.Empty<int>();
        private static readonly string[] EmptyString = Array.Empty<string>();
        private static readonly string[][] EmptyStrings = Array.Empty<string[]>();

        private static string[] mIDs;
        private static string[] mLangs;
        public static string[][] mTexts;

        private static Dictionary<string, int> mLang2Index;
        private static Dictionary<string, int> mKey2Index;

#if UNITY_EDITOR
        private static string[] mExcelNames;
        private static int[] mIDExcelNameIndexs;
#endif

        private static string[] mSensitiveWords;

        private static string mConfigLanguage = "en";
        private static string mDefaultLanguage;
        private static string mSelectedLanguage;

        /// <summary>Raised whenever the selected language changes or data reloads.</summary>
        public static event Action onLanguageChanged;

        public static string[] IDs => mIDs ?? EmptyString;
        public static string[] Langs => mLangs ?? EmptyString;
        public static string[][] Texts => mTexts ?? EmptyStrings;

#if UNITY_EDITOR
        public static string[] ExcelNames => mExcelNames ?? EmptyString;
        public static int[] IDExcelNameIndexs => mIDExcelNameIndexs ?? EmptyInts;
#endif

        public static string[] SensitiveWords => mSensitiveWords ?? EmptyString;

        /// <summary>Language configured at build/import time (default "en").</summary>
        public static string ConfigLanguage
        {
            get => mConfigLanguage;
            set => mConfigLanguage = value;
        }

        /// <summary>Language set by the host as fallback when no selection exists.</summary>
        public static string DefaultLanguage
        {
            get => mDefaultLanguage;
            set => mDefaultLanguage = value;
        }

        /// <summary>Current language, resolving selection over default over config.</summary>
        public static string SelectedLanguage
        {
            get
            {
                var lang = ConfigLanguage;
                if (!string.IsNullOrEmpty(mDefaultLanguage) && Langs.Contains(mDefaultLanguage))
                    lang = mDefaultLanguage;
                if (!string.IsNullOrEmpty(mSelectedLanguage) && Langs.Contains(mSelectedLanguage))
                    lang = mSelectedLanguage;
                return lang;
            }
            set
            {
                if (!string.Equals(mSelectedLanguage, value))
                {
                    mSelectedLanguage = value;
                    RefreshAll();
                }
            }
        }

        /// <summary>Current language as a <see cref="Locale"/> (Default if the code is unknown).</summary>
        public static Locale CurrentLocale => LocaleUtil.FromCode(SelectedLanguage);

        /// <summary>Switches the active language and notifies all localized components.</summary>
        public static void SetLanguage(Locale locale) => SelectedLanguage = LocaleUtil.ToCode(locale);

        private static void RefreshAll()
        {
            try
            {
                onLanguageChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError("刷新文字出错!");
                Debug.LogException(e);
            }

#if UNITY_EDITOR
            var textComponents = UnityEngine.Object.FindObjectsOfType<LocalizedBase>();
            foreach (var component in textComponents)
                component.Refresh();
#endif
        }

        #region 文件读取

        /// <summary>Clears and re-reads all localization data from <paramref name="folder"/>.</summary>
        public static void Reload(string folder)
        {
            mIDs = null;
            mLangs = null;
            mTexts = null;
            mLang2Index = null;
            mKey2Index = null;

#if UNITY_EDITOR
            mExcelNames = null;
            mIDExcelNameIndexs = null;
#endif

            var langFiles = new List<string>();

            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                switch (name)
                {
                    case ID_FILE_NAME:
                        mIDs = ReadStrings(file);
                        break;
                    case DEF_LANG_FILE_NAME:
                        mConfigLanguage = ReadLang(file, "en");
                        break;
                    case SENSITIVE_WORD_FILE_NAME:
                        mSensitiveWords = ReadStrings(file);
                        break;
#if UNITY_EDITOR
                    case DEBUG_ID_FILE_NAME:
                        mIDExcelNameIndexs = ReadInts(file);
                        break;
                    case DEBUG_NAME_FILE_NAME:
                        mExcelNames = ReadStrings(file);
                        break;
#endif
                    default:
                        if (Path.GetExtension(file) == LANG_FILE_EXT)
                            langFiles.Add(file);
                        break;
                }
            }

            if (mIDs != null && mIDs.Length > 0)
            {
                langFiles.Sort();

                mLangs = langFiles.Select(r => LocaleUtil.NormalizeLangHeader(Path.GetFileNameWithoutExtension(r))).ToArray();
                mTexts = new string[mLangs.Length][];

                for (var i = 0; i < langFiles.Count; i++)
                    mTexts[i] = ReadStrings(langFiles[i]);
            }

            RefreshAll();
        }

        private static string ReadLang(string path, string def)
            => File.Exists(path) ? File.ReadAllText(path) : def;

        private static int[] ReadInts(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var result = new List<int>();
            unsafe
            {
                fixed (byte* ptr = &bytes[0])
                {
                    using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                    if (BlobAssetReference<CfgLocalizationIndex>.TryRead(reader, 3, out var blob))
                    {
                        for (var i = 0; i < blob.Value.values.Length; i++)
                            result.Add(blob.Value.values[i]);
                    }
                }
            }
            return result.ToArray();
        }

        private static string[] ReadStrings(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var result = new List<string>();
            unsafe
            {
                fixed (byte* ptr = &bytes[0])
                {
                    using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                    if (BlobAssetReference<CfgLocalization>.TryRead(reader, 3, out var blob))
                    {
                        for (var i = 0; i < blob.Value.values.Length; i++)
                            result.Add(blob.Value.values[i].ToString());
                    }
                }
            }
            return result.ToArray();
        }

        #endregion

        #region 文字查询

        /// <summary>Whether <paramref name="key"/> exists in the loaded data.</summary>
        public static bool HasKey(string key)
        {
            if (mKey2Index == null)
                GetText(key);
            return mKey2Index != null && mKey2Index.ContainsKey(key);
        }

        /// <summary>Localized text for <paramref name="key"/> in the current language.</summary>
        public static string GetText(string key)
            => !string.IsNullOrEmpty(key) ? GetTextByLang(key.Trim(), SelectedLanguage) : string.Empty;

        /// <summary>Localized text with {number} placeholders formatted via string.Format.</summary>
        public static string GetText(string key, params object[] args)
        {
            var result = GetText(key);
            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    result = string.Format(result, args);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            return result;
        }

        /// <summary>Localized text for <paramref name="key"/> in a specific <paramref name="lang"/>.</summary>
        public static string GetTextByLang(string key, string lang)
        {
            if (mLang2Index == null && mLangs != null)
            {
                mLang2Index = new Dictionary<string, int>();
                for (var i = 0; i < mLangs.Length; i++)
                    mLang2Index.Add(mLangs[i], i);
            }

            if (mKey2Index == null && mIDs != null)
            {
                mKey2Index = new Dictionary<string, int>();
                for (var i = 0; i < mIDs.Length; i++)
                    mKey2Index.Add(mIDs[i], i);
            }

            if (mLang2Index != null && mKey2Index != null
                && mLang2Index.TryGetValue(lang, out var langIndex)
                && mKey2Index.TryGetValue(key, out var keyIndex))
            {
                if (langIndex >= 0 && langIndex < mTexts.Length
                    && keyIndex >= 0 && keyIndex < mTexts[langIndex].Length)
                {
                    var result = Texts[langIndex][keyIndex];
                    if (string.IsNullOrEmpty(result))
                        result = $"[{key.Split('/')[^1]}]";
                    else
                    {
#if UNITY_EDITOR
                        if (GetAppendTextType() != 0)
                            result += GetAppendText();
#endif
                    }
                    return result;
                }
            }

            return $"未找到Key : {key}";
        }

        private static int mAppendTextType;

#if UNITY_EDITOR
        public static string GetAppendText()
        {
            switch (GetAppendTextType())
            {
                case 1:
                    return " (Life is a great big canvas, and you should throw all the paint on it you can. 人生是一幅大画布，你应该努力绘出绚丽多彩的画面。)";
                case 2:
                    return " (The song I came to sing  我来唱的歌\nremains unsung to this day.  至今仍未唱出\nI have spent my days in stringing  我日日用琴声\nand in unstringing my instrument.  调整琴柱，解下琴弦\nThe time has not come true,  时日未到\nthe words have not been rightly set;  词未择当\nonly there is the agony  只有烦恼\nof wishing in my heart.....  在我内心诉说……)";
            }
            return string.Empty;
        }

        public static int GetAppendTextType()
            => mAppendTextType;

        public static void SetAppendTextType(int type)
        {
            if (type != GetAppendTextType())
            {
                mAppendTextType = type;
                onLanguageChanged?.Invoke();
            }
        }
#endif
        #endregion
    }
}
