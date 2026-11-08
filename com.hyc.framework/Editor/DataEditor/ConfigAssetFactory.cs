using System;
using System.Collections.Generic;
using System.IO;
using HYC.Framework.Config;
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
            ConfigAssetLayout.Register(fullPath, type);
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
                    ConfigAssetLayout.Register(p, type);
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

        /// <summary>
        /// 创建配置模板资产（<see cref="ConfigTemplate"/>）。模板是框架基础设施、无 ID/GUID
        /// 字段（不参与发号服），故不走 <see cref="Create(Type, string, string)"/> 的派发流程
        /// （对模板调 <c>Create</c>/<c>CreateMany</c> 会因派发失败而整体回滚）。
        /// 本方法是模板创建的单模板入口，数据树 / 代码生成器 / Excel 导入均经此创建。
        /// 一次性建很多模板请走 <see cref="CreateTemplates"/>（只做一次 Save/Refresh，开销更低）。
        /// <para>folder 为空时自动归置到「配置模板」类型文件夹（根目录/配置模板，见
        /// <see cref="ConfigAssetLayout"/>）；显式传入时尊重调用方。</para>
        /// <para>文件名由 displayName 净化而来（与模板改名同步逻辑同源），冲突自动加序号；
        /// 默认自动挂框架级 ConfigBase 基类模板（保证生成的配置类带 ID/GUID）。</para>
        /// </summary>
        public static ConfigTemplate CreateTemplate(string displayName, string className = null, string folder = null)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("displayName 不能为空", nameof(displayName));

            var targetFolder = string.IsNullOrEmpty(folder)
                ? (ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder)
                : folder;
            EnsureFolder(targetFolder);

            // 文件名用 displayName（净化后），保证对象选择器里可区分；冲突自动加序号
            var assetName = ConfigTemplateCodeGen.DisplayNameToAssetName(displayName);
            var path = Path.Combine(targetFolder, assetName + ".asset");
            var index = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(targetFolder, $"{assetName} {index}.asset");
                index++;
            }

            var tpl = ScriptableObject.CreateInstance<ConfigTemplate>();
            tpl.displayName = displayName.Trim();
            tpl.className = string.IsNullOrWhiteSpace(className) ? "NewConfig" : className.Trim();
            // 默认基类 = ConfigBase（框架级，保证所有配置带 ID/GUID）
            tpl.baseTemplate = ConfigTemplateCodeGen.GetConfigBaseTemplate();
            AssetDatabase.CreateAsset(tpl, path);
            ConfigAssetLayout.Register(path, typeof(ConfigTemplate));
            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return tpl;
        }

        /// <summary>
        /// 批量创建模板资产（AI / 脚本一次性建很多模板的高性能入口）。
        /// 与 <see cref="CreateTemplate"/> 行为一致（归置到配置模板文件夹、基类默认 ConfigBase、
        /// 文件名净化 + 冲突自增），但全程只做 <b>一次</b> <c>SaveAssets</c> + <b>一次</b>
        /// <c>Refresh</c>（单模板版每个都 Save/Refresh，N 个会放大 N 倍开销）。
        /// <para>
        /// 任一条规格失败 → 整体回滚已创建资产（与 <see cref="CreateMany"/> 同策略）。
        /// 每条规格可带 <see cref="TemplateFieldSpec"/> 直接把字段建好，建完即可走
        /// <c>ConfigTemplateCodeGen.GenerateTemplates</c> 批量生成代码。
        /// </para>
        /// </summary>
        public static ConfigTemplate[] CreateTemplates(IEnumerable<TemplateSpec> specs)
        {
            if (specs == null)
                throw new ArgumentNullException(nameof(specs));

            var list = new List<TemplateSpec>();
            foreach (var s in specs)
                list.Add(s);
            if (list.Count == 0)
                return new ConfigTemplate[0];

            var targetFolder = string.IsNullOrEmpty(list[0].folder)
                ? (ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder)
                : list[0].folder;
            EnsureFolder(targetFolder);

            var defaultBase = ConfigTemplateCodeGen.GetConfigBaseTemplate();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var created = new List<ConfigTemplate>(list.Count);

            try
            {
                foreach (var spec in list)
                {
                    if (spec == null || string.IsNullOrWhiteSpace(spec.displayName))
                        throw new InvalidOperationException("TemplateSpec.displayName 不能为空。");

                    var assetName = ConfigTemplateCodeGen.DisplayNameToAssetName(spec.displayName);
                    var path = UniqueTemplatePath(targetFolder, assetName, usedNames);

                    var tpl = ScriptableObject.CreateInstance<ConfigTemplate>();
                    tpl.displayName = spec.displayName.Trim();
                    tpl.className = string.IsNullOrWhiteSpace(spec.className) ? "NewConfig" : spec.className.Trim();
                    tpl.baseTemplate = spec.baseTemplate ?? defaultBase;
                    tpl.unique = spec.unique;
                    if (spec.fields != null)
                    {
                        foreach (var fs in spec.fields)
                        {
                            if (fs == null || string.IsNullOrWhiteSpace(fs.name))
                                continue;
                            tpl.fields.Add(new ConfigTemplateField
                            {
                                name = fs.name,
                                description = fs.displayName ?? "",
                                type = fs.type,
                                isList = fs.isList,
                                refTypeFullName = fs.refTypeFullName ?? "",
                                enumRefClassName = fs.enumRefClassName ?? "",
                            });
                        }
                    }

                    AssetDatabase.CreateAsset(tpl, path);
                    ConfigAssetLayout.Register(path, typeof(ConfigTemplate));
                    created.Add(tpl);
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

            // 全部成功：一次性落盘 + 刷新（关键性能点）
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return created.ToArray();
        }

        // ---------- 内部 ----------

        /// <summary>在 folder 下求一个不与磁盘及本批次已用名冲突的 .asset 路径（冲突自增后缀）。</summary>
        private static string UniqueTemplatePath(string folder, string assetName, HashSet<string> usedNames)
        {
            for (var suffix = 0; ; suffix++)
            {
                var name = suffix == 0 ? assetName : $"{assetName} {suffix}";
                var path = Path.Combine(folder, name + ".asset");
                if (!File.Exists(path) && !usedNames.Contains(name))
                {
                    usedNames.Add(name);
                    return path;
                }
            }
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

        /// <summary>
        /// 类型本体是否声明为唯一配置。只读<b>本体直接标注</b>的特性（<c>inherit: false</c>）——
        /// 代码生成器给每个生成类无条件写一条 <c>[CfgAsset]</c>，故本体必定有。
        /// <para>
        /// 不能用单数 <c>Attribute.GetCustomAttribute(..., inherit: true)</c>：特性声明为
        /// <c>AllowMultiple = true</c> + <c>Inherited = true</c>，模板继承模板时继承链上会有
        /// ≥2 条，单数版在反射读取阶段直接抛 <see cref="System.Reflection.AmbiguousMatchException"/>
        /// （崩在读取而不是取值，与 <see cref="CfgAssetAttribute.Unique"/> 是 true 还是 false 无关）。
        /// </para>
        /// </summary>
        private static bool IsUniqueType(Type type)
        {
            var attrs = type.GetCustomAttributes(typeof(CfgAssetAttribute), false);
            return attrs.Length > 0 && ((CfgAssetAttribute)attrs[0]).Unique;
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

    /// <summary>
    /// 批量创建时单个模板的规格。显示名必填；类名 / 文件夹 / 字段等均可选。
    /// <para>字段用 <see cref="TemplateFieldSpec"/> 描述，建模板时一并写入，省去后续逐条回填。</para>
    /// </summary>
    public sealed class TemplateSpec
    {
        public string displayName;
        public string className;
        public string folder;
        public bool unique;
        public ConfigTemplate baseTemplate;
        public List<TemplateFieldSpec> fields;
    }

    /// <summary>批量建模板时单个字段的描述（映射到 <c>ConfigTemplateField</c> 的核心字段）。</summary>
    public sealed class TemplateFieldSpec
    {
        public string name;
        public string displayName;
        public ConfigFieldType type = ConfigFieldType.String;
        public bool isList;
        public string refTypeFullName;
        public string enumRefClassName;
    }
}
