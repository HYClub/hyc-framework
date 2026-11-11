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
    /// <para>
    /// 批量建模板（含字段 / 默认值 / 只读，AI 直接照抄）：
    /// <code>
    /// ConfigAssetFactory.CreateTemplates(
    ///     ConfigAssetFactory.Template("角色/英雄配置", "HeroConfig",
    ///         TemplateFieldSpec.Int("maxHp", 100).Desc("最大生命"),
    ///         TemplateFieldSpec.Float("moveSpeed", 3.5f),
    ///         TemplateFieldSpec.Bool("isBoss", false).ReadOnly(),
    ///         TemplateFieldSpec.Text("heroName", "新英雄"),
    ///         TemplateFieldSpec.Enum("heroType", "HeroType", "Warrior")),
    ///     ConfigAssetFactory.Template("道具/道具配置", "ItemConfig",
    ///         TemplateFieldSpec.Int("price", 10),
    ///         TemplateFieldSpec.Of("icon", ConfigFieldType.Sprite)));
    ///
    /// // 给已存在的模板追加字段（同一套规格）：
    /// ConfigAssetFactory.AddFields(tpl, TemplateFieldSpec.Bool("enabled", true));
    ///
    /// // 幂等创建 / 创建即填值 / 自定义名字批量：
    /// var boss = ConfigAssetFactory.CreateOrGet&lt;BossConfig&gt;("Boss01");
    /// var mob = ConfigAssetFactory.CreateWith&lt;MobConfig&gt;("Mob01", c =&gt; c.hp = 50);
    /// var named = ConfigAssetFactory.CreateMany(typeof(MobConfig), new[] { "Slime", "Bat" });
    ///
    /// // 建模板 + 一步生成代码：
    /// var r = ConfigAssetFactory.CreateTemplatesAndGenerate(
    ///     ConfigAssetFactory.Template("道具/道具配置", "ItemConfig",
    ///         TemplateFieldSpec.Int("price", 10)));
    /// </code>
    /// 静态工厂按字段类型自动写默认值槽；枚举默认值写成员名即可，工厂换算成数值。
    /// 建完走 <c>ConfigTemplateCodeGen.GenerateTemplates</c> 生成代码。
    /// </para>
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
            ValidateBatchType(type);

            var digits = Math.Max(1, count.ToString().Length);
            var names = new List<string>(count);
            for (var i = 1; i <= count; i++)
                names.Add($"{SanitizeFileName(prefix)}_{i.ToString("D" + digits)}");
            return CreateManyCore(type, names, folder);
        }

        /// <summary>
        /// 批量创建（自定义资产名）：一次给一批名字，逐个派发 ID，任一失败整体回滚。
        /// 名字空白 / 重复直接报错。与前缀版共用同一套回滚策略。
        /// </summary>
        public static IReadOnlyList<ScriptableObject> CreateMany(Type type, IEnumerable<string> names, string folder = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (names == null)
                throw new ArgumentNullException(nameof(names));
            ValidateBatchType(type);

            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n))
                    throw new ArgumentException("资产名不能为空或空白", nameof(names));
                var clean = SanitizeFileName(n);
                if (!seen.Add(clean))
                    throw new InvalidOperationException($"批量创建的资产名重复：{clean}");
                list.Add(clean);
            }
            if (list.Count == 0)
                return new ScriptableObject[0];
            return CreateManyCore(type, list, folder);
        }

        /// <summary>泛型版批量创建：AI 已知编译期类型时省去逐条转型。</summary>
        public static IReadOnlyList<T> CreateMany<T>(string prefix, int count, string folder = null) where T : ScriptableObject
        {
            var made = CreateMany(typeof(T), prefix, count, folder);
            var typed = new List<T>(made.Count);
            foreach (var a in made)
                typed.Add((T)a);
            return typed;
        }

        /// <summary>
        /// 幂等创建：同名资产已存在则直接复用（不重新派发 ID），否则按 <see cref="Create{T}(string, string)"/> 创建。
        /// AI 可重复执行同一段脚本，不会因「文件已存在」中断。
        /// </summary>
        public static T CreateOrGet<T>(string assetName, string folder = null) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(assetName))
                throw new ArgumentException("assetName 不能为空", nameof(assetName));
            var type = typeof(T);
            var targetFolder = string.IsNullOrEmpty(folder)
                ? (ConfigAssetLayout.EnsureTypeFolder(type) ?? ConfigDataSettings.RootFolder)
                : folder;
            EnsureFolder(targetFolder);
            var fullPath = Path.Combine(targetFolder, SanitizeFileName(assetName) + ".asset");
            var existing = AssetDatabase.LoadAssetAtPath(fullPath, type) as T;
            return existing != null ? existing : Create<T>(assetName, folder);
        }

        /// <summary>
        /// 创建并立即初始化：工厂内完成填值 + SetDirty + SaveAssets，AI 不用记这三步。
        /// 初始化回调抛异常时删除刚创建的资产再抛出（不留下半初始化资产）。
        /// </summary>
        public static T CreateWith<T>(string assetName, Action<T> initialize, string folder = null) where T : ScriptableObject
        {
            if (initialize == null)
                throw new ArgumentNullException(nameof(initialize));
            var asset = Create<T>(assetName, folder);
            try
            {
                initialize(asset);
            }
            catch
            {
                var p = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(p))
                    AssetDatabase.DeleteAsset(p);
                throw;
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void ValidateBatchType(Type type)
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException($"类型 {type.Name} 不是 ScriptableObject", nameof(type));
            if (IsUniqueType(type))
                throw new InvalidOperationException($"类型 {type.Name} 为唯一配置，不支持批量创建。");
        }

        /// <summary>
        /// 批量创建核心：路径预检 → 逐个建资产 → 逐个派发 ID → 任一失败整体删除回滚 → 一次 Save/Refresh。
        /// 前缀版与自定义名字版共用，保证两条入口行为一致。
        /// </summary>
        private static IReadOnlyList<ScriptableObject> CreateManyCore(Type type, List<string> assetNames, string folder)
        {
            var targetFolder = string.IsNullOrEmpty(folder)
                ? (ConfigAssetLayout.EnsureTypeFolder(type) ?? ConfigDataSettings.RootFolder)
                : folder;
            EnsureFolder(targetFolder);

            var paths = new List<string>(assetNames.Count);
            foreach (var name in assetNames)
                paths.Add(Path.Combine(targetFolder, name + ".asset"));
            foreach (var p in paths)
                if (File.Exists(p))
                    throw new InvalidOperationException($"文件已存在：{p}");

            var created = new List<ScriptableObject>(paths.Count);
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

            var typeFolder = ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder;
            // 批次默认目录 = 第一条规格显式指定的 folder（都没设时全部落类型文件夹），
            // 每条规格仍可用自己的 folder 覆盖；此前只有 list[0].folder 生效，其余被静默忽略。
            var batchFolder = string.IsNullOrEmpty(list[0].folder) ? typeFolder : list[0].folder;
            EnsureFolder(batchFolder);

            var defaultBase = ConfigTemplateCodeGen.GetConfigBaseTemplate();
            // 每个目录各自的已用名集合：不同目录下的同名资产不该互相顶掉
            var usedNamesByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var created = new List<ConfigTemplate>(list.Count);

            try
            {
                foreach (var spec in list)
                {
                    if (spec == null || string.IsNullOrWhiteSpace(spec.displayName))
                        throw new InvalidOperationException("TemplateSpec.displayName 不能为空。");

                    var specFolder = string.IsNullOrEmpty(spec.folder) ? batchFolder : spec.folder;
                    EnsureFolder(specFolder);
                    if (!usedNamesByFolder.TryGetValue(specFolder, out var usedNames))
                    {
                        usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        usedNamesByFolder.Add(specFolder, usedNames);
                    }

                    var assetName = ConfigTemplateCodeGen.DisplayNameToAssetName(spec.displayName);
                    var path = UniqueTemplatePath(specFolder, assetName, usedNames);

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
                            ResolveSelfReference(fs, tpl.className);
                            tpl.fields.Add(BuildField(spec.displayName, fs));
                        }
                    }
                    if (spec.overrides != null && spec.overrides.Count > 0)
                        BuildDefaultOverrides(tpl, spec.displayName, spec.overrides);

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

        /// <summary>
        /// 批量创建的可变参数版：AI / 脚本直接列模板规格即可，不必自己包数组。
        /// 与 <c>CreateTemplates(IEnumerable&lt;TemplateSpec&gt;)</c> 同一套逻辑与回滚策略。
        /// </summary>
        public static ConfigTemplate[] CreateTemplates(params TemplateSpec[] specs)
            => CreateTemplates((IEnumerable<TemplateSpec>)specs);

        /// <summary>
        /// 建模板规格的便捷入口：显示名 + 类名 + 任意多个字段。
        /// 字段用 <see cref="TemplateFieldSpec"/> 的静态工厂按类型创建，默认值按真实类型传入，
        /// 工厂自动写入对应的默认值槽，调用方不需要知道 internal 值槽的存在。
        /// </summary>
        public static TemplateSpec Template(string displayName, string className, params TemplateFieldSpec[] fields)
        {
            return new TemplateSpec
            {
                displayName = displayName,
                className = className,
                fields = fields != null && fields.Length > 0 ? new List<TemplateFieldSpec>(fields) : null,
            };
        }

        /// <summary>
        /// 给已存在的模板批量追加字段（同一套 <see cref="TemplateFieldSpec"/>，支持默认值 / 只读）。
        /// 全部构造并校验通过后才一次性写入：任一失败都不改模板；同名字段直接报错，不静默改名。
        /// 只查本模板自身字段，继承链上的重名由生成代码阶段拦截。
        /// </summary>
        public static int AddFields(ConfigTemplate template, params TemplateFieldSpec[] fields)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (fields == null || fields.Length == 0)
                return 0;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (template.fields != null)
                foreach (var existing in template.fields)
                    if (existing != null)
                        names.Add(existing.name);

            var pending = new List<ConfigTemplateField>();
            foreach (var fs in fields)
            {
                if (fs == null || string.IsNullOrWhiteSpace(fs.name))
                    throw new InvalidOperationException("TemplateFieldSpec.name 不能为空。");
                ResolveSelfReference(fs, template.className);
                var field = BuildField(template.displayName, fs);
                if (!names.Add(field.name))
                    throw new InvalidOperationException($"模板 {template.displayName} 已存在字段 {field.name}，禁止重复添加。");
                pending.Add(field);
            }

            if (template.fields == null)
                template.fields = new List<ConfigTemplateField>();
            template.fields.AddRange(pending);
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            return pending.Count;
        }

        /// <summary>
        /// 单模板 + 字段一步到位：等价于 <c>CreateTemplates(Template(...))</c> 取第一个。
        /// 不叫 CreateTemplate 是为了避开与既有 <c>CreateTemplate(string, string, string)</c> 的 null 二义重载。
        /// </summary>
        public static ConfigTemplate CreateTemplateWithFields(string displayName, string className,
            params TemplateFieldSpec[] fields)
        {
            var made = CreateTemplates(Template(displayName, className, fields));
            return made.Length > 0 ? made[0] : null;
        }

        /// <summary>
        /// 建模板 + 立即批量生成代码（一条龙）：返回模板数组与 <c>GenerateTemplates</c> 的
        /// (成功数, 失败数, 错误汇总)。生成失败不回滚模板资产（资产已合法创建），错误逐条列在 errors 里。
        /// </summary>
        public static (ConfigTemplate[] templates, int ok, int failed, string errors)
            CreateTemplatesAndGenerate(params TemplateSpec[] specs)
        {
            var templates = CreateTemplates(specs);
            var (ok, failed, errors) = ConfigTemplateCodeGen.GenerateTemplates(templates);
            return (templates, ok, failed, errors);
        }

        /// <summary>按生成类名查已有模板（AI 增量改模板的第一步）；找不到返回 null。</summary>
        public static ConfigTemplate FindTemplate(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return null;
            foreach (var t in ConfigTemplateCodeGen.LoadAllTemplates())
                if (t != null && string.Equals(t.className, className, StringComparison.Ordinal))
                    return t;
            return null;
        }

        /// <summary>
        /// 批量删除字段（幂等）：不存在的名字直接跳过，返回实际删除条数。
        /// 只删本模板自身字段；有实际删除才 SetDirty + SaveAssets。
        /// </summary>
        public static int RemoveFields(ConfigTemplate template, params string[] fieldNames)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (template.fields == null || fieldNames == null || fieldNames.Length == 0)
                return 0;

            var toRemove = new HashSet<string>(fieldNames, StringComparer.OrdinalIgnoreCase);
            var before = template.fields.Count;
            template.fields.RemoveAll(f => f != null && toRemove.Contains(f.name));
            var removed = before - template.fields.Count;
            if (removed > 0)
            {
                EditorUtility.SetDirty(template);
                AssetDatabase.SaveAssets();
            }
            return removed;
        }

        // ---------- 内部 ----------

        /// <summary>
        /// 自引用规格 → 引用全名：按「当前命名空间 + 本模板 className」写，不依赖编译类型。
        /// 生成器按模板资产 className 判定引用存在，代码生成后 UI 再按短名找到真实类型。
        /// </summary>
        private static void ResolveSelfReference(TemplateFieldSpec fs, string className)
        {
            if (fs == null)
                return;
            // 两种写法都收敛到同一个关键字解析：SelfReference(name) 的标记位，或 Reference(name, "self")
            var isSelf = fs.selfReference || ConfigTemplateCodeGen.IsSelfRefKeyword(fs.refTypeFullName);
            if (!isSelf)
                return;
            if (string.IsNullOrWhiteSpace(className))
                throw new InvalidOperationException("自引用字段需要模板 className（不能为空）。");
            fs.type = ConfigFieldType.Reference;
            fs.refTypeFullName = ConfigTemplateCodeGen.ResolveRefArgument(
                ConfigTemplateCodeGen.SelfRefKeyword, className);
        }

        /// <summary>
        /// 字段规格 → ConfigTemplateField，并校验默认值。
        /// CreateTemplates 与 AddFields 共用，保证两条入口规则一致；
        /// 校验失败抛异常，CreateTemplates 会整体回滚已创建资产。
        /// </summary>
        private static ConfigTemplateField BuildField(string templateName, TemplateFieldSpec fs)
        {
            var field = new ConfigTemplateField
            {
                name = fs.name.Trim(),
                description = fs.displayName ?? "",
                type = fs.type,
                isList = fs.isList,
                refTypeFullName = fs.refTypeFullName ?? "",
                enumRefClassName = fs.enumRefClassName ?? "",
                readOnly = fs.readOnly,
                useDefault = fs.useDefault,
                defaultNumber = fs.defaultNumber,
                defaultEnumValue = fs.defaultEnumValue,
                defaultReal = fs.defaultReal,
                defaultFlag = fs.defaultFlag,
                defaultText = fs.defaultText ?? "",
            };
            if (!ConfigDefaultValue.TryValidate(field, out var error))
                throw new InvalidOperationException($"模板 {templateName} 的字段 {field.name} 默认值无效：{error}");
            return field;
        }

        /// <summary>
        /// 把「基类字段默认值覆盖」规格写进派生模板：类型恒以基类字段为准，
        /// 规格的类型必须与基类字段一致（防止 AI 用错静态工厂把值写进错误的槽）。
        /// 校验失败抛异常，CreateTemplates 会整体回滚。
        /// </summary>
        private static void BuildDefaultOverrides(ConfigTemplate tpl, string templateName,
            List<TemplateFieldSpec> specs)
        {
            var chain = new List<ConfigTemplate>();
            if (tpl.baseTemplate == null
                || !ConfigTemplateCodeGen.TryBuildInheritanceChain(tpl, chain, out var chainError))
            {
                throw new InvalidOperationException(
                    $"模板 {templateName} 的默认值覆盖：{chainError ?? "没有基类模板，无法覆盖"}");
            }

            var seen = new HashSet<string>();
            foreach (var fs in specs)
            {
                if (fs == null || string.IsNullOrWhiteSpace(fs.name))
                    continue;
                var name = fs.name.Trim();
                if (!seen.Add(name))
                    throw new InvalidOperationException($"模板 {templateName} 的默认值覆盖字段 {name} 重复");

                var baseField = ConfigTemplateCodeGen.FindFieldInChain(chain, name);
                if (baseField == null)
                    throw new InvalidOperationException($"模板 {templateName} 的覆盖目标 {name} 不在基类继承链中");
                if (fs.type != baseField.type || fs.isList != baseField.isList)
                    throw new InvalidOperationException(
                        $"模板 {templateName} 的覆盖字段 {name} 类型与基类不一致（规格 {fs.type}，基类 {baseField.type}）");

                var shadow = new ConfigTemplateField
                {
                    name = name,
                    type = baseField.type,
                    isList = baseField.isList,
                    enumRefClassName = baseField.enumRefClassName ?? "",
                    useDefault = true,
                    defaultNumber = fs.defaultNumber,
                    defaultEnumValue = fs.defaultEnumValue,
                    defaultReal = fs.defaultReal,
                    defaultFlag = fs.defaultFlag,
                    defaultText = fs.defaultText ?? "",
                };
                if (!ConfigDefaultValue.TryValidateOverride(baseField, shadow, out var error))
                    throw new InvalidOperationException($"模板 {templateName} 的字段 {name} 默认值覆盖无效：{error}");
                tpl.baseDefaultOverrides.Add(shadow);
            }
        }

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
    /// <para>字段用 <see cref="TemplateFieldSpec"/> 描述，建模板时一并写入，省去后续逐条回填。
    /// 用 <c>ConfigAssetFactory.Template</c> 构造更省事。</para>
    /// </summary>
    public sealed class TemplateSpec
    {
        public string displayName;
        public string className;
        public string folder;
        public bool unique;
        public ConfigTemplate baseTemplate;
        public List<TemplateFieldSpec> fields;
        /// <summary>基类字段默认值覆盖：用 TemplateFieldSpec 的按类型工厂描述（如 Int("hp", 250)）。</summary>
        public List<TemplateFieldSpec> overrides;

        // ---------- 链式修饰（AI 不用记字段名） ----------

        /// <summary>全工程唯一（Single）：工厂禁止重复创建该类型。</summary>
        public TemplateSpec Unique(bool value = true) { unique = value; return this; }

        /// <summary>指定基类模板；不设则默认挂框架级 ConfigBase（生成的配置类带 ID/GUID）。</summary>
        public TemplateSpec Base(ConfigTemplate template) { baseTemplate = template; return this; }

        /// <summary>该模板的落盘目录（Assets/...）；不设则用批次默认目录（配置模板类型文件夹）。</summary>
        public TemplateSpec InFolder(string path) { folder = path; return this; }

        /// <summary>追加字段，可与 <c>ConfigAssetFactory.Template(...)</c> 构造时的 params 叠加。</summary>
        public TemplateSpec Fields(params TemplateFieldSpec[] more)
        {
            if (more == null || more.Length == 0)
                return this;
            if (fields == null)
                fields = new List<TemplateFieldSpec>();
            fields.AddRange(more);
            return this;
        }

        /// <summary>
        /// 覆盖基类字段的默认值：按类型工厂传值，如 <c>Int("hp", 250)</c> / <c>Bool("isBoss", true)</c> /
        /// <c>Enum("element", "HeroElement", "Water")</c>。类型必须与基类字段一致；基类字段只读或不存在时抛异常。
        /// 生成结果＝派生类构造函数里的赋值，只在有覆盖项时生成构造函数。
        /// </summary>
        public TemplateSpec OverrideDefaults(params TemplateFieldSpec[] more)
        {
            if (more == null || more.Length == 0)
                return this;
            if (overrides == null)
                overrides = new List<TemplateFieldSpec>();
            overrides.AddRange(more);
            return this;
        }
    }

    /// <summary>
    /// 批量建模板 / 追加字段时单个字段的描述。
    /// <para>
    /// <b>AI / 脚本请用静态工厂</b>（<c>Int</c> / <c>Float</c> / <c>Bool</c> / <c>Text</c> /
    /// <c>Enum</c> / <c>Reference</c> / <c>Of</c> …）按类型创建并直接传入默认值：
    /// 工厂自动把值写进对应的默认值槽，调用方不必知道 <c>defaultNumber</c> 等内部槽位，也不会填错槽。
    /// 不支持默认值的类型（资源引用 / 结构值类型 / Char / Decimal）用 <c>Of</c>。
    /// </para>
    /// </summary>
    public sealed class TemplateFieldSpec
    {
        public string name;
        public string displayName;
        public ConfigFieldType type = ConfigFieldType.String;
        public bool isList;
        public string refTypeFullName;
        public string enumRefClassName;

        /// <summary>数据编辑器里该字段只读（生成 [ReadOnly]）。用 <c>ReadOnly()</c> 链式设置。</summary>
        public bool readOnly;

        /// <summary>是否生成默认值初始化器；静态工厂在传入默认值时自动置位，一般无需手填。</summary>
        public bool useDefault;
        public long defaultNumber;
        public int defaultEnumValue;
        public double defaultReal;
        public bool defaultFlag;
        public string defaultText;

        /// <summary>
        /// 自引用字段标记：由 <c>SelfReference(name)</c> 置位，建模板时解析成
        /// 「当前命名空间 + 本模板 className」，模板与生成代码都还不存在也能建字段。
        /// </summary>
        public bool selfReference;

        // ---------- 链式修饰 ----------

        /// <summary>设置字段描述（模板字段表里的「描述」列）。</summary>
        public TemplateFieldSpec Desc(string description) { displayName = description; return this; }

        /// <summary>数据编辑器里只读。</summary>
        public TemplateFieldSpec ReadOnly(bool value = true) { readOnly = value; return this; }

        /// <summary>设为列表字段（列表字段不支持默认值）。</summary>
        public TemplateFieldSpec AsList(bool value = true) { isList = value; return this; }

        // ---------- 按类型的静态工厂（默认值按真实类型传，自动落槽） ----------

        public static TemplateFieldSpec Int(string name, int? defaultValue = null)
            => NumberSpec(name, ConfigFieldType.Int, defaultValue.HasValue ? (long?)defaultValue.Value : null);

        public static TemplateFieldSpec Long(string name, long? defaultValue = null)
            => NumberSpec(name, ConfigFieldType.Long, defaultValue);

        public static TemplateFieldSpec Short(string name, short? defaultValue = null)
            => NumberSpec(name, ConfigFieldType.Short, defaultValue.HasValue ? (long?)defaultValue.Value : null);

        public static TemplateFieldSpec Byte(string name, byte? defaultValue = null)
            => NumberSpec(name, ConfigFieldType.Byte, defaultValue.HasValue ? (long?)defaultValue.Value : null);

        public static TemplateFieldSpec UInt(string name, uint? defaultValue = null)
            => NumberSpec(name, ConfigFieldType.UInt, defaultValue.HasValue ? (long?)defaultValue.Value : null);

        public static TemplateFieldSpec Float(string name, float? defaultValue = null)
        {
            var f = new TemplateFieldSpec { name = name, type = ConfigFieldType.Float };
            if (defaultValue.HasValue)
            {
                f.useDefault = true;
                f.defaultReal = defaultValue.Value;
            }
            return f;
        }

        public static TemplateFieldSpec Double(string name, double? defaultValue = null)
        {
            var f = new TemplateFieldSpec { name = name, type = ConfigFieldType.Double };
            if (defaultValue.HasValue)
            {
                f.useDefault = true;
                f.defaultReal = defaultValue.Value;
            }
            return f;
        }

        public static TemplateFieldSpec Bool(string name, bool? defaultValue = null)
        {
            var f = new TemplateFieldSpec { name = name, type = ConfigFieldType.Bool };
            if (defaultValue.HasValue)
            {
                f.useDefault = true;
                f.defaultFlag = defaultValue.Value;
            }
            return f;
        }

        /// <summary>字符串族通用构造；<c>defaultValue = null</c> 表示不设默认值，传 <c>""</c> 表示默认空串。</summary>
        private static TemplateFieldSpec TextSpec(string name, ConfigFieldType type, string defaultValue)
        {
            var f = new TemplateFieldSpec { name = name, type = type };
            if (defaultValue != null)
            {
                f.useDefault = true;
                f.defaultText = defaultValue;
            }
            return f;
        }

        public static TemplateFieldSpec Text(string name, string defaultValue = null)
            => TextSpec(name, ConfigFieldType.String, defaultValue);

        public static TemplateFieldSpec LocKey(string name, string defaultValue = null)
            => TextSpec(name, ConfigFieldType.LocalizedKey, defaultValue);

        public static TemplateFieldSpec Addressable(string name, string defaultValue = null)
            => TextSpec(name, ConfigFieldType.Addressable, defaultValue);

        public static TemplateFieldSpec AudioEvent(string name, string defaultValue = null)
            => TextSpec(name, ConfigFieldType.AudioEvent, defaultValue);

        /// <summary>
        /// 枚举字段：按<b>成员名</b>指定默认值，工厂现场查枚举定义资产换算成数值存入专用 int 槽。
        /// 成员名不存在 / 枚举定义未创建时直接抛异常，不静默写 0。
        /// </summary>
        public static TemplateFieldSpec Enum(string name, string enumClassName, string memberName)
        {
            var f = new TemplateFieldSpec
            {
                name = name,
                type = ConfigFieldType.Enum,
                enumRefClassName = enumClassName ?? "",
            };
            if (memberName == null)
                return f;

            var def = ConfigEnumCodeGen.FindEnum(f.enumRefClassName);
            if (def == null || def.values == null)
                throw new InvalidOperationException(
                    $"字段 {name} 的枚举定义 {f.enumRefClassName} 不存在：请先创建枚举定义资产再指定枚举默认值。");
            for (var i = 0; i < def.values.Count; i++)
            {
                if (def.values[i] == null
                    || !string.Equals(def.values[i].name, memberName, StringComparison.Ordinal))
                    continue;
                var value = ConfigEnumDefinition.ValueOf(def, i);
                if (value > int.MaxValue || value < int.MinValue)
                    throw new InvalidOperationException($"枚举成员 {memberName} 的数值 {value} 超出默认值槽（int）范围。");
                f.useDefault = true;
                f.defaultEnumValue = (int)value;
                return f;
            }
            throw new InvalidOperationException(
                $"枚举 {f.enumRefClassName} 中没有成员 {memberName}；可用成员见枚举定义资产。");
        }

        /// <summary>枚举字段：已知枚举数值时直接指定（跳过成员名反查）。</summary>
        public static TemplateFieldSpec Enum(string name, string enumClassName, int defaultValue)
        {
            return new TemplateFieldSpec
            {
                name = name,
                type = ConfigFieldType.Enum,
                enumRefClassName = enumClassName ?? "",
                useDefault = true,
                defaultEnumValue = defaultValue,
            };
        }

        /// <summary>单参数重载：引用本模板将要生成的配置类（等价 <c>SelfReference(name)</c>，连关键字都不用写）。</summary>
        public static TemplateFieldSpec Reference(string name) => SelfReference(name);

        public static TemplateFieldSpec Reference(string name, string refTypeFullName)
        {
            return new TemplateFieldSpec
            {
                name = name,
                type = ConfigFieldType.Reference,
                refTypeFullName = refTypeFullName ?? "",
            };
        }

        /// <summary>
        /// 自引用字段：字段类型是「当前模板将要生成的配置类」。
        /// 模板还没创建、生成代码还不存在也能用；工厂按 className 写引用全名，生成期按模板资产判定。
        /// 典型用途：树形配置（parent / nextLevel）引用同表另一行。
        /// </summary>
        public static TemplateFieldSpec SelfReference(string name)
            => new TemplateFieldSpec
            {
                name = name,
                type = ConfigFieldType.Reference,
                selfReference = true,
            };

        /// <summary>其它类型（资源引用族 / 结构值类型 / Char / Decimal 等不支持默认值的类型）。</summary>
        public static TemplateFieldSpec Of(string name, ConfigFieldType type)
            => new TemplateFieldSpec { name = name, type = type };

        // ---------- 常用非默认值类型快捷入口（等价于 Of(name, 对应类型)） ----------

        public static TemplateFieldSpec Sprite(string name) => Of(name, ConfigFieldType.Sprite);
        public static TemplateFieldSpec Texture2D(string name) => Of(name, ConfigFieldType.Texture2D);
        public static TemplateFieldSpec GameObject(string name) => Of(name, ConfigFieldType.GameObject);
        public static TemplateFieldSpec AudioClip(string name) => Of(name, ConfigFieldType.AudioClip);
        public static TemplateFieldSpec AnimationClip(string name) => Of(name, ConfigFieldType.AnimationClip);
        public static TemplateFieldSpec Vector2(string name) => Of(name, ConfigFieldType.Vector2);
        public static TemplateFieldSpec Vector3(string name) => Of(name, ConfigFieldType.Vector3);
        public static TemplateFieldSpec Color(string name) => Of(name, ConfigFieldType.Color);
        public static TemplateFieldSpec BehaviourTree(string name) => Of(name, ConfigFieldType.BehaviourTree);

        private static TemplateFieldSpec NumberSpec(string name, ConfigFieldType type, long? defaultValue)
        {
            var f = new TemplateFieldSpec { name = name, type = type };
            if (defaultValue.HasValue)
            {
                f.useDefault = true;
                f.defaultNumber = defaultValue.Value;
            }
            return f;
        }
    }
}
