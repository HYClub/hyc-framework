using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using HYC.Framework.IO;

namespace HYC.Framework.Loc
{
    /// <summary>
    /// Blob-driven localization manager. Reads <c>id</c>/<c>lang</c>/<c>filter</c>
    /// files plus one <c>{lang}.lang</c> blob per language from a folder and
    /// serves key lookups. Decoupled HYC port of the source LocalizationManager
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

            // 曾在此处（仅编辑器）补一次 FindObjectsOfType<LocalizedBase> 全场景强制刷新。
            // 已移除：onLanguageChanged 会刷新所有已启用组件（LocalizedBase.OnEnable 订阅），
            // 而组件重新激活时 OnEnable 自身也会 Refresh —— 两条路径已覆盖全部场景，
            // 全场景扫描只是重复劳动，且开销随场景对象数线性增长。
        }

        #region 文件读取

        /// <summary>
        /// 从 <paramref name="folder"/> 重新读取全部本地化数据。
        ///
        /// ⚠️ 失败语义是「**保持原数据不变**」。目录不存在 / 路径写错 / blob 解析抛错 /
        /// <c>id</c> 条目为空…… 一律只报错并返回。
        ///
        /// 旧实现是「<b>先清空所有静态字段、再读盘</b>」，一旦读盘失败（最常见的就是
        /// 路径写错）宿主的本地化表就整张丢失，现象是 UI 全线「未找到Key : xxx」，
        /// 而过程毫无日志线索，排查成本极高。现在改为：先读进局部变量，
        /// 全部成功才<b>一次性原子替换</b>静态状态。
        /// </summary>
        public static void Reload(string folder)
        {
            if (string.IsNullOrEmpty(folder) || (!StreamingAssets.NeedsPreload && !Directory.Exists(folder)))
            {
                Debug.LogError($"[Localization] Reload 失败：目录不存在或路径为空（{folder ?? "<null>"}）。" +
                               "已保留现有本地化数据。");
                return;
            }

            string[] newIds = null, newLangs = null, newSensitive = null;
            string[][] newTexts = null;
            string newConfigLanguage = mConfigLanguage;
#if UNITY_EDITOR
            string[] newExcelNames = null;
            int[] newIdExcelNameIndexs = null;
#endif
            var langFiles = new List<string>();

            try
            {
                var files = StreamingAssets.NeedsPreload ? ListCachedFiles(folder) : Directory.GetFiles(folder);
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    switch (name)
                    {
                        case ID_FILE_NAME:
                            newIds = ReadStrings(file);
                            break;
                        case DEF_LANG_FILE_NAME:
                            newConfigLanguage = ReadLang(file, "en");
                            break;
                        case SENSITIVE_WORD_FILE_NAME:
                            newSensitive = ReadStrings(file);
                            break;
#if UNITY_EDITOR
                        case DEBUG_ID_FILE_NAME:
                            newIdExcelNameIndexs = ReadInts(file);
                            break;
                        case DEBUG_NAME_FILE_NAME:
                            newExcelNames = ReadStrings(file);
                            break;
#endif
                        default:
                            if (Path.GetExtension(file) == LANG_FILE_EXT)
                                langFiles.Add(file);
                            break;
                    }
                }

                if (newIds == null || newIds.Length == 0)
                {
                    Debug.LogError($"[Localization] {folder} 中没有可用的 {ID_FILE_NAME} 条目（key 表为空），" +
                                   "判定为无效数据目录。已保留现有本地化数据。");
                    return;
                }

                langFiles.Sort();

                newLangs = langFiles.Select(r => LocaleUtil.NormalizeLangHeader(Path.GetFileNameWithoutExtension(r))).ToArray();
                newTexts = new string[newLangs.Length][];

                for (var i = 0; i < langFiles.Count; i++)
                    newTexts[i] = ReadStrings(langFiles[i]);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Localization] Reload({folder}) 失败：{ex.GetType().Name}: {ex.Message}。" +
                               "已保留现有本地化数据。");
                return;
            }

            // —— 原子提交：以上全部成功，才替换静态状态 ——
            mIDs = newIds;
            mLangs = newLangs;
            mTexts = newTexts;
            mConfigLanguage = newConfigLanguage;
            if (newSensitive != null) mSensitiveWords = newSensitive;
#if UNITY_EDITOR
            mExcelNames = newExcelNames;
            mIDExcelNameIndexs = newIdExcelNameIndexs;
#endif
            // 索引按新数据惰性重建（GetTextByLang 内部判空后自己填）
            mLang2Index = null;
            mKey2Index = null;

            RefreshAll();
        }

        private static string ReadLang(string path, string def)
            => ReadAllTextSafe(path) ?? def;

        private static byte[] ReadAllBytesSafe(string path)
            => StreamingAssets.NeedsPreload ? StreamingAssets.ReadBytes(path) : File.ReadAllBytes(path);

        private static string ReadAllTextSafe(string path)
            => StreamingAssets.NeedsPreload ? StreamingAssets.ReadText(path) : (File.Exists(path) ? File.ReadAllText(path) : null);

        /// <summary>NeedsPreload 平台（WebGL / Android）不能列目录，改为从预取缓存里按相对目录前缀枚举键。</summary>
        private static string[] ListCachedFiles(string folder)
        {
            var rel = folder;
            if (rel.StartsWith(Application.streamingAssetsPath))
                rel = rel.Substring(Application.streamingAssetsPath.Length).TrimStart('/');
            var keys = new List<string>();
            foreach (var k in StreamingAssets.GetKeysUnder(rel))
                keys.Add(k);
            return keys.ToArray();
        }

        private static int[] ReadInts(string path)
        {
            var bytes = ReadAllBytesSafe(path);
            if (bytes == null) return EmptyInts;
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
            var bytes = ReadAllBytesSafe(path);
            if (bytes == null) return EmptyString;
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
