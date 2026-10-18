using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置导出入口：定位并实例化生成的 XxxExport 类（<see cref="ConfigBaseExport"/> 子类，可多级继承；
    /// 先查注册表，未注册才做一次性程序集扫描定位类型），走基类公共入口完成导出。
    /// 支持单个实例导出、文件夹内全部导出。
    /// </summary>
    public static class ConfigExportService
    {
        /// <summary>导出单个配置实例（按运行时类型匹配 XxxConfigExport）。</summary>
        public static bool ExportSingle(UnityEngine.Object asset, bool client, bool server)
        {
            // Unity 的 == null 对「已销毁对象」也返回 true，所以先判空再取属性。
            if (asset == null)
                return false;

            // 下面整条链（CollectConfigAssets / EnsureUniqueBeforeExport）都是 List<ScriptableObject>，
            // 而形参声明是 UnityEngine.Object（历史遗留：允许传任意子类型再在这里判型）。
            // 必须先显式转一次再进集合——Object → ScriptableObject 是向下转型，C# 不允许隐式，
            // 直接 all.Contains(asset) 会 CS1503。
            var so = asset as ScriptableObject;
            if (so == null)
            {
                Debug.LogWarning($"导出失败：{asset.name} 不是 ScriptableObject（类型 {asset.GetType().Name}）");
                return false;
            }

            // 导出前查重：单资产对全根目录扫描（含系统外复制的资产）
            var all = ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder);
            if (!all.Contains(so))
                all.Add(so);
            if (!ConfigIdService.EnsureUniqueBeforeExport(all, "导出单个配置"))
                return false;

            var exportType = FindExportType(so.GetType());
            if (exportType == null)
            {
                Debug.LogWarning($"未找到 {so.GetType().Name} 的导出类（请先生成代码）");
                return false;
            }

            // 生成的 XxxExport 是 ConfigBaseExport 的子类（可多级继承）：实例化后走基类公共入口。
            // 不走静态反射——继承链上同名静态方法会让 GetMethod 产生歧义。
            if (!TryCreateExporter(exportType, so.GetType(), out var exporter))
                return false;

            BuildErrorWindow.BeginExportOperation();
            var ok = exporter.ExportCore(so as ConfigBase, client, server);
            BuildErrorWindow.EndExportOperation($"导出 {so.name}", ok);
            return ok;
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

            BuildErrorWindow.BeginExportOperation();
            foreach (var asset in assets)
            {
                var type = asset.GetType();
                if (done.Add(type))
                {
                    if (ExportAll(type, client, server))
                        exported++;
                }
            }
            // 零检查项时按设置给一次「未发现问题」确认（一次目录导出最多一个框）
            BuildErrorWindow.EndExportOperation($"导出目录 {folder}", exported > 0);

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

        /// <summary>按类型批量导出（实例化生成的 XxxExport 调 <see cref="ConfigBaseExport.ExportAllCore"/>）。</summary>
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

            if (!TryCreateExporter(exportType, configType, out var exporter))
                return false;

            return exporter.ExportAllCore(client, server);
        }

        /// <summary>
        /// 实例化生成的导出类（传入配置类型，基类据此收集资产 / 命名产物文件）。
        /// 旧版生成的 XxxExport 是 <c>static class</c>（IL 层面 abstract+sealed、不可实例化），
        /// 据此识别并提示重新生成；实例化后即可按继承链调用最派生 override。
        /// 公开供 <see cref="ConfigCheckService"/> 复用（检查与导出共用同一套实例化规则）。
        /// </summary>
        public static bool TryCreateExporter(Type exportType, Type configType, out ConfigBaseExport exporter)
        {
            exporter = null;
            if (exportType == null)
                return false;
            if (exportType.IsAbstract)
            {
                Debug.LogWarning($"导出类 {exportType.Name} 不可实例化（旧版静态类？），请执行 Regenerate All Config Code 重新生成");
                return false;
            }
            exporter = Activator.CreateInstance(exportType, configType) as ConfigBaseExport;
            return exporter != null;
        }

        /// <summary>
        /// 全量清理后全量导出。先删除客户端/服务器导出目录里的全部 <c>*.blob</c>（避免改名/删类型后残留幽灵文件），
        /// 再保存全部配置资产并重新导出所有已注册配置类型（每类型一份产物）。
        /// 供面板「全量清理后全量导出」调用。返回成功导出的类型数。
        /// </summary>
        public static int CleanAndExportAll(bool client, bool server)
        {
            // 先落盘所有改动，确保「改了没存」也被导出
            SaveFolder(ConfigDataSettings.RootFolder);

            // 导出前 ID/GUID 查重（与单实例/目录导出路径对齐——全量路径此前漏此关卡）。
            // 必须排在清理旧 blob 之前：用户取消时不能留下"旧产物已删、新产物未写"的空窗。
            if (!ConfigIdService.EnsureUniqueBeforeExport(
                    ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder),
                    "全量清理后全量导出"))
                return 0;

            if (client) ClearBlobDir(ConfigDataSettings.ClientExportDir);
            if (server) ClearBlobDir(ConfigDataSettings.ServerExportDir);

            var types = CollectConfigTypes(ConfigDataSettings.RootFolder);
            var exported = 0;
            BuildErrorWindow.BeginExportOperation();
            foreach (var t in types)
                if (ExportAll(t, client, server))
                    exported++;

            // 零检查项时按设置给一次「未发现问题」确认（逐类型导出会命中标记，不会弹 N 个框）
            BuildErrorWindow.EndExportOperation("全量清理后全量导出",
                types.Count > 0 && exported == types.Count);

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

        /// <summary>
        /// 导出类注册表：配置类型名 → 生成的 <c>XxxExport</c> 类型。
        /// 由生成的 <c>XxxExport.cs</c> 在静态构造里 <see cref="Register"/> 填入（零反射命中）。
        /// </summary>
        private static readonly Dictionary<string, Type> s_exportTypes = new Dictionary<string, Type>();

        /// <summary>是否已做过一次全程序集扫描（一个域会话内只做一次，之后全部走字典）。</summary>
        private static bool s_scannedAllAssemblies;

        /// <summary>
        /// 注册导出类。生成的 <c>XxxExport.cs</c> 静态构造中调用，使 <see cref="FindExportType"/>
        /// 无需任何程序集扫描即可命中。键取配置类型的短名——注册表只在
        /// <see cref="ConfigDataSettings.Namespace"/> 这一个命名空间内使用，不会与其它工程冲突。
        /// </summary>
        public static void Register(Type configType, Type exportType)
        {
            if (configType == null || exportType == null)
                return;
            s_exportTypes[configType.Name] = exportType;
        }

        /// <summary>
        /// 按类型查找生成的 XxxConfigExport（命名空间 {ns}.Editor）。
        /// 优先命中注册表（零反射）；未注册时**整个域会话只做一次**全程序集扫描并缓存结果，
        /// 之后重复查询直接读字典——避免逐类型反复扫全部程序集类型（<c>CleanAndExportAll</c> 会逐类型调用）。
        /// 扫描必须按 <see cref="Type.FullName"/> 全名匹配：只比短名会命中其它工程/命名空间下的同名类并静默导出错表。
        /// </summary>
        public static Type FindExportType(Type configType)
        {
            if (configType == null)
                return null;
            if (s_exportTypes.TryGetValue(configType.Name, out var registered))
                return registered;
            if (s_scannedAllAssemblies)
                return null;

            s_scannedAllAssemblies = true;
            var ns = ConfigDataSettings.Namespace;
            var prefix = $"{ns}.Editor.";
            var suffix = "Export";
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }
                catch (Exception) { continue; }
                foreach (var t in types)
                {
                    var full = t?.FullName;
                    if (string.IsNullOrEmpty(full) || !full.StartsWith(prefix) || !full.EndsWith(suffix))
                        continue;
                    var key = full.Substring(prefix.Length, full.Length - prefix.Length - suffix.Length);
                    if (key.Length > 0 && !s_exportTypes.ContainsKey(key))
                        s_exportTypes[key] = t;
                }
            }
            s_exportTypes.TryGetValue(configType.Name, out var found);
            return found;
        }
    }
}
