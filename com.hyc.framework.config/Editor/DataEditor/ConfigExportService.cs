using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置导出入口：通过反射调用生成的 XxxConfigExport 类。
    /// 支持单个实例导出、文件夹内全部导出。
    /// </summary>
    public static class ConfigExportService
    {
        /// <summary>导出单个配置实例（按运行时类型匹配 XxxConfigExport）。</summary>
        public static bool ExportSingle(UnityEngine.Object asset, bool client, bool server)
        {
            if (asset == null)
                return false;

            // 导出前查重：单资产对全根目录扫描（含系统外复制的资产）
            var all = ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder);
            if (!all.Contains(asset))
                all.Add(asset as ScriptableObject);
            if (!ConfigIdService.EnsureUniqueBeforeExport(all, "导出单个配置"))
                return false;

            var exportType = FindExportType(asset.GetType());
            if (exportType == null)
            {
                Debug.LogWarning($"未找到 {asset.GetType().Name} 的导出类（请先生成代码）");
                return false;
            }

            var m = exportType.GetMethod("Export", BindingFlags.Public | BindingFlags.Static);
            if (m == null)
                return false;

            return (bool)m.Invoke(null, new object[] { asset, client, server });
        }

        /// <summary>导出文件夹下所有配置资产（每类型批量导出，一类型一份产物）。</summary>
        public static int ExportFolder(string folder, bool client, bool server)
        {
            var exported = 0;
            var assetPaths = AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder });
            var assets = new List<ScriptableObject>();
            var done = new HashSet<Type>();

            foreach (var guid in assetPaths)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                    continue;

                var type = asset.GetType();
                if (type == typeof(ConfigTemplate))
                    continue;
                if (type.Namespace == null || !type.Namespace.StartsWith(ConfigDataSettings.Namespace))
                    continue;

                assets.Add(asset);
            }

            // 导出前查重（重复/未分配 → 弹窗重新派发）
            if (!ConfigIdService.EnsureUniqueBeforeExport(assets, $"导出目录 {folder}"))
                return 0;

            foreach (var asset in assets)
            {
                var type = asset.GetType();
                if (done.Add(type))
                {
                    if (ExportAll(type, client, server))
                        exported++;
                }
            }

            return exported;
        }

        /// <summary>按类型批量导出（调用生成的 XxxConfigExport.ExportAll）。</summary>
        public static bool ExportAll(Type configType, bool client, bool server)
        {
            if (configType == null)
                return false;

            var exportType = FindExportType(configType);
            if (exportType == null)
            {
                Debug.LogWarning($"未找到 {configType.Name} 的导出类（请先生成代码）");
                return false;
            }

            var m = exportType.GetMethod("ExportAll", BindingFlags.Public | BindingFlags.Static);
            if (m == null)
                return false;

            return (bool)m.Invoke(null, new object[] { client, server });
        }

        /// <summary>按类型查找生成的 XxxConfigExport（命名空间 {ns}.Editor）。</summary>
        public static Type FindExportType(Type configType)
        {
            if (configType == null)
                return null;
            var ns = ConfigDataSettings.Namespace;
            var fullName = $"{ns}.Editor.{configType.Name}Export";
            var type = Type.GetType(fullName + ", Assembly-CSharp-Editor");
            if (type == null)
            {
                // 全程序集扫描兜底
                type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t != null && t.Name == configType.Name + "Export");
            }
            return type;
        }
    }
}
