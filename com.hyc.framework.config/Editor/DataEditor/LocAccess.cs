using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// loc 包（HYC.Framework.Loc）反射适配器。config 包不硬引用 loc 包：
    /// loc 已安装 → 联想/选择/翻译/校验可用；未安装 → <see cref="IsLocInstalled"/> 为 false，
    /// 数据编辑器不显示"多语言Key"类型，LocalizedKey 字段退化为普通 string 输入。
    /// </summary>
    public static class LocAccess
    {
        private const string ManagerTypeName = "HYC.Framework.Loc.LocalizationManager, HYC.Framework.Loc.Runtime";

        private static Type sManagerType;
        private static bool sResolved;
        private static bool sReloadTried;

        private static Type ManagerType
        {
            get
            {
                if (!sResolved)
                {
                    sManagerType = Type.GetType(ManagerTypeName);
                    sResolved = true;
                }
                return sManagerType;
            }
        }

        /// <summary>loc 包是否已安装（能否读到 LocalizationManager）。</summary>
        public static bool IsLocInstalled => ManagerType != null;

        /// <summary>数据为空时自动读取 StreamingAssets/Localization（loc 包导入 Excel 的输出目录），只尝试一次。</summary>
        public static void EnsureLoaded()
        {
            if (sReloadTried || ManagerType == null)
                return;
            sReloadTried = true;
            var m = ManagerType.GetMethod("Reload", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (m == null)
                return;
            var folder = Path.Combine(Application.streamingAssetsPath, "Localization");
            try
            {
                m.Invoke(null, new object[] { folder });
            }
            catch
            {
                // 读取失败保持空数据
            }
        }

        private static readonly string[] Empty = Array.Empty<string>();

        private static string[] GetStaticArray(string propName)
        {
            EnsureLoaded();
            var t = ManagerType;
            if (t == null)
                return Empty;
            var prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
            try
            {
                return prop?.GetValue(null) as string[] ?? Empty;
            }
            catch
            {
                return Empty;
            }
        }

        private static int[] GetStaticIntArray(string propName)
        {
            EnsureLoaded();
            var t = ManagerType;
            if (t == null)
                return Array.Empty<int>();
            var prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
            try
            {
                return prop?.GetValue(null) as int[] ?? Array.Empty<int>();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        /// <summary>所有本地化 key。</summary>
        public static string[] GetKeys() => GetStaticArray("IDs");

        /// <summary>key 来源 Excel 文件名（与 Keys 对齐）。</summary>
        public static string[] GetExcelNames() => GetStaticArray("ExcelNames");

        /// <summary>每个 key 的 Excel 索引（与 Keys 对齐）。</summary>
        public static int[] GetExcelIndexes() => GetStaticIntArray("IDExcelNameIndexs");

        /// <summary>当前语言代码。</summary>
        public static string GetSelectedLanguage()
        {
            var t = ManagerType;
            if (t == null)
                return "";
            var prop = t.GetProperty("SelectedLanguage", BindingFlags.Public | BindingFlags.Static);
            try
            {
                return prop?.GetValue(null) as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>key 是否存在。</summary>
        public static bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key) || ManagerType == null)
                return false;
            EnsureLoaded();
            var m = ManagerType.GetMethod("HasKey", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            try
            {
                return m != null && (bool)m.Invoke(null, new object[] { key });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>取当前语言翻译；key 不存在或该语言缺值返回 null。</summary>
        public static string GetText(string key)
        {
            if (string.IsNullOrEmpty(key) || ManagerType == null)
                return null;
            var m = ManagerType.GetMethod("GetText", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            try
            {
                return m?.Invoke(null, new object[] { key }) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>按语言取翻译；key/语言不存在或缺值返回 null。</summary>
        public static string GetTextByLang(string key, string lang)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(lang) || ManagerType == null)
                return null;
            var m = ManagerType.GetMethod("GetTextByLang", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string) }, null);
            try
            {
                return m?.Invoke(null, new object[] { key, lang }) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
