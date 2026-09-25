using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// 保存文件夹下所有配置资产（递归）。先 <c>SetDirty</c> 再 <c>SaveAssets</c>，
        /// 跳过 <see cref="ConfigTemplate"/> 与本命名空间之外的 ScriptableObject。
        /// 供「保存并导出」在导出前确保所有改动已落盘。
        /// </summary>
        public static void SaveFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return;

            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
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

                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
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

        /// <summary>
        /// 全量清理后全量导出。先删除客户端/服务器导出目录里的全部 <c>*.blob</c>（避免改名/删类型后残留幽灵文件），
        /// 再保存全部配置资产并重新导出所有已注册配置类型（每类型一份产物）。
        /// 供面板「全量清理后全量导出」调用。返回成功导出的类型数。
        /// </summary>
        public static int CleanAndExportAll(bool client, bool server)
        {
            if (client) ClearBlobDir(ConfigDataSettings.ClientExportDir);
            if (server) ClearBlobDir(ConfigDataSettings.ServerExportDir);

            // 先落盘所有改动，确保「改了没存」也被导出
            SaveFolder(ConfigDataSettings.RootFolder);

            var types = CollectConfigTypes(ConfigDataSettings.RootFolder);
            var exported = 0;
            foreach (var t in types)
                if (ExportAll(t, client, server))
                    exported++;

            AssetDatabase.Refresh();
            Debug.Log($"[Config] 全量清理后全量导出完成：{exported} 类配置 -> 客户端[{ConfigDataSettings.ClientExportDir}] 服务器[{ConfigDataSettings.ServerExportDir}]");
            return exported;
        }

        /// <summary>删除指定目录内的全部 <c>*.blob</c>（不删子目录，避免误伤）。目录不存在则跳过。</summary>
        private static void ClearBlobDir(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            var count = 0;
            foreach (var f in Directory.GetFiles(dir, "*.blob"))
            {
                File.Delete(f);
                count++;
            }
            if (count > 0)
                Debug.Log($"[Config] 已清理 {count} 个旧 blob：{dir}");
        }

        /// <summary>收集根目录下全部配置资产类型（去重，跳过模板/枚举/本命名空间之外）。</summary>
        private static List<Type> CollectConfigTypes(string folder)
        {
            var types = new List<Type>();
            var seen = new HashSet<Type>();
            if (!AssetDatabase.IsValidFolder(folder)) return types;

            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;
                var type = asset.GetType();
                if (type == typeof(ConfigTemplate)) continue;
                if (type.Namespace == null || !type.Namespace.StartsWith(ConfigDataSettings.Namespace)) continue;
                if (seen.Add(type)) types.Add(type);
            }
            return types;
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
