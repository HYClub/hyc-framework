using System;
using System.IO;
using UnityEditor;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Project-level configuration for the QK data editor: the asset root
    /// folder scanned by <see cref="ConfigDataTree"/> and a local ID generator
    /// (no server round-trip, unlike the source StarDeep client).
    /// </summary>
    public static class ConfigDataSettings
    {
        private const string PrefRootFolder = "HYC.Framework.Config.DataEditor.RootFolder";
        private static readonly string DefaultRootFolder = "Assets/ConfigData";

        private const string PrefOutputDir = "HYC.Framework.Config.DataEditor.OutputDir";
        private static readonly string DefaultOutputDir = "Assets/GeneratedConfigs";

        private const string PrefNamespace = "HYC.Framework.Config.DataEditor.Namespace";
        private static readonly string DefaultNamespace = "HYC.Sample.Generated";

        private const string PrefEditorDir = "HYC.Framework.Config.DataEditor.EditorDir";
        private static readonly string DefaultEditorDir = "Assets/GeneratedConfigs/Editor";

        private const string PrefClientExportDir = "HYC.Framework.Config.DataEditor.ClientExportDir";
        private static readonly string DefaultClientExportDir = "Exports/Client";

        private const string PrefServerExportDir = "HYC.Framework.Config.DataEditor.ServerExportDir";
        private static readonly string DefaultServerExportDir = "Exports/Server";

        private const string PrefSideTypeIsStruct = "HYC.Framework.Config.DataEditor.SideTypeIsStruct";

        private const string PrefClientFormat = "HYC.Framework.Config.DataEditor.ClientFormat";
        private const string PrefServerFormat = "HYC.Framework.Config.DataEditor.ServerFormat";

        private const string PrefBlobOutputDir = "HYC.Framework.Config.DataEditor.BlobOutputDir";
        private static readonly string DefaultBlobOutputDir = "Assets/StreamingAssets/ConfigBlob";

        private const string PrefBlobLoadPath = "HYC.Framework.Config.DataEditor.BlobLoadPath";
        private static readonly string DefaultBlobLoadPath = "ConfigBlob";

        private const string PrefIdProviderType = "HYC.Framework.Config.DataEditor.IdProviderType";
        private const string PrefIdServerIp = "HYC.Framework.Config.DataEditor.IdServerIp";
        private const string PrefIdServerPort = "HYC.Framework.Config.DataEditor.IdServerPort";

        /// <summary>ID 构造器类型：0=本地，1=局域网。</summary>
        public static int IdProviderType
        {
            get => EditorPrefs.GetInt(PrefIdProviderType, 0);
            set => EditorPrefs.SetInt(PrefIdProviderType, value);
        }

        /// <summary>局域网 ID 服务器 IP。</summary>
        public static string IdServerIp
        {
            get
            {
                var ip = EditorPrefs.GetString(PrefIdServerIp, "127.0.0.1");
                return string.IsNullOrEmpty(ip) ? "127.0.0.1" : ip;
            }
            set => EditorPrefs.SetString(PrefIdServerIp, value);
        }

        /// <summary>局域网 ID 服务器端口。</summary>
        public static int IdServerPort
        {
            get
            {
                var p = EditorPrefs.GetInt(PrefIdServerPort, 8920);
                return p < 1 || p > 65535 ? 8920 : p;
            }
            set => EditorPrefs.SetInt(PrefIdServerPort, value);
        }

        /// <summary>客户端导出格式：0=JSON, 1=Blob。</summary>
        public static int ClientFormat
        {
            get => EditorPrefs.GetInt(PrefClientFormat, 0);
            set => EditorPrefs.SetInt(PrefClientFormat, value);
        }

        /// <summary>服务器导出格式：0=JSON。</summary>
        public static int ServerFormat
        {
            get => EditorPrefs.GetInt(PrefServerFormat, 0);
            set => EditorPrefs.SetInt(PrefServerFormat, value);
        }

        /// <summary>Blob 文件输出目录（编辑器导出位置，StreamingAssets 内以便打包）。</summary>
        public static string BlobOutputDir
        {
            get
            {
                var dir = EditorPrefs.GetString(PrefBlobOutputDir, DefaultBlobOutputDir);
                return string.IsNullOrEmpty(dir) ? DefaultBlobOutputDir : dir;
            }
            set => EditorPrefs.SetString(PrefBlobOutputDir, value);
        }

        /// <summary>Blob 文件运行时加载子路径（相对 StreamingAssets）。</summary>
        public static string BlobLoadPath
        {
            get
            {
                var p = EditorPrefs.GetString(PrefBlobLoadPath, DefaultBlobLoadPath);
                return string.IsNullOrEmpty(p) ? DefaultBlobLoadPath : p;
            }
            set => EditorPrefs.SetString(PrefBlobLoadPath, value);
        }

        /// <summary>自定义配置编辑器（XxxConfigEditor.cs）的生成目录。</summary>
        public static string EditorDir
        {
            get
            {
                var dir = EditorPrefs.GetString(PrefEditorDir, DefaultEditorDir);
                return string.IsNullOrEmpty(dir) ? DefaultEditorDir : dir;
            }
            set => EditorPrefs.SetString(PrefEditorDir, value);
        }

        /// <summary>客户端导出目录（绝对路径或项目相对路径）。</summary>
        public static string ClientExportDir
        {
            get
            {
                var dir = EditorPrefs.GetString(PrefClientExportDir, DefaultClientExportDir);
                return string.IsNullOrEmpty(dir) ? DefaultClientExportDir : dir;
            }
            set => EditorPrefs.SetString(PrefClientExportDir, value);
        }

        /// <summary>服务器导出目录（绝对路径或项目相对路径）。</summary>
        public static string ServerExportDir
        {
            get
            {
                var dir = EditorPrefs.GetString(PrefServerExportDir, DefaultServerExportDir);
                return string.IsNullOrEmpty(dir) ? DefaultServerExportDir : dir;
            }
            set => EditorPrefs.SetString(PrefServerExportDir, value);
        }

        /// <summary>客户端/服务器导出类型：true = struct（字段平铺），false = class（保持继承链）。</summary>
        public static bool SideTypeIsStruct
        {
            get => EditorPrefs.GetBool(PrefSideTypeIsStruct, false);
            set => EditorPrefs.SetBool(PrefSideTypeIsStruct, value);
        }

        /// <summary>Root folder (project-relative, e.g. Assets/ConfigData) scanned by the data editor tree.</summary>
        public static string RootFolder
        {
            get
            {
                var folder = EditorPrefs.GetString(PrefRootFolder, DefaultRootFolder);
                return string.IsNullOrEmpty(folder) ? DefaultRootFolder : folder;
            }
            set => EditorPrefs.SetString(PrefRootFolder, value);
        }

        /// <summary>Output folder for generated config classes.</summary>
        public static string OutputDir
        {
            get
            {
                var dir = EditorPrefs.GetString(PrefOutputDir, DefaultOutputDir);
                return string.IsNullOrEmpty(dir) ? DefaultOutputDir : dir;
            }
            set => EditorPrefs.SetString(PrefOutputDir, value);
        }

        /// <summary>Namespace used by generated config classes.</summary>
        public static string Namespace
        {
            get
            {
                var ns = EditorPrefs.GetString(PrefNamespace, DefaultNamespace);
                return string.IsNullOrEmpty(ns) ? DefaultNamespace : ns;
            }
            set => EditorPrefs.SetString(PrefNamespace, value);
        }

        /// <summary>Creates the root folder (and missing parents) on disk, then refreshes the asset database.</summary>
        public static void EnsureRootFolder()
        {
            var folder = RootFolder;
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var normalized = folder.Replace('\\', '/').Trim('/');
            var parts = normalized.Split('/');
            var current = parts[0];
            if (!AssetDatabase.IsValidFolder(current))
                AssetDatabase.CreateFolder("Assets", parts[0]);

            for (var i = 1; i < parts.Length; i++)
            {
                var parent = current;
                current = parent + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(current))
                    AssetDatabase.CreateFolder(parent, parts[i]);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>Returns a locally-unique positive ID (timestamp seed + per-session counter).</summary>
        public static long NextId()
        {
            _counter++;
            return unchecked(_seed + _counter);
        }

        private static readonly long _seed = Math.Abs(DateTime.UtcNow.Ticks);
        private static long _counter;

        /// <summary>Absolute OS path for the root folder, or null when it does not exist yet.</summary>
        public static string GetAbsoluteRootFolder()
        {
            EnsureRootFolder();
            var folder = RootFolder;
            if (!AssetDatabase.IsValidFolder(folder))
                return null;
            return Path.Combine(Directory.GetCurrentDirectory(), folder.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
