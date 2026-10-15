using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置资产唯一创建入口（"万能 AI 接口"）。
    ///
    /// 任何代码 / AI 创建配置资产都必须走这里，确保 {ID, GUID} 一定由
    /// <see cref="ConfigIdService"/>（发号服）派发；禁止直接
    /// <c>ScriptableObject.CreateInstance</c> + 手填 <c>ID</c>（见仓库 AGENTS.md）。
    ///
    /// 用法：
    /// <code>
    /// var a = ConfigAssetFactory.Create&lt;MyConfig&gt;("Boss01");   // 泛型
    /// var b = ConfigAssetFactory.Create(typeof(MyConfig), "Boss02"); // 非泛型
    /// var list = ConfigAssetFactory.CreateMany(typeof(MyConfig), "Mob", 10); // 批量
    /// // 之后填字段 → EditorUtility.SetDirty(a); AssetDatabase.SaveAssets();
    /// </code>
    /// </summary>
    public static class ConfigAssetFactory
    {
        /// <summary>
        /// 创建单个配置资产并派发 ID。
        /// <para>folder 为空时自动归置到「类型文件夹」（根目录/类型短名，没有则创建，
        /// 见 <see cref="ConfigAssetLayout"/>）；显式传入 folder 时尊重调用方。</para>
        /// </summary>
        public static T Create<T>(string assetName, string folder = null) where T : ScriptableObject
        {
            return (T)Create(typeof(T), assetName, folder);
        }

        /// <summary>
        /// 非泛型创建（AI 不一定知道编译期类型）。返回带合法 ID 的资产；
        /// 失败（发号服不可用 / 唯一类型已存在 / 文件冲突）抛 <see cref="InvalidOperationException"/>。
        /// </summary>
        public static ScriptableObject Create(Type type, string assetName, string folder = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException($"类型 {type.Name} 不是 ScriptableObject", nameof(type));
            if (string.IsNullOrEmpty(assetName))
                throw new ArgumentException("assetName 不能为空", nameof(assetName));

            var targetFolder = string.IsNullOrEmpty(folder)
                ? (ConfigAssetLayout.EnsureTypeFolder(type) ?? ConfigDataSettings.RootFolder)
                : folder;
            EnsureFolder(targetFolder);

            if (IsUniqueType(type) && HasExistingUnique(type, targetFolder))
                throw new InvalidOperationException($"类型 {type.Name} 为唯一配置（[CfgAsset(Unique=true)]），项目中已存在，禁止重复创建。");

            var fullPath = Path.Combine(targetFolder, SanitizeFileName(assetName) + ".asset");
            if (File.Exists(fullPath))
                throw new InvalidOperationException($"文件已存在：{fullPath}");

            var asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, fullPath);
            if (!AssignNewId(asset))
            {
                AssetDatabase.DeleteAsset(fullPath);
                throw new InvalidOperationException($"为 {type.Name} 派发 ID 失败（发号服不可用？该类型是否缺少 ID/GUID 字段？）。资产已回滚。");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ConfigIconApplier.ApplyForAsset(asset, ConfigIconApplier.FindTemplateForType(type));
            return asset;
        }

        /// <summary>
        /// 批量创建：<c>prefix_001 … prefix_NNN</c>，逐个派发 ID；任一失败整体回滚已创建资产。
        /// 唯一类型不支持批量（直接抛异常）。
        /// <para>folder 为空时自动归置到「类型文件夹」（同 <see cref="Create(Type, string, string)"/>）。</para>
        /// </summary>
        public static IReadOnlyList<ScriptableObject> CreateMany(Type type, string prefix, int count, string folder = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "count 必须 >= 1");
            if (string.IsNullOrEmpty(prefix))
                prefix = type.Name;

            if (IsUniqueType(type))
                throw new InvalidOperationException($"类型 {type.Name} 为唯一配置，不支持批量创建。");

            var targetFolder = string.IsNullOrEmpty(folder)
                ? (ConfigAssetLayout.EnsureTypeFolder(type) ?? ConfigDataSettings.RootFolder)
                : folder;
            EnsureFolder(targetFolder);

            var digits = Math.Max(1, count.ToString().Length);
            var paths = new List<string>(count);
            for (var i = 1; i <= count; i++)
                paths.Add(Path.Combine(targetFolder, $"{SanitizeFileName(prefix)}_{i.ToString("D" + digits)}.asset"));

            foreach (var p in paths)
                if (File.Exists(p))
                    throw new InvalidOperationException($"文件已存在：{p}");

            var created = new List<ScriptableObject>(count);
            try
            {
                foreach (var p in paths)
                {
                    var a = ScriptableObject.CreateInstance(type);
                    AssetDatabase.CreateAsset(a, p);
                    created.Add(a);
                }

                foreach (var a in created)
                {
                    if (!AssignNewId(a))
                        throw new InvalidOperationException($"为 {type.Name} 派发 ID 失败，批量创建整体回滚。");
                }
            }
            catch
            {
                foreach (var a in created)
                {
                    var p = AssetDatabase.GetAssetPath(a);
                    if (!string.IsNullOrEmpty(p))
                        AssetDatabase.DeleteAsset(p);
                }
                throw;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var iconTemplate = ConfigIconApplier.FindTemplateForType(type);
            foreach (var a in created)
                ConfigIconApplier.ApplyForAsset(a, iconTemplate);
            return created;
        }

        // ---------- 内部 ----------

        /// <summary>从当前 ID 构造器获取 {id,guid} 并写入资产。失败返回 false（不弹窗，抛给调用方决定）。</summary>
        private static bool AssignNewId(ScriptableObject asset)
        {
            if (asset == null)
                return false;
            ConfigId cid;
            try
            {
                cid = ConfigIdService.RequestIdSync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigAssetFactory] 派发 ID 失败：{e.Message}");
                return false;
            }
            return ConfigIdService.TryWriteId(asset, cid);
        }

        private static bool IsUniqueType(Type type)
        {
            var attr = (CfgAssetAttribute)Attribute.GetCustomAttribute(type, typeof(CfgAssetAttribute), true);
            return attr != null && attr.Unique;
        }

        private static bool HasExistingUnique(Type type, string folder)
        {
            foreach (var g in AssetDatabase.FindAssets($"t:{type.Name}", new[] { folder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var main = AssetDatabase.GetMainAssetTypeAtPath(p);
                if (type.IsAssignableFrom(main))
                    return true;
            }
            return false;
        }

        /// <summary>逐级创建目录（必须在 Assets 下）；已存在则跳过。</summary>
        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace("\\", "/").Trim('/');
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"配置目录必须在 Assets 下：{folder}", nameof(folder));
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var parts = folder.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var s = name.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c.ToString(), "_");
            return string.IsNullOrEmpty(s) ? "Config" : s;
        }
    }
}
