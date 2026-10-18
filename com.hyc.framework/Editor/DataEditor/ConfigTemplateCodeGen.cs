using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HYC.Framework.Editor;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Generates a C# <c>[CfgAsset]</c> config class from a <see cref="ConfigTemplate"/>.
    /// Output lands in <see cref="GeneratedPaths.Config"/> under a fixed namespace so
    /// the new type is picked up by <see cref="ConfigCreateWindow"/> immediately
    /// after a compile.
    /// </summary>
    public static class ConfigTemplateCodeGen
    {
        /// <summary>旧版默认输出目录（生成代码统一收口前的落点）。存量工程迁移见 <see cref="GeneratedFolderMigration"/>。</summary>
        public const string LegacyOutputDir = "Assets/GeneratedConfigs";
        /// <summary>旧版默认命名空间（生成代码命名空间 root 机制前的落点）。</summary>
        public const string LegacyDefaultNamespace = "HYC.Sample.Generated";

        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class",
            "const","continue","decimal","default","delegate","do","double","else","enum","event",
            "explicit","extern","false","finally","fixed","float","for","foreach","goto","if",
            "implicit","in","int","interface","internal","is","lock","long","namespace","new",
            "null","object","operator","out","override","params","private","protected","public",
            "readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static",
            "string","struct","switch","this","throw","true","try","typeof","uint","ulong",
            "unchecked","unsafe","ushort","using","virtual","void","volatile","while",
        };

        public static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name[0] != '_' && !char.IsLetter(name[0]))
                return false;
            for (var i = 1; i < name.Length; i++)
            {
                if (name[i] != '_' && !char.IsLetterOrDigit(name[i]))
                    return false;
            }
            return !CSharpKeywords.Contains(name);
        }

        /// <summary>
        /// 写盘前先比对内容：一致则跳过写入与 <c>AssetDatabase.ImportAsset</c>。
        /// 代码生成会连着产出六七个文件，重复生成时内容通常没变；
        /// 无条件重写会白白触发一串重新导入（Unity 侧表现为 .meta 时间戳刷新、编译抖动、
        /// 版本库里产生无意义 diff）。内容不同才写，返回是否真的写了。
        /// 路径相对工程根（内部按 <c>Directory.GetCurrentDirectory()</c> 拼接；传绝对路径则原样使用）。
        /// <para>
        /// 纯比对写盘、<b>不套生成头</b>——实现已下沉到 <see cref="GeneratedCodeFile.WriteIfChanged"/>；
        /// 写 .cs 生成物请走 <see cref="GeneratedCodeFile.WriteAndImport"/>（自动套「自动生成」壳），
        /// 或自行 <see cref="GeneratedCodeFile.WithHeader"/> 后调本方法。此处保留是为兼容既有调用方。
        /// </para>
        /// </summary>
        public static bool WriteIfChanged(string relativePath, string content)
            => GeneratedCodeFile.WriteIfChanged(relativePath, content);

        /// <summary>套「自动生成」壳 → 写文件 → 按需导入（内容未变则跳过导入，避免无谓的重新编译）。</summary>
        private static void WriteAndImport(string relativePath, string content)
            => GeneratedCodeFile.WriteAndImport(relativePath, content);

        /// <summary>删除生成产物（.meta 由 <see cref="AssetDatabase.DeleteAsset"/> 一并处理）；不存在则跳过。</summary>
        private static void DeleteGeneratedFile(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return;
            var full = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (!File.Exists(full))
                return;
            AssetDatabase.DeleteAsset(relativePath);
        }

        /// <summary>
        /// 清理 <see cref="ConfigDataSettings.OutputDir"/> 下「命名空间不在当前 root 派生范围」的生成 .cs（+.meta）。
        /// 命名空间 root 切换后，旧规则产出的文件不会被新规则覆盖（文件名制式不同 / 曾被格式门挡掉），
        /// 永远留在工程里报 CS0246。
        /// <para>
        /// 默认只删带自动生成头的文件（手写文件不碰）；解析不出命名空间的异常格式保守跳过。
        /// 传 <paramref name="relaxHeaderForEnums"/>=true（Regenerate All 用）时，对框架独占的枚举输出目录
        /// 放宽为「不依赖头壳」——旧版生成器产出的枚举文件没有自动生成头，否则会漏清、残留旧命名空间。
        /// 返回删除数量。
        /// </para>
        /// </summary>
        public static int CleanStaleNamespaceFiles(bool relaxHeaderForEnums = false)
        {
            var rootDir = Path.Combine(Directory.GetCurrentDirectory(), ConfigDataSettings.OutputDir);
            if (!Directory.Exists(rootDir))
                return 0;
            var currentNs = ConfigDataSettings.Namespace;
            var currentEnumNs = ConfigDataSettings.EnumNamespace;
            // 枚举输出目录由 ConfigEnumCodeGen 独占，任何落在其中的旧命名空间文件都是过时产物。
            var enumsDir = Path.Combine(Directory.GetCurrentDirectory(), ConfigEnumCodeGen.EnumsOutputDir)
                .Replace('\\', '/');
            var removed = 0;
            foreach (var file in Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories))
            {
                var relative = ToRelativeAssetPath(file);
                if (relative == null)
                    continue;
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }
                var filePath = file.Replace('\\', '/');
                var isEnumFile = relaxHeaderForEnums
                    && filePath.StartsWith(enumsDir + "/", StringComparison.Ordinal);
                // 默认只清理自动生成文件（首行 // <auto-generated>）；枚举目录在放宽模式下不依赖头壳。
                var firstLine = text.Split('\n')[0].Trim();
                if (!firstLine.Contains("<auto-generated>") && !isEnumFile)
                    continue;
                var ns = FirstNamespaceOf(text);
                if (ns == null)
                    continue; // 解析不出命名空间（异常格式）→ 保守跳过
                if (ns == currentNs || ns.StartsWith(currentNs + ".", StringComparison.Ordinal)
                    || ns == currentEnumNs || ns.StartsWith(currentEnumNs + ".", StringComparison.Ordinal))
                    continue; // 当前规则产物，保留
                AssetDatabase.DeleteAsset(relative);
                removed++;
            }
            return removed;
        }

        /// <summary>绝对路径 → 工程相对资产路径（Assets/...）；不在工程目录内返回 null。</summary>
        private static string ToRelativeAssetPath(string fullPath)
        {
            var cwd = Directory.GetCurrentDirectory().Replace('\\', '/');
            var p = fullPath.Replace('\\', '/');
            if (!p.StartsWith(cwd + "/", StringComparison.Ordinal))
                return null;
            return p.Substring(cwd.Length + 1);
        }

        /// <summary>取生成文件里第一个 namespace 声明名；遇到首个非注释非空非 using 行即止（找不到返回 null）。</summary>
        private static string FirstNamespaceOf(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("namespace ", StringComparison.Ordinal))
                    return t.Substring("namespace ".Length).Trim();
                if (t.Length > 0 && !t.StartsWith("//") && !t.StartsWith("using ", StringComparison.Ordinal))
                    return null;
            }
            return null;
        }

        /// <summary>
        /// Sanitizes a display name for storage: strips whitespace and converts
        /// backslashes to forward slashes so category/name splitting stays consistent.
        /// </summary>
        public static string SanitizeDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return displayName;

            var sb = new StringBuilder(displayName.Length);
            foreach (var c in displayName)
            {
                if (char.IsWhiteSpace(c))
                    continue;
                sb.Append(c == '\\' ? '/' : c);
            }
            return sb.ToString();
        }

        /// <summary>Validates template input. Returns false and fills <paramref name="error"/> when invalid.</summary>
        public static bool Validate(ConfigTemplate template, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(template.displayName))
            {
                error = "显示名不能为空";
                return false;
            }
            if (!IsValidIdentifier(template.className))
            {
                error = $"类名 {template.className} 不是合法的 C# 标识符";
                return false;
            }

            // 类名全局唯一：其他模板不得使用相同 className
            var nameConflict = FindClassNameConflict(template);
            if (nameConflict != null)
            {
                error = $"类名 {template.className} 已被其他模板使用（{AssetDatabase.GetAssetPath(nameConflict)}）";
                return false;
            }

            // 基类链：循环检测 + 基类类名合法性
            var chain = new List<ConfigTemplate>();
            if (template.baseTemplate != null)
            {
                if (!TryBuildInheritanceChain(template, chain, out error))
                    return false;
            }

            // 查重范围 = 继承链字段名 + 自身字段名（不可与基类字段重名/覆盖）
            var allNames = new HashSet<string>();
            foreach (var baseTpl in chain)
            {
                foreach (var baseField in baseTpl.fields)
                    allNames.Add(baseField.name);
            }

            var ownNames = new HashSet<string>();
            foreach (var field in template.fields)
            {
                if (!IsValidIdentifier(field.name))
                {
                    error = $"字段名 {field.name} 不是合法的 C# 标识符";
                    return false;
                }
                if (!ownNames.Add(field.name))
                {
                    error = $"字段名 {field.name} 重复";
                    return false;
                }
                if (allNames.Contains(field.name))
                {
                    error = $"字段名 {field.name} 与基类字段重复";
                    return false;
                }
                if (field.type == ConfigFieldType.Reference && string.IsNullOrEmpty(field.refTypeFullName))
                {
                    error = $"字段 {field.name} 已选择“配置引用”，但未选择引用类型";
                    return false;
                }
                if (field.type == ConfigFieldType.Reference)
                {
                    // 引用目标必须存在：生成类（短名命中模板）或反射可解析的手写 [CfgAsset] 类。
                    // 判定按短名 + 当前命名空间，不信存储全名里的旧命名空间（root 切换后已失效）。
                    var refShortName = RefTypeShortName(field.refTypeFullName);
                    if (refShortName == null
                        || (!IsGeneratedConfigClassName(refShortName) && ResolveRefTypeForUi(field.refTypeFullName) == null))
                    {
                        error = $"字段 {field.name} 引用的配置类型不存在（模板已删除或未生成）";
                        return false;
                    }
                }
                if (field.type == ConfigFieldType.Enum)
                {
                    if (string.IsNullOrEmpty(field.enumRefClassName))
                    {
                        error = $"字段 {field.name} 已选择“枚举引用”，但未选择枚举定义";
                        return false;
                    }
                    if (ConfigEnumCodeGen.FindEnum(field.enumRefClassName) == null)
                    {
                        error = $"字段 {field.name} 的枚举 {field.enumRefClassName} 不存在（未创建或已删除）";
                        return false;
                    }
                }
            }

            // 导出可行性（硬错误，阻断生成）：服务器 JSON 直接导出 Unity 对象/Gradient/曲线；
            // 以及一切在 Blob 里落到 default:int 兜底的“直接导出”（客户端编译报错）。
            var exportErrors = CollectExportFeasibilityErrors(template);
            if (exportErrors.Count > 0)
            {
                error = string.Join("；", exportErrors);
                return false;
            }

            return true;
        }

        // ---------- 导出可行性校验（静态：不靠捏样例，直接复用生成器的类型解析） ----------

        /// <summary>
        /// 该类型在「直接导出」下是否无法被 Newtonsoft 序列化为 JSON（服务器侧）。
        /// 集合定义见 <see cref="ConfigFieldTypes.IsServerUnsupported"/>（与模板编辑器共用同一份判定）。
        /// </summary>
        private static bool IsJsonUnsupportedElementType(ConfigFieldType type)
            => ConfigFieldTypes.IsServerUnsupported(type);

        /// <summary>
        /// 静态判断某字段的导出配置是否“能导”（与代码生成共用同一份类型知识，永不漂移，不依赖样例试跑）：
        /// <list type="bullet">
        /// <item>客户端(Blob)：复用 <see cref="BlobElemInfoOf"/> —— 若落到 <c>default: int</c> 兜底（该类型 Direct 下生成器本就会产出坏代码、编译报错），视为不可导。</item>
        /// <item>服务器(JSON)：Direct 且类型属于 <see cref="IsJsonUnsupportedElementType"/> 视为不可导。</item>
        /// </list>
        /// 返回 (ClientBroken, ServerBroken)；用元组而非新增类型，避免改动文件/类型计数。
        /// </summary>
        public static (bool ClientBroken, bool ServerBroken) GetExportFeasibility(ConfigTemplateField f)
        {
            var result = (ClientBroken: false, ServerBroken: false);
            if (f == null)
                return result;

            // 客户端：Blob 必须 blittable。BlobElemInfoOf 对不支持的类型会落 default:int（历史兜底），
            // 但生成器随后在 BlobBuilder 里把对象/结构赋给 int 字段 → 编译报错。把“落到兜底”识别为不可导。
            // （Int/Enum 本身合法映射成 int，排除；BehaviourTree→long 等也非 int，不会误伤。）
            if (IsExportedOnSide(f, true))
            {
                var info = BlobElemInfoOf(f);
                if (info.TypeName == "int" && f.type != ConfigFieldType.Int && f.type != ConfigFieldType.Enum)
                    result.ClientBroken = true;
            }

            // 服务器：Direct 且类型是 Unity 对象/不可 JSON 序列化类型 → Newtonsoft 无法序列化。
            if (IsExportedOnSide(f, false))
            {
                if (f.serverExport.mode == ConfigFieldExportMode.Direct && IsJsonUnsupportedElementType(f.type))
                    result.ServerBroken = true;
            }

            return result;
        }

        /// <summary>单字段的导出可行性说明（不可导时返回非空提示，含哪一侧 + 建议；可导时返回 null）。</summary>
        public static string ExportFeasibilityMessage(ConfigTemplateField f)
        {
            if (f == null)
                return null;
            var feas = GetExportFeasibility(f);
            if (!feas.ClientBroken && !feas.ServerBroken)
                return null;

            var typeName = f.type.ToString();
            var sides = feas.ClientBroken && feas.ServerBroken
                ? "客户端 Blob 与服务器 JSON 均"
                : feas.ClientBroken ? "客户端 Blob" : "服务器 JSON";
            // 服务器侧不可 JSON 类型（Object 引用族 / Color / Color32 / Gradient / AnimationCurve）：
            // 模板编辑器已强制只能「不导出」，历史存档若是「直接导出」需改回，不再建议资源名等无效选项。
            // 其余资源类型可走 资源名/路径/AA地址/转字符串；非资源类型只能转字符串；两侧都能单独选「不导出」。
            string suggestion;
            if (feas.ServerBroken && IsJsonUnsupportedElementType(f.type))
                suggestion = "服务器侧已不再提供该类型的导出选项；若历史存档为「直接导出」，请在「服务器导出」下拉改回「不导出」。";
            else if (IsAssetRefType(f.type))
                suggestion = "请改用：资源名 / 资源路径 / AA地址 / 转字符串（任一即可导出为字符串），或该侧直接选「不导出」。";
            else
                suggestion = "请改用：转字符串（导出为字符串），或该侧直接选「不导出」。";
            return $"字段 {f.name}（{typeName}）使用「直接导出」→ {sides}无法导出。{suggestion}";
        }

        /// <summary>
        /// 收集模板所有字段的导出可行性错误（硬错误，阻断生成）。覆盖：资源/对象/Gradient/曲线直接导出到服务器 JSON；
        /// 以及一切在 Blob 里落到 default:int 兜底的“直接导出”（客户端编译报错）。
        /// </summary>
        public static List<string> CollectExportFeasibilityErrors(ConfigTemplate template)
        {
            var errors = new List<string>();
            if (template == null)
                return errors;
            foreach (var f in template.fields)
            {
                var msg = ExportFeasibilityMessage(f);
                if (msg != null)
                    errors.Add(msg);
            }
            return errors;
        }

        /// <summary>
        /// 在自身 + 继承链字段中按名查找（关联参数条件的目标字段用；链为空时只查自身）。
        /// </summary>
        public static ConfigTemplateField FindFieldByName(ConfigTemplate template, List<ConfigTemplate> chain, string fieldName)
        {
            if (template == null || string.IsNullOrEmpty(fieldName))
                return null;

            foreach (var f in template.fields)
            {
                if (f != null && f.name == fieldName)
                    return f;
            }
            if (chain != null)
            {
                foreach (var tpl in chain)
                {
                    if (tpl == null)
                        continue;
                    foreach (var f in tpl.fields)
                    {
                        if (f != null && f.name == fieldName)
                            return f;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 收集字段「关联参数」条件的警告（不阻断生成：无效条件在生成时跳过、求值时视为满足）。
        /// 覆盖：未选目标 / 指向自身 / 目标不存在 / 目标为列表 / 目标非 bool-enum / enum 条件未选值。
        /// </summary>
        public static List<string> CollectVisibilityWarnings(ConfigTemplate template)
        {
            var warnings = new List<string>();
            if (template == null)
                return warnings;

            var chain = new List<ConfigTemplate>();
            TryBuildInheritanceChain(template, chain, out _); // 链异常时链为空 → 目标全"找不到"，由既有错误提示负责

            foreach (var field in template.fields)
            {
                if (field?.visibility == null)
                    continue;
                foreach (var cond in field.visibility)
                {
                    if (cond == null)
                        continue;
                    var targetName = cond.targetField ?? "";
                    if (string.IsNullOrEmpty(targetName))
                    {
                        warnings.Add($"字段 {field.name} 有一条关联条件未选择目标字段（该条件不生效）");
                        continue;
                    }
                    if (targetName == field.name)
                    {
                        warnings.Add($"字段 {field.name} 的关联条件指向自身（该条件不生效）");
                        continue;
                    }
                    var target = FindFieldByName(template, chain, targetName);
                    if (target == null)
                    {
                        warnings.Add($"字段 {field.name} 的关联条件指向不存在的字段 {targetName}（视为满足，字段保持显示）");
                        continue;
                    }
                    if (target.isList)
                    {
                        warnings.Add($"字段 {field.name} 的关联条件指向列表字段 {targetName}（不支持，条件不生效）");
                        continue;
                    }
                    if (target.type == ConfigFieldType.Bool)
                        continue;
                    if (target.type == ConfigFieldType.Enum)
                    {
                        if (cond.enumValues == null || cond.enumValues.Count == 0)
                            warnings.Add($"字段 {field.name} 对 {targetName} 的关联条件未选择任何枚举值（永远满足不了，字段将不显示）");
                        continue;
                    }
                    warnings.Add($"字段 {field.name} 的关联条件指向的 {targetName} 不是布尔/枚举字段（不支持，条件不生效）");
                }
            }
            return warnings;
        }

        /// <summary>Generates the C# source text for a template, or null on validation failure.</summary>
        public static string GenerateCode(ConfigTemplate template, out string error)
        {
            error = null;
            if (!Validate(template, out error))
                return null;

            // 解析继承链，检测循环
            var chain = new List<ConfigTemplate>();
            if (!TryBuildInheritanceChain(template, chain, out error))
                return null;

            var usings = new SortedSet<string> { "HYC.Framework.Config" };
            var needsUnity = false;
            var needsCollections = false;

            var fieldLines = new StringBuilder();
            foreach (var field in template.fields)
            {
                var typeName = ResolveTypeName(field, usings, ref needsUnity, ref needsCollections);
                if (typeName == null)
                {
                    error = $"字段 {field.name} 的引用类型无法解析（可能已删除）";
                    return null;
                }

                if (!string.IsNullOrEmpty(field.description))
                {
                    var desc = field.description.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    fieldLines.AppendLine($"        [Header(\"{desc}\")]");
                    needsUnity = true;
                }

                // 数值范围限制：Min/Max 特性（编辑器输入自动钳制）
                if (field.hasRange && IsNumericField(field.type))
                {
                    fieldLines.AppendLine($"        [Min({field.minValue}f)]");
                    fieldLines.AppendLine($"        [Max({field.maxValue}f)]");
                    needsUnity = true;
                }

                // 只读字段：加 [ReadOnly] 特性（数据编辑器据此禁用该字段的输入；HYC.Framework.Config 已在 usings）
                if (field.readOnly)
                    fieldLines.AppendLine("        [ReadOnly]");

                // 多语言 key：加 [LocKey] 特性（数据编辑器据此绘制联想/选择/tooltip/校验）
                if (field.type == ConfigFieldType.LocalizedKey)
                    fieldLines.AppendLine("        [LocKey]");
                // 行为树：加 [BehaviourTreeField] 特性（数据编辑器据此绘制树选择下拉）
                if (field.type == ConfigFieldType.BehaviourTree)
                    fieldLines.AppendLine("        [BehaviourTreeField]");
                // Addressable：加 [AddressableField] 特性（数据编辑器据此绘制拖资源框）
                if (field.type == ConfigFieldType.Addressable)
                    fieldLines.AppendLine("        [AddressableField]");
                // 框架音频：加 [AudioField] 特性（数据编辑器据此绘制音频下拉框）
                if (field.type == ConfigFieldType.AudioEvent)
                    fieldLines.AppendLine("        [AudioField]");

                // 关联参数：条件可见性（数据编辑器按条件绘制本字段；多条 AND，可叠加多个特性）
                if (field.visibility != null)
                {
                    foreach (var cond in field.visibility)
                    {
                        if (cond == null || string.IsNullOrEmpty(cond.targetField))
                            continue;
                        var target = FindFieldByName(template, chain, cond.targetField);
                        if (target == null || target.isList || ReferenceEquals(target, field))
                            continue;   // 目标丢失/列表字段/指向自身 → 不求值（视为满足），模板侧给警告
                        if (target.type == ConfigFieldType.Bool)
                        {
                            fieldLines.AppendLine(
                                $"        [CfgVisibleWhen(\"{cond.targetField}\", {(cond.boolValue ? "true" : "false")})]");
                        }
                        else if (target.type == ConfigFieldType.Enum)
                        {
                            var values = cond.enumValues;
                            if (values == null || values.Count == 0)
                                continue;   // 空值列表永不满足 → 不发射（保持显示），模板侧给警告
                            var parts = new List<string> { $"\"{cond.targetField}\"" };
                            foreach (var v in values)
                                parts.Add($"\"{v}\"");
                            fieldLines.AppendLine($"        [CfgVisibleWhen({string.Join(", ", parts)})]");
                        }
                        // 其它类型（目标字段被改成 int 等）→ 不发射，求值时视为满足
                    }
                }

                fieldLines.AppendLine($"        public {typeName} {field.name};");
            }

            // 基类 className：取继承链上最近一级基类（chain[0] 是直接基类）。
            // 无基类模板时直接继承 ScriptableObject。统一生成非 sealed 类。
            var baseClassName = chain.Count > 0 ? chain[0].className : "ScriptableObject";
            var declaration = $"public class {template.className} : {baseClassName}";
            if (chain.Count == 0)
                needsUnity = true; // ScriptableObject 在 UnityEngine，需 using

            if (needsUnity)
                usings.Add("UnityEngine");
            if (needsCollections)
                usings.Add("System.Collections.Generic");

            // 框架级模板（FrameworkTemplates 下的 ConfigBase 等基类）是基础设施，不是
            // 用户可创建的配置资产：照常标 [CfgAsset]（数据编辑器仍可识别），但加
            // Hide = true，不出现在「配置创建器」里。基类分配走 GetConfigBaseTemplate()
            // 按模板 className 查找，不依赖创建器。
            var templateAssetPath = AssetDatabase.GetAssetPath(template);
            var isFrameworkTemplate = !string.IsNullOrEmpty(templateAssetPath)
                && templateAssetPath.StartsWith(FrameworkTemplatesFolder, StringComparison.Ordinal);

            var sb = new StringBuilder();
            foreach (var u in usings)
                sb.AppendLine($"using {u};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ConfigDataSettings.Namespace}");
            sb.AppendLine("{");
            sb.AppendLine(isFrameworkTemplate
                ? $"    [CfgAsset(\"{Escape(template.displayName)}\", 0, Hide = true)]"
                : $"    [CfgAsset(\"{Escape(template.displayName)}\", 0)]");
            sb.AppendLine($"    {declaration}");
            sb.AppendLine("    {");
            sb.Append(fieldLines);
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }


        /// <summary>字段导出目标过滤：返回该目标包含的字段（自身 + 继承链全部）。</summary>
        public static ConfigTemplateField[] GetExportFields(ConfigTemplate template, bool forClient)
        {
            var list = new List<ConfigTemplateField>();
            var chain = new List<ConfigTemplate>();
            if (!TryBuildInheritanceChain(template, chain, out _))
                return list.ToArray();

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var f in chain[i].fields)
                {
                    if (IsExportedOnSide(f, forClient))
                        list.Add(f);
                }
            }
            foreach (var f in template.fields)
            {
                if (IsExportedOnSide(f, forClient))
                    list.Add(f);
            }
            return list.ToArray();
        }

        /// <summary>是否为数值字段类型（可配 Min/Max）。判定集合见 <see cref="ConfigFieldTypes.IsNumeric"/>。</summary>
        private static bool IsNumericField(ConfigFieldType type)
            => ConfigFieldTypes.IsNumeric(type);

        /// <summary>
        /// 该字段类型是否存在真正的「空」态（null / 空串），即「非空」检查是否有意义。
        /// 判定集合见 <see cref="ConfigFieldTypes.SupportsNotEmpty"/>。
        /// </summary>
        private static bool SupportsNotEmptyField(ConfigFieldType type)
            => ConfigFieldTypes.SupportsNotEmpty(type);

        /// <summary>
        /// 该字段在这一侧是否导出。旧版靠 <c>exportTarget</c>（Client/Server/Both）单独判定「进哪一侧」，
        /// 现统一由该侧的导出设置决定：选了 <see cref="ConfigFieldExportMode.None"/> 就是这一侧不导出。
        /// 单一真源，避免「闸门 + 方式」两处都要维护。
        /// </summary>
        private static bool IsExportedOnSide(ConfigTemplateField f, bool forClient)
        {
            if (f == null) return false;
            var set = forClient ? f.clientExport : f.serverExport;
            return set != null && set.mode != ConfigFieldExportMode.None;
        }

        /// <summary>
        /// 生成客户端/服务器类型代码。struct 时字段平铺（子含父全部字段，结构体不能继承）；
        /// class 时保持继承链（仅声明自身字段，基类字段如 ID/GUID 来自基类；
        /// From 平铺赋值全部导出字段——继承字段经派生引用可直接访问，赋值合法）。
        /// </summary>
        public static string GenerateSideType(ConfigTemplate template, bool forClient, out string error)
        {
            error = null;
            if (!Validate(template, out error))
                return null;

            var chain = new List<ConfigTemplate>();
            if (!TryBuildInheritanceChain(template, chain, out error))
                return null;

            var side = forClient ? "Client" : "Server";
            var ns = ConfigDataSettings.Namespace;
            var className = template.className;
            var isStruct = ConfigDataSettings.SideTypeIsStruct;

            var usings = new SortedSet<string>();
            var needsUnity = false;
            var needsCollections = false;

            // 自身导出字段（class 模式只声明这些；struct 模式平铺全部继承字段）
            var ownFields = new List<ConfigTemplateField>();
            foreach (var f in template.fields)
            {
                if (IsExportedOnSide(f, forClient))
                    ownFields.Add(f);
            }

            // From 赋值用全部导出字段：继承链字段（顶层→直接基类）+ 自身
            var flatFields = new List<ConfigTemplateField>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var f in chain[i].fields)
                {
                    if (IsExportedOnSide(f, forClient))
                        flatFields.Add(f);
                }
            }
            flatFields.AddRange(ownFields);

            var declaredFields = isStruct ? flatFields : ownFields;

            var fieldLines = new StringBuilder();
            foreach (var field in declaredFields)
            {
                var typeName = ExportFieldDeclTypeName(field, forClient, usings, ref needsUnity, ref needsCollections, out var typeError);
                if (typeName == null)
                {
                    error = typeError ?? $"字段 {field.name} 的导出类型无法解析";
                    return null;
                }
                fieldLines.AppendLine($"        public {typeName} {field.name};");
            }

            if (needsUnity)
                usings.Add("UnityEngine");
            if (needsCollections)
                usings.Add("System.Collections.Generic");

            // 侧类型继承链：链上直接基类存在即继承（不再看 generateCode）。
            // 宿主生成的基类 → 同命名空间短名；框架级基类（generateCode=false，主类随框架预编译，
            // 如 ConfigBase）→ 框架手写的侧类型基类（HYC.Framework.Config.{side}.{Base}{side}），全限定名引用。
            // 约定：框架级基类模板必须在框架内提供同名侧类型基类，否则生成代码 CS0246（报错自明）。
            string baseSideName = null;
            if (chain.Count > 0)
            {
                var baseTpl = chain[0];
                baseSideName = baseTpl.generateCode
                    ? $"{baseTpl.className}{side}"
                    : $"HYC.Framework.Config.{side}.{baseTpl.className}{side}";
            }

            var sb = new StringBuilder();
            foreach (var u in usings)
                sb.AppendLine($"using {u};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.{side}");
            sb.AppendLine("{");

            if (isStruct)
            {
                sb.AppendLine($"    public struct {className}{side}");
            }
            else
            {
                var baseDecl = baseSideName != null ? $" : {baseSideName}" : "";
                sb.AppendLine($"    public class {className}{side}{baseDecl}");
            }
            sb.AppendLine("    {");
            sb.Append(fieldLines);

            // From 赋值方法：始终平铺赋值全部导出字段（Editor 类字段公开可访问；按每侧导出设置投影）
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>从编辑器配置类赋值（仅导出目标匹配的字段，含继承字段；按导出设置投影）。</summary>");
            sb.AppendLine($"        public static {className}{side} From({ns}.{className} editor)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var r = new {className}{side}();");
            foreach (var f in flatFields)
                sb.AppendLine($"            r.{f.name} = {ExportFromValueExpr(f, forClient, $"editor.{f.name}")};");
            sb.AppendLine("            return r;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }


        /// <summary>生成 Blob 结构体 Cfg{ClassName} + BlobBuilder（客户端 Blob 导出用）。</summary>
        public static string GenerateBlobStruct(ConfigTemplate template, out string error)
        {
            error = null;
            if (!Validate(template, out error))
                return null;

            var ns = ConfigDataSettings.Namespace;
            var className = template.className;

            // 客户端导出字段（含继承链，平铺——Blob 结构不能继承）
            var clientFields = GetExportFields(template, true);
            if (clientFields.Length == 0)
            {
                error = $"模板 {className} 没有客户端导出字段，无法生成 Blob";
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using Unity.Collections;");
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Blob");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 的 Blob 结构（客户端运行时查询）。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public struct Cfg{className}");
            sb.AppendLine("    {");

            var hasBlobString = false;
            var hasBlobArray = false;
            foreach (var f in clientFields)
            {
                var typeName = BlobTypeName(f, out var isStr, out var isArr);
                if (isStr) hasBlobString = true;
                if (isArr) hasBlobArray = true;
                sb.AppendLine($"        public {typeName} {f.name};");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>编辑器配置类型 → Blob 字段类型名（按客户端导出设置；string→BlobString/FixedString, List→BlobArray）。</summary>
        private static string BlobTypeName(ConfigTemplateField f, out bool isString, out bool isArray)
        {
            isString = false;
            isArray = false;

            var info = BlobElemInfoOf(f);
            if (info.BlobString || info.FixedString)
                isString = true;
            if (f.isList)
            {
                isArray = true;
                return $"BlobArray<{info.TypeName}>";
            }
            return info.TypeName;
        }

        /// <summary>生成 Blob 构建器（编辑器导出用）：BlobBuilder 构建 Cfg{ClassName} 表。</summary>
        public static string GenerateBlobBuilder(ConfigTemplate template, out string error)
        {
            error = null;
            var structCode = GenerateBlobStruct(template, out error);
            if (structCode == null)
                return null;

            var ns = ConfigDataSettings.Namespace;
            var className = template.className;
            var clientFields = GetExportFields(template, true);

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Unity.Collections;");
            sb.AppendLine("using Unity.Entities;");
            // 构建器只在导出时（Editor）被调用，生成到 Editor 程序集，可引用编辑器 API 解析资源地址
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using HYC.Framework.Config.Editor;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Blob");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 的 Blob 构建器：把编辑器配置类构建成 BlobAssetReference。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static class {className}BlobBuilder");
            sb.AppendLine("    {");
            sb.AppendLine($"        /// <summary>构建单个配置的 Blob 行。</summary>");
            sb.AppendLine($"        public static void Write(ref BlobBuilder builder, ref Cfg{className} target, {ns}.{className} data)");
            sb.AppendLine("        {");
            foreach (var f in clientFields)
            {
                var info = BlobElemInfoOf(f);
                if (f.isList)
                {
                    sb.AppendLine($"            var arr{f.name} = builder.Allocate(ref target.{f.name}, data.{f.name}.Count);");
                    if (info.BlobString || info.FixedString)
                    {
                        sb.AppendLine($"            for (var i = 0; i < data.{f.name}.Count; i++)");
                        if (info.FixedString)
                            sb.AppendLine($"                arr{f.name}[i] = new {info.TypeName}({BlobStringExpr(f, $"data.{f.name}[i]")});");
                        else
                            sb.AppendLine($"                builder.AllocateString(ref arr{f.name}[i], {BlobStringExpr(f, $"data.{f.name}[i]")});");
                    }
                    else
                    {
                        sb.AppendLine($"            for (var i = 0; i < data.{f.name}.Count; i++) arr{f.name}[i] = {BlobNumExpr(f, $"data.{f.name}[i]")};");
                    }
                }
                else if (info.FixedString)
                {
                    sb.AppendLine($"            target.{f.name} = new {info.TypeName}({BlobStringExpr(f, $"data.{f.name}")});");
                }
                else if (info.BlobString)
                {
                    sb.AppendLine($"            builder.AllocateString(ref target.{f.name}, {BlobStringExpr(f, $"data.{f.name}")});");
                }
                else
                {
                    sb.AppendLine($"            target.{f.name} = {BlobNumExpr(f, $"data.{f.name}")};");
                }
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>构建一个配置实例的 Blob。</summary>");
            sb.AppendLine($"        public static BlobAssetReference<Cfg{className}> Build({ns}.{className} data)");
            sb.AppendLine("        {");
            sb.AppendLine("            var builder = new BlobBuilder(Allocator.Temp);");
            sb.AppendLine($"            ref var root = ref builder.ConstructRoot<Cfg{className}>();");
            sb.AppendLine($"            Write(ref builder, ref root, data);");
            sb.AppendLine($"            var blob = builder.CreateBlobAssetReference<Cfg{className}>(Allocator.Persistent);");
            sb.AppendLine("            builder.Dispose();");
            sb.AppendLine("            return blob;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Walks <paramref name="template"/>'s base chain (template → baseTemplate → …)
        /// into <paramref name="chain"/> ordered from outermost base to nearest base.
        /// Returns false and sets <paramref name="error"/> on a cycle or a missing
        /// base class-name (would produce invalid inheritance).
        /// </summary>
        public static bool TryBuildInheritanceChain(ConfigTemplate template, List<ConfigTemplate> chain, out string error)
        {
            error = null;
            chain.Clear();

            var visited = new HashSet<ConfigTemplate> { template };
            var current = template.baseTemplate;
            while (current != null)
            {
                if (!visited.Add(current))
                {
                    error = $"基类循环引用: {current.displayName} 已出现在继承链中";
                    return false;
                }

                if (string.IsNullOrEmpty(current.className) || !IsValidIdentifier(current.className))
                {
                    error = $"基类模板 “{current.displayName}” 的类名 {current.className} 非法，无法作为基类";
                    return false;
                }

                chain.Add(current);
                current = current.baseTemplate;
            }

            return true;
        }

        /// <summary>
        /// Flattened field list: outermost base fields first, then this template's
        /// own fields. Base fields are marked read-only so the editor can render
        /// them as a readonly group.
        /// </summary>
        public static List<KeyValuePair<ConfigTemplateField, bool>> GetAllFields(ConfigTemplate template, out string error)
        {
            error = null;
            var result = new List<KeyValuePair<ConfigTemplateField, bool>>();

            var chain = new List<ConfigTemplate>();
            if (!TryBuildInheritanceChain(template, chain, out error))
                return result;

            // chain 为"近→远"（直接基类在前），展示按"最顶层 → 直接基类"顺序
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var field in chain[i].fields)
                    result.Add(new KeyValuePair<ConfigTemplateField, bool>(field, true));
            }
            foreach (var field in template.fields)
                result.Add(new KeyValuePair<ConfigTemplateField, bool>(field, false));

            return result;
        }

        private static string ResolveTypeName(ConfigTemplateField field, SortedSet<string> usings, ref bool needsUnity, ref bool needsCollections)
        {
            string baseName;

            // 值类型关键字
            switch (field.type)
            {
                case ConfigFieldType.String: baseName = "string"; break;
                case ConfigFieldType.Int: baseName = "int"; break;
                case ConfigFieldType.Long: baseName = "long"; break;
                case ConfigFieldType.Float: baseName = "float"; break;
                case ConfigFieldType.Double: baseName = "double"; break;
                case ConfigFieldType.Bool: baseName = "bool"; break;
                case ConfigFieldType.Short: baseName = "short"; break;
                case ConfigFieldType.Byte: baseName = "byte"; break;
                case ConfigFieldType.UInt: baseName = "uint"; break;
                case ConfigFieldType.Char: baseName = "char"; break;
                case ConfigFieldType.Decimal: baseName = "decimal"; break;
                case ConfigFieldType.BehaviourTree: baseName = "long"; break;
                default:
                {
                    // UnityEngine 类型
                    var unityType = UnityTypeName(field.type);
                    if (unityType != null)
                    {
                        baseName = unityType;
                        needsUnity = true;
                        break;
                    }

                    if (field.type == ConfigFieldType.Reference)
                    {
                        // 引用目标分两种，命名空间来源不同：
                        // ① 生成类（短名命中某 generateCode 模板的 className）——命名空间恒由当前
                        //    root 派生，与模板资产里存储的 pick 时全名无关。按存储全名反射会在 root
                        //    切换后命中旧程序集的旧命名空间，把失效 using 写进生成代码（CS0246）；
                        //    且编译失败期间旧程序集不退出，再生成依旧反射到旧类型——死循环。
                        //    故生成类只取短名，零反射。
                        // ② 手写 [CfgAsset] 类（无对应模板）——按真实命名空间：先按当前 ns 重建
                        //    全名试，失败再按存储全名兜底（它不随 root 搬家，存储值仍指得对）。
                        var shortName = RefTypeShortName(field.refTypeFullName);
                        if (shortName == null)
                            return null;
                        if (IsGeneratedConfigClassName(shortName))
                        {
                            baseName = shortName;
                            // 与枚举分支同一处理：侧类型（{ns}.Client/Server）靠这个 using 短名引用配置类。
                            usings.Add(ConfigDataSettings.Namespace);
                        }
                        else
                        {
                            var t = Type.GetType(RebuildRefTypeFullName(field.refTypeFullName) ?? field.refTypeFullName)
                                    ?? Type.GetType(field.refTypeFullName);
                            if (t == null)
                                return null;
                            if (!string.IsNullOrEmpty(t.Namespace))
                                usings.Add(t.Namespace);
                            baseName = t.Name;
                        }
                        break;
                    }

                    if (field.type == ConfigFieldType.Enum)
                    {
                        var e = ConfigEnumCodeGen.FindEnum(field.enumRefClassName);
                        if (e == null)
                            return null;
                        baseName = e.className;
                        usings.Add(ConfigDataSettings.EnumNamespace);
                        break;
                    }

                    if (field.type == ConfigFieldType.LocalizedKey)
                    {
                        baseName = "string";
                        break;
                    }

                    if (field.type == ConfigFieldType.Addressable)
                    {
                        baseName = "string";
                        break;
                    }

                    if (field.type == ConfigFieldType.AudioEvent)
                    {
                        baseName = "string";
                        usings.Add("HYC.Framework.Audio");
                        break;
                    }

                    return null;
                }
            }

            if (field.isList)
            {
                needsCollections = true;
                return $"List<{baseName}>";
            }
            return baseName;
        }

        // ---------- 配置引用类型的确定性解析（命名空间 root 切换安全） ----------

        /// <summary>
        /// 从模板资产存储的 Assembly-qualified 全名里剥出类型短名（丢弃命名空间与程序集限定）。
        /// <para>
        /// 引用目标必为 <c>[CfgAsset]</c> 配置类，其命名空间恒由当前命名空间 root 派生
        /// （<see cref="ConfigDataSettings.Namespace"/>），与 pick 时存储的全名里的命名空间无关。
        /// 按存储全名反射会在 root 切换后命中旧程序集里的旧命名空间类型，把失效 using 写进
        /// 生成代码（CS0246）；且编译失败期间旧程序集不退出，再生成依旧反射到旧类型——死循环。
        /// </para>
        /// </summary>
        public static string RefTypeShortName(string storedFullName)
        {
            if (string.IsNullOrEmpty(storedFullName))
                return null;
            var comma = storedFullName.IndexOf(',');
            var typePart = comma >= 0 ? storedFullName.Substring(0, comma) : storedFullName;
            var dot = typePart.LastIndexOf('.');
            var shortName = dot >= 0 ? typePart.Substring(dot + 1) : typePart;
            shortName = shortName.Trim();
            return shortName.Length > 0 ? shortName : null;
        }

        /// <summary>
        /// 把存储的全名按<b>当前</b>命名空间重建（换命名空间、保留程序集限定部分），供反射解析用。
        /// 存储全名里的命名空间只是 pick 时的历史痕迹，root 切换后按它反射会拿到旧命名空间类型。
        /// </summary>
        public static string RebuildRefTypeFullName(string storedFullName)
        {
            var shortName = RefTypeShortName(storedFullName);
            if (shortName == null)
                return null;
            var comma = storedFullName.IndexOf(',');
            var assemblyPart = comma >= 0 ? storedFullName.Substring(comma) : string.Empty;
            return ConfigDataSettings.Namespace + "." + shortName + assemblyPart;
        }

        /// <summary>
        /// 供编辑器 UI 解析引用类型：先按存储全名试，失败再按当前命名空间重建后试
        /// （root 切换后旧全名里的命名空间可能已失效）。仅用于 UI 展示/校验，不参与代码生成。
        /// </summary>
        public static Type ResolveRefTypeForUi(string storedFullName)
        {
            if (string.IsNullOrEmpty(storedFullName))
                return null;
            var t = Type.GetType(storedFullName);
            if (t != null)
                return t;
            var rebuilt = RebuildRefTypeFullName(storedFullName);
            return rebuilt != null ? Type.GetType(rebuilt) : null;
        }

        /// <summary>
        /// 类型是否为框架生成的配置类/枚举（按短名命中模板/枚举清单）——其命名空间按当前 root
        /// 派生，不信任反射拿到的命名空间（可能是旧程序集的残留）。
        /// </summary>
        private static bool IsGeneratedConfigOrEnumType(Type t)
        {
            if (t == null || string.IsNullOrEmpty(t.Name))
                return false;
            if (ConfigEnumCodeGen.LoadAllEnums().Any(e => e != null && e.className == t.Name))
                return true;
            return IsGeneratedConfigClassName(t.Name);
        }

        /// <summary>短名是否命中某 <c>generateCode</c> 模板的 className（= 由生成器产出的配置类）。</summary>
        private static bool IsGeneratedConfigClassName(string shortName)
            => LoadAllTemplates().Any(t => t != null && t.generateCode && t.className == shortName);

        private static string UnityTypeName(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Sprite: return "Sprite";
                case ConfigFieldType.Texture2D: return "Texture2D";
                case ConfigFieldType.GameObject: return "GameObject";
                case ConfigFieldType.AudioClip: return "AudioClip";
                case ConfigFieldType.Material: return "Material";
                case ConfigFieldType.Mesh: return "Mesh";
                case ConfigFieldType.PhysicMaterial: return "PhysicMaterial";
                case ConfigFieldType.Font: return "Font";
                case ConfigFieldType.Shader: return "Shader";
                case ConfigFieldType.TextAsset: return "TextAsset";
                case ConfigFieldType.Object: return "Object";
                case ConfigFieldType.AnimationClip: return "AnimationClip";
                case ConfigFieldType.AnimatorController: return "AnimatorController";
                case ConfigFieldType.Vector2: return "Vector2";
                case ConfigFieldType.Vector3: return "Vector3";
                case ConfigFieldType.Vector4: return "Vector4";
                case ConfigFieldType.Vector2Int: return "Vector2Int";
                case ConfigFieldType.Vector3Int: return "Vector3Int";
                case ConfigFieldType.Quaternion: return "Quaternion";
                case ConfigFieldType.Color: return "Color";
                case ConfigFieldType.Color32: return "Color32";
                case ConfigFieldType.Rect: return "Rect";
                case ConfigFieldType.RectInt: return "RectInt";
                case ConfigFieldType.RectOffset: return "RectOffset";
                case ConfigFieldType.Bounds: return "Bounds";
                case ConfigFieldType.BoundsInt: return "BoundsInt";
                case ConfigFieldType.Gradient: return "Gradient";
                case ConfigFieldType.AnimationCurve: return "AnimationCurve";
                case ConfigFieldType.LayerMask: return "LayerMask";
                default: return null;
            }
        }

        // ---------- 导出设置（字段级投影：客户端/服务端各自解析） ----------

        private static readonly ConfigFieldExportSetting s_DefaultExport = new ConfigFieldExportSetting();

        private static ConfigFieldExportSetting ExportSettingOf(ConfigTemplateField f, bool forClient)
        {
            var s = forClient ? f.clientExport : f.serverExport;
            return s ?? s_DefaultExport;
        }

        /// <summary>string-like 源类型（编辑器存储为 string）。</summary>
        private static bool IsStringLike(ConfigFieldType type)
            => type == ConfigFieldType.String || type == ConfigFieldType.LocalizedKey
               || type == ConfigFieldType.Addressable || type == ConfigFieldType.AudioEvent;

        /// <summary>源字段是否为 Unity 资源/对象引用（编辑器存储为对象）。判定集合见 <see cref="ConfigFieldTypes.IsAssetRef"/>。</summary>
        private static bool IsAssetRefType(ConfigFieldType type)
            => ConfigFieldTypes.IsAssetRef(type);

        /// <summary>取配置引用目标类型上的某个成员字段类型。</summary>
        private static Type MemberTypeOf(ConfigTemplateField f, string memberName, out string error)
        {
            error = null;
            // 存储全名里的命名空间可能已随 root 切换失效：先按原全名试，再按当前命名空间重建后试。
            var t = Type.GetType(f.refTypeFullName);
            if (t == null)
            {
                var rebuilt = RebuildRefTypeFullName(f.refTypeFullName);
                if (rebuilt != null)
                    t = Type.GetType(rebuilt);
            }
            if (t == null)
            {
                error = $"字段 {f.name} 的引用类型无法解析（可能未生成）";
                return null;
            }
            var fi = t.GetField(memberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (fi == null)
            {
                error = $"字段 {f.name} 的引用类型 {t.Name} 上没有成员 {memberName}";
                return null;
            }
            return fi.FieldType;
        }

        private static bool IsScalarType(Type t)
            => t.IsPrimitive || t.IsEnum || t == typeof(string);

        /// <summary>CLR 类型 → 生成代码里可用的类型名（基础类型关键字 / 类型名 + using）。</summary>
        private static string CsTypeName(Type t, SortedSet<string> usings, out string error)
        {
            error = null;
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(byte)) return "byte";
            if (t == typeof(sbyte)) return "sbyte";
            if (t == typeof(short)) return "short";
            if (t == typeof(ushort)) return "ushort";
            if (t == typeof(int)) return "int";
            if (t == typeof(uint)) return "uint";
            if (t == typeof(long)) return "long";
            if (t == typeof(ulong)) return "ulong";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(decimal)) return "decimal";
            if (t == typeof(char)) return "char";
            // 配置/枚举类的命名空间由当前 root 唯一确定：反射拿到的可能是旧程序集的旧命名空间
            // （root 切换后、重新编译前），不能信。枚举落在独立的 {root}.Enum（与配置类 {root}.config 平级）；
            // 只有真正的外部类型才按反射命名空间加 using。
            string effectiveNs;
            if (t.IsEnum && IsGeneratedConfigOrEnumType(t))
                effectiveNs = ConfigDataSettings.EnumNamespace;
            else if (IsGeneratedConfigOrEnumType(t))
                effectiveNs = ConfigDataSettings.Namespace;
            else
                effectiveNs = t.Namespace;
            if (!string.IsNullOrEmpty(effectiveNs) && effectiveNs != ConfigDataSettings.Namespace)
                usings.Add(effectiveNs);
            return t.Name;
        }

        /// <summary>计算某侧导出字段在 DTO/JSON 类型里的类型名（Direct 保持原逻辑）。</summary>
        private static string ExportFieldDeclTypeName(ConfigTemplateField f, bool forClient,
            SortedSet<string> usings, ref bool needsUnity, ref bool needsCollections, out string error)
        {
            error = null;
            var setting = ExportSettingOf(f, forClient);
            if (setting.IsDirect)
                return ResolveTypeName(f, usings, ref needsUnity, ref needsCollections);

            string elem;
            switch (setting.mode)
            {
                case ConfigFieldExportMode.ToString:
                case ConfigFieldExportMode.AssetName:
                case ConfigFieldExportMode.AssetPath:
                case ConfigFieldExportMode.AssetAddressable:
                    elem = "string";
                    break;
                case ConfigFieldExportMode.BlobString:
                case ConfigFieldExportMode.FixedString:
                    if (!IsStringLike(f.type))
                    {
                        error = $"字段 {f.name} 的\"{setting.mode}\"只能用于字符串字段";
                        return null;
                    }
                    // DTO/JSON 侧仍是 string；Blob 侧由 Blob 生成器按此选择编码
                    elem = "string";
                    break;
                case ConfigFieldExportMode.Member:
                {
                    if (f.type != ConfigFieldType.Reference)
                    {
                        error = $"字段 {f.name} 选了\"取成员\"但不是配置引用字段";
                        return null;
                    }
                    if (string.IsNullOrEmpty(setting.memberName))
                    {
                        error = $"字段 {f.name} 选了\"取成员\"但未选择成员";
                        return null;
                    }
                    var mt = MemberTypeOf(f, setting.memberName, out error);
                    if (mt == null) return null;
                    if (!IsScalarType(mt))
                    {
                        error = $"字段 {f.name} 的成员 {setting.memberName} 不是标量类型";
                        return null;
                    }
                    elem = CsTypeName(mt, usings, out error);
                    if (elem == null) return null;
                    break;
                }
                default:
                    error = $"字段 {f.name} 的导出方式未实现: {setting.mode}";
                    return null;
            }

            if (f.isList)
            {
                needsCollections = true;
                return $"List<{elem}>";
            }
            return elem;
        }

        /// <summary>生成 From 赋值右侧表达式（editor 表达式）。Direct/BlobString/FixedString 直接赋值原字段。</summary>
        private static string ExportFromValueExpr(ConfigTemplateField f, bool forClient, string editorExpr)
        {
            var setting = ExportSettingOf(f, forClient);
            switch (setting.mode)
            {
                case ConfigFieldExportMode.ToString:
                    if (f.isList)
                        return $"{editorExpr} == null ? null : {editorExpr}.ConvertAll(v => v.ToString())";
                    return $"{editorExpr}.ToString()";
                case ConfigFieldExportMode.Member:
                    if (f.isList)
                        return $"{editorExpr} == null ? null : {editorExpr}.ConvertAll(v => v == null ? default : v.{setting.memberName})";
                    return $"{editorExpr} != null ? {editorExpr}.{setting.memberName} : default";
                case ConfigFieldExportMode.AssetName:
                    if (f.isList)
                        return $"{editorExpr} == null ? null : {editorExpr}.ConvertAll(v => v == null ? null : v.name)";
                    return $"{editorExpr} != null ? {editorExpr}.name : null";
                case ConfigFieldExportMode.AssetPath:
                case ConfigFieldExportMode.AssetAddressable:
                    // 需编辑器 API（AssetDatabase/Addressables）在导出时解析，From 阶段置空由导出器回填
                    return "null";
                default:
                    return editorExpr;
            }
        }

        // ---------- Blob（客户端）字段级写入辅助 ----------

        private sealed class BlobElemInfo
        {
            public string TypeName;
            public bool BlobString;
            public bool FixedString;
        }

        /// <summary>客户端字段在 Blob 里的元素类型 + 存储方式（按客户端导出设置）。</summary>
        private static BlobElemInfo BlobElemInfoOf(ConfigTemplateField f)
        {
            var info = new BlobElemInfo();
            var set = ExportSettingOf(f, true);
            var type = f.type;

            // 显式 FixedString（仅字符串源字段可选）
            if (set.mode == ConfigFieldExportMode.FixedString && IsStringLike(type))
            {
                info.TypeName = $"FixedString{(int)set.fixedStringSize}Bytes";
                info.FixedString = true;
                return info;
            }

            // 配置引用取成员：string 成员 → BlobString；枚举 → int；其他标量数值 → 同类型
            if (set.mode == ConfigFieldExportMode.Member)
            {
                var mt = MemberTypeOf(f, set.memberName, out _);
                if (mt != null && IsScalarType(mt))
                {
                    if (mt == typeof(string))
                    {
                        info.TypeName = "BlobString";
                        info.BlobString = true;
                        return info;
                    }
                    if (mt.IsEnum)
                    {
                        info.TypeName = "int";
                        return info;
                    }
                    info.TypeName = CsTypeName(mt, new SortedSet<string>(), out _);
                    return info;
                }
            }

            // 资源引用（Object/Sprite/...）+ 导出 名字/路径/AA地址 → Blob 里是字符串（定长 FixedString，容量取 fixedStringSize，默认128）
            if (IsAssetRefType(type)
                && (set.mode == ConfigFieldExportMode.AssetName
                    || set.mode == ConfigFieldExportMode.AssetPath
                    || set.mode == ConfigFieldExportMode.AssetAddressable))
            {
                info.TypeName = $"FixedString{(int)set.fixedStringSize}Bytes";
                info.FixedString = true;
                return info;
            }

            // string 类源 / 资源名字 / 数值转字符串 → BlobString
            if (IsStringLike(type)
                || set.mode == ConfigFieldExportMode.AssetName
                || (set.mode == ConfigFieldExportMode.ToString && !IsStringLike(type)))
            {
                info.TypeName = "BlobString";
                info.BlobString = true;
                return info;
            }

            // Direct / 其他：沿用原有映射；仍不支持的类型沿用 int 回退（历史行为）
            switch (type)
            {
                case ConfigFieldType.Int: info.TypeName = "int"; break;
                case ConfigFieldType.Long: info.TypeName = "long"; break;
                case ConfigFieldType.Float: info.TypeName = "float"; break;
                case ConfigFieldType.Double: info.TypeName = "double"; break;
                case ConfigFieldType.Bool: info.TypeName = "bool"; break;
                case ConfigFieldType.Short: info.TypeName = "short"; break;
                case ConfigFieldType.Byte: info.TypeName = "byte"; break;
                case ConfigFieldType.UInt: info.TypeName = "uint"; break;
                case ConfigFieldType.BehaviourTree: info.TypeName = "long"; break;
                case ConfigFieldType.Reference: info.TypeName = "long"; break;
                case ConfigFieldType.Enum: info.TypeName = "int"; break;
                // 非托管 blittable 的 Unity 结构体：Blob 里可直接存原类型
                case ConfigFieldType.Color: info.TypeName = "Color"; break;
                case ConfigFieldType.Color32: info.TypeName = "Color32"; break;
                case ConfigFieldType.Vector2: info.TypeName = "Vector2"; break;
                case ConfigFieldType.Vector3: info.TypeName = "Vector3"; break;
                case ConfigFieldType.Vector4: info.TypeName = "Vector4"; break;
                case ConfigFieldType.Quaternion: info.TypeName = "Quaternion"; break;
                case ConfigFieldType.Vector2Int: info.TypeName = "Vector2Int"; break;
                case ConfigFieldType.Vector3Int: info.TypeName = "Vector3Int"; break;
                default: info.TypeName = "int"; break;
            }
            return info;
        }

        /// <summary>数值型 Blob 赋值表达式（成员导出/枚举带转换）。</summary>
        private static string BlobNumExpr(ConfigTemplateField f, string e)
        {
            var set = ExportSettingOf(f, true);
            if (set.mode == ConfigFieldExportMode.Member)
            {
                var mt = MemberTypeOf(f, set.memberName, out _);
                if (mt != null && mt.IsEnum)
                    return $"(int)({e} == null ? default : {e}.{set.memberName})";
                return $"{e} == null ? default : {e}.{set.memberName}";
            }
            if (f.type == ConfigFieldType.Enum)
                return $"(int){e}";
            // 配置引用：Blob 里只存目标配置的长整型 ID（ConfigBase.ID）；源字段是引用对象，
            // 需空安全取出 ID，避免 long = 引用对象 的 CS0029。
            if (f.type == ConfigFieldType.Reference)
                return $"{e} == null ? 0L : {e}.ID";
            return e;
        }

        /// <summary>字符串型 Blob 赋值源表达式（直接串/资源名/成员串/数值 ToString）。</summary>
        private static string BlobStringExpr(ConfigTemplateField f, string e)
        {
            var set = ExportSettingOf(f, true);
            if (IsAssetRefType(f.type))
            {
                // 资源引用：导出时由编辑器 API 解析（构建器在 Editor 程序集，可引用 GUIDrawer/AssetDatabase）
                // 未拖入资源 / 未标记 Addressable 时返回空串，避免 FixedString 构造收到 null
                if (set.mode == ConfigFieldExportMode.AssetAddressable)
                    return $"{e} == null ? \"\" : (HYC.Framework.Config.Editor.GUIDrawer.GetAddressableAddress({e}) ?? \"\")";
                if (set.mode == ConfigFieldExportMode.AssetPath)
                    return $"{e} == null ? \"\" : (UnityEditor.AssetDatabase.GetAssetPath({e}) ?? \"\")";
                if (set.mode == ConfigFieldExportMode.AssetName)
                    return $"{e} == null ? \"\" : {e}.name";
            }
            if (IsStringLike(f.type))
                return $"{e} ?? \"\"";   // 缺省/留空的字符串字段反序列化为 null，BlobBuilder 直接写 null 会 NRE，故兜底为空串
            if (set.mode == ConfigFieldExportMode.AssetName)
                return $"{e} == null ? \"\" : {e}.name";
            if (set.mode == ConfigFieldExportMode.Member)
            {
                var mt = MemberTypeOf(f, set.memberName, out _);
                if (mt == typeof(string))
                    return $"{e} == null ? \"\" : {e}.{set.memberName}";
                return $"{e}.ToString()";
            }
            if (set.mode == ConfigFieldExportMode.ToString)
                return $"{e}.ToString()";
            return e;
        }

        /// <summary>在导出器生成的 JSON 分支里，为“路径/AA地址”字段回填编辑器解析后的字符串。</summary>
        private static void AppendJsonAssetOverrides(StringBuilder sb, ConfigTemplate template, bool client, string dtoVar, string dataExpr, string indent)
        {
            foreach (var f in GetExportFields(template, client))
            {
                var set = ExportSettingOf(f, client);
                if (set.mode != ConfigFieldExportMode.AssetPath && set.mode != ConfigFieldExportMode.AssetAddressable)
                    continue;

                var expr = $"{dataExpr}.{f.name}";
                string value;
                if (set.mode == ConfigFieldExportMode.AssetPath)
                {
                    value = f.isList
                        ? $"{expr} == null ? null : {expr}.ConvertAll(o => o == null ? null : UnityEditor.AssetDatabase.GetAssetPath(o))"
                        : $"{expr} == null ? null : UnityEditor.AssetDatabase.GetAssetPath({expr})";
                }
                else
                {
                    value = f.isList
                        ? $"{expr} == null ? null : {expr}.ConvertAll(o => o == null ? null : HYC.Framework.Config.Editor.GUIDrawer.GetAddressableAddress(o))"
                        : $"{expr} == null ? null : HYC.Framework.Config.Editor.GUIDrawer.GetAddressableAddress({expr})";
                }
                sb.AppendLine($"{indent}{dtoVar}.{f.name} = {value};");
            }
        }

        private static string Escape(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>True when the generated .cs for this template exists on disk (即已生成过)。</summary>
        public static bool IsGenerated(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return false;
            var relativePath = $"{ConfigDataSettings.OutputDir}/{template.className}.cs";
            return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
        }

        /// <summary>
        /// 模板作为基类是否“就绪”（宿主生成的派生代码能否解析到该基类类型）：
        /// ① 框架级模板（<c>generateCode=false</c>，主类随包预编译，如 <c>ConfigBase</c>）恒就绪；
        /// ② 其余模板宿主 <c>.cs</c> 已生成为就绪；
        /// ③ 兜底：框架 Runtime 程序集已编译出同名基类类型（<c>HYC.Framework.Config.{className}</c>）——
        ///    即使 <c>baseTemplate</c> 误指向旧宿主副本 / 旧 <c>generateCode</c> 值，也认定基类可用，
        ///    编辑器据此不再误报“基类未生成代码”。派生模板的“基类须已生成”校验用它。
        /// </summary>
        public static bool IsBaseReady(ConfigTemplate template)
        {
            if (template == null)
                return false;
            if (!template.generateCode)
                return true;
            if (IsGenerated(template))
                return true;
            return FrameworkProvidesBaseType(template.className);
        }

        // 每种基类类名在单个域会话内只扫一次程序集（域重载会清空，天然反映最新编译结果）。
        private static readonly Dictionary<string, bool> s_FrameworkBaseTypeCache = new Dictionary<string, bool>();

        /// <summary>框架 Runtime 是否已编译出 <c>HYC.Framework.Config.{className}</c> 基类类型。</summary>
        private static bool FrameworkProvidesBaseType(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;
            if (s_FrameworkBaseTypeCache.TryGetValue(className, out var cached))
                return cached;

            var fullName = "HYC.Framework.Config." + className;
            var found = false;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetType(fullName) != null)
                    {
                        found = true;
                        break;
                    }
                }
                catch
                {
                    // 个别程序集 GetType 可能因类型加载异常抛出，忽略继续扫描
                }
            }

            s_FrameworkBaseTypeCache[className] = found;
            return found;
        }

        /// <summary>displayName → 安全资产文件名（去空格、/ 和 \ 换 -、去非法字符）。</summary>
        public static string DisplayNameToAssetName(string displayName)
        {
            var sanitized = SanitizeDisplayName(displayName);
            if (string.IsNullOrEmpty(sanitized))
                return "未命名";

            var sb = new StringBuilder(sanitized.Length);
            foreach (var c in sanitized)
            {
                if (c == '/' || c == '\\')
                {
                    sb.Append('-');
                }
                else if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ' ')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('-');
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 将模板资产重命名为 displayName 对应的文件名（MoveAsset 保留 GUID）。
        /// 目标名冲突时自动加序号。返回实际路径。
        /// </summary>
        public static string SyncTemplateAssetName(ConfigTemplate template)
        {
            if (template == null)
                return null;

            var currentPath = AssetDatabase.GetAssetPath(template);
            if (string.IsNullOrEmpty(currentPath))
                return null;

            var dir = Path.GetDirectoryName(currentPath);
            var desired = DisplayNameToAssetName(template.displayName);
            var fileName = desired + ".asset";
            var targetPath = Path.Combine(dir, fileName);

            var index = 1;
            while (!string.Equals(targetPath, currentPath, StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                fileName = $"{desired} {index}.asset";
                targetPath = Path.Combine(dir, fileName);
                index++;
            }

            if (string.Equals(targetPath, currentPath, StringComparison.OrdinalIgnoreCase))
                return currentPath; // 已经是对的名字

            var result = AssetDatabase.MoveAsset(currentPath, targetPath);
            if (!string.IsNullOrEmpty(result))
            {
                // 回退到当前路径
                return currentPath;
            }

            AssetDatabase.SaveAssets();
            return targetPath;
        }

        /// <summary>Writes the generated .cs under <see cref="OutputDir"/> and imports it.</summary>
        public static bool WriteFile(ConfigTemplate template, out string error)
        {
            // 框架级基类（如 ConfigBase）：主类随框架预编译（HYC.Framework.Config.ConfigBase），
            // 不再往宿主生成任何代码（主类 / Editor / Client / Server / Blob / Check / Export）。
            // 由模板自身的 generateCode 开关声明，不写死类名。
            if (template != null && !template.generateCode)
            {
                error = null; // 跳过即成功，无错误
                return true;
            }

            var code = GenerateCode(template, out error);
            if (code == null)
                return false;

            var absoluteDir = Path.Combine(Directory.GetCurrentDirectory(), ConfigDataSettings.OutputDir);
            Directory.CreateDirectory(absoluteDir);

            var relativePath = $"{ConfigDataSettings.OutputDir}/{template.className}.cs";
            WriteAndImport(relativePath, code);

            // 同时生成 XxxConfigEditor.cs（走默认分组渲染，用户可自行扩展）
            WriteEditorFile(template);

            // 生成客户端/服务器类型 + 检查类 + 导出类
            WriteSideFiles(template, forClient: true, out _);
            WriteSideFiles(template, forClient: false, out _);
            WriteCheckFile(template);
            WriteExportFile(template);

            // 客户端格式为 Blob 时：生成 Blob 结构 + 构建器 + 运行时加载 System（需有客户端导出字段）
            if (ConfigDataSettings.ClientFormat == 1 && GetExportFields(template, true).Length > 0)
            {
                WriteBlobStructFile(template);
                WriteBlobBuilderFile(template);
                WriteBlobSystemFile(template);
            }
            else
            {
                // 非 Blob 格式（或无客户端导出字段）：删除残留产物。命名空间 root 切换后这些
                // 文件不会被重写，永远停在旧命名空间，是 CS0246 的错误源。
                DeleteGeneratedFile($"{ConfigDataSettings.OutputDir}/Blob/Cfg{template.className}.cs");
                DeleteGeneratedFile($"{ConfigDataSettings.OutputDir}/Blob/{template.className}BlobSystem.cs");
                DeleteGeneratedFile($"{ConfigDataSettings.OutputDir}/Editor/Blob/{template.className}BlobBuilder.cs");
                DeleteGeneratedFile($"{ConfigDataSettings.OutputDir}/Blob/{template.className}BlobBuilder.cs"); // 旧位置
            }

            // 配置查询门面（{ns}.ConfigManager，静态、无 World 依赖）随每次生成同步
            WriteFacadeFile();

            AssetDatabase.Refresh();

            // 图标传播：自定义纹理持久化进 .cs.meta；模板 / MonoScript / 存量资产即时应用
            ConfigIconApplier.PersistForTemplate(template);
            ConfigIconApplier.ApplyForTemplate(template);

            // 记录本次生成类名
            // Package 内资产只读，不能（也不需要）持久化 lastGeneratedClassName
            var templatePath = AssetDatabase.GetAssetPath(template);
            if (string.IsNullOrEmpty(templatePath) || !templatePath.StartsWith("Packages/"))
            {
                template.lastGeneratedClassName = template.className;
                EditorUtility.SetDirty(template);
            }
            return true;
        }

        /// <summary>
        /// 写配置查询门面 <c>{OutputDir}/ConfigManager.cs</c>：<c>{ns}.ConfigManager.{Type}.GetByID(id)</c>
        /// → <c>ref Cfg{Type}</c>（零拷贝，转框架 <c>HYC.Framework.Config.ConfigRegistry.GetRef</c>）；
        /// 遍历全表走 <c>{Type}.Count</c> + <c>{Type}.GetByIndex(i)</c>（转 <c>GetRowCount</c> / <c>GetRowRefAt</c>）。
        /// 静态、无 World 依赖，是宿主查询配置的唯一稳定入口（低层 <c>ConfigRegistry</c> 仅生成代码 / 自建加载系统内部调用；不再提供 World 扩展，消除与 EA0001 铁律冲突的「值拷贝 / 未命中返 default」地雷）。
        /// <para>只收录磁盘上已生成 Blob 结构 <c>Blob/Cfg{Type}.cs</c> 的类型——避免引用不存在的类型（CS0246）；
        /// 无可用类型（ClientFormat≠Blob 或全部无客户端导出字段）时删除残留门面。</para>
        /// <para>门面名 <c>ConfigManager</c> 与框架低层注册表 <c>HYC.Framework.Config.ConfigRegistry</c>
        /// 已不重名：宿主查询只写 <c>ConfigManager</c> 即门面，不会与框架类型二义（CS0104）；
        /// 只有手工建表 / 注册才需直接调 <c>ConfigRegistry</c>（生成的 <c>XxxBlobSystem</c> 内部就是这么做的）。</para>
        /// </summary>
        public static void WriteFacadeFile()
        {
            var path = $"{ConfigDataSettings.OutputDir}/ConfigManager.cs";
            var classNames = CollectBlobTypeClassNames();
            if (classNames.Count == 0)
            {
                DeleteGeneratedFile(path);   // 无 Blob 类型 → 清残留门面（与 Blob 生成条件联动）
                return;
            }
            WriteAndImport(path, BuildFacadeCode(classNames));
        }

        /// <summary>收集磁盘上已生成 <c>Blob/Cfg{Type}.cs</c> 的配置类型短名（Ordinal 排序，稳定输出）。</summary>
        private static List<string> CollectBlobTypeClassNames()
        {
            var list = new List<string>();
            var root = Directory.GetCurrentDirectory();
            foreach (var t in LoadAllTemplates())
            {
                if (t == null || !t.generateCode || string.IsNullOrEmpty(t.className))
                    continue;
                var blobPath = Path.Combine(root, $"{ConfigDataSettings.OutputDir}/Blob/Cfg{t.className}.cs");
                if (!File.Exists(blobPath))
                    continue;   // 只收录已生成 Blob 结构的类型，避免引用不存在的 Cfg{Type}
                list.Add(t.className);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>拼门面源码：<c>{ns}.ConfigManager</c> 下每类型一个嵌套类——GetByID 转 <c>ConfigRegistry.GetRef</c>、GetByIndex 转 <c>GetRowRefAt</c>、Count 转 <c>GetRowCount</c>。</summary>
        private static string BuildFacadeCode(List<string> classNames)
        {
            var ns = ConfigDataSettings.Namespace;
            var sb = new StringBuilder();
            sb.AppendLine($"using {ns}.Blob;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>配置查询门面：静态、无 World 依赖。每类型一个嵌套类——按 id 查询走 GetByID，遍历全表走 Count + GetByIndex，均返回指向 Blob 行的可写 ref（零拷贝；刻意不用 ref readonly——C# 禁 ref-readonly 返回值绑 ref var（CS8329），而分析器又要求 blob 行局部变量必须非只读 ref（EA0001），可写 ref 是唯一合法形态，只读靠纪律保证）。⛔ 取行必须 ref 接收（EA0001）：<c>ref var row = ref ...GetByID(id)</c>——行含 BlobString/BlobArray（自相对偏移），值拷贝寻址错位。</summary>");
            sb.AppendLine("    public static class ConfigManager");
            sb.AppendLine("    {");
            foreach (var className in classNames)
            {
                sb.AppendLine($"        /// <summary>{className} 查询。⛔ 取行必须 ref 接收（EA0001）：<c>ref var row = ref ...{className}.GetByID(id)</c>——行含 BlobString/BlobArray，值拷贝寻址错位。</summary>");
                sb.AppendLine($"        public static class {className}");
                sb.AppendLine("        {");
                sb.AppendLine($"            /// <summary>按配置 id 取一行（零拷贝 ref）。未命中抛 KeyNotFoundException——引用型 id 必然存在，数据错误应响亮失败。⛔ 必须 <c>ref var row = ref ...</c> 接收（EA0001：blob 行禁值拷贝，值拷贝寻址错位）；ref 禁跨方法传参/存字段/闭包捕获/跨帧缓存。</summary>");
                sb.AppendLine($"            public static ref Cfg{className} GetByID(long id)");
                sb.AppendLine($"                => ref HYC.Framework.Config.ConfigRegistry.GetRef<Cfg{className}>(id);");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>表已加载的行数；未加载时为 0。遍历全表时作 GetByIndex 的循环边界。</summary>");
                sb.AppendLine($"            public static int Count");
                sb.AppendLine($"                => HYC.Framework.Config.ConfigRegistry.GetRowCount<Cfg{className}>();");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>按行序取一行（零拷贝 ref）。越界抛 KeyNotFoundException；配合 Count 遍历全表。⛔ 同样必须 <c>ref var r = ref ...</c> 接收（EA0001）。</summary>");
                sb.AppendLine($"            public static ref Cfg{className} GetByIndex(int index)");
                sb.AppendLine($"                => ref HYC.Framework.Config.ConfigRegistry.GetRowRefAt<Cfg{className}>(index);");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>配置 id 是否已加载（不取行、零拷贝、无 EA0001 风险）。配合 GetByID 实现『宽松语义』：<c>if (Contains(id)) {{ ref var row = ref GetByID(id); ... }}</c>。表未注册时返回 false。</summary>");
                sb.AppendLine($"            public static bool Contains(long id)");
                sb.AppendLine($"                => HYC.Framework.Config.ConfigRegistry.Contains<Cfg{className}>(id);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }


        /// <summary>生成 Blob 结构体文件（ns.Blob / Cfg{className}.cs）。</summary>
        private static void WriteBlobStructFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;
            var code = GenerateBlobStruct(template, out _);
            if (code == null)
                return;
            var dir = $"{ConfigDataSettings.OutputDir}/Blob";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), dir));
            var path = $"{dir}/Cfg{template.className}.cs";
            WriteAndImport(path, code);
        }

        /// <summary>生成 Blob 构建器文件（ns.Blob / {className}BlobBuilder.cs）。</summary>
        private static void WriteBlobBuilderFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;
            var code = GenerateBlobBuilder(template, out _);
            if (code == null)
                return;

            // 清理旧位置的同名文件：构建器曾生成在 {OutputDir}/Blob（运行时程序集），后移到
            // {OutputDir}/Editor/Blob（Editor 程序集）。若旧文件残留，同名类会在两个程序集里
            // 各存在一份 → CS0101 重复定义。已升级的存量工程见此自动迁移。
            var legacy = $"{ConfigDataSettings.OutputDir}/Blob/{template.className}BlobBuilder.cs";
            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), legacy)))
                AssetDatabase.DeleteAsset(legacy);

            // 构建器只在导出时由 Editor 调用，放到 Editor 程序集下，才能引用 GUIDrawer/AssetDatabase 解析资源地址
            var dir = $"{ConfigDataSettings.OutputDir}/Editor/Blob";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), dir));
            var path = $"{dir}/{template.className}BlobBuilder.cs";
            WriteAndImport(path, code);
        }

        /// <summary>生成 Blob 运行时加载 System + 查询扩展（运行时把 .blob 挂进 ConfigRegistry）。</summary>
        private static void WriteBlobSystemFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;
            var className = template.className;
            var ns = ConfigDataSettings.Namespace;

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            // 不再输出 using System.IO：加载走 StreamingAssets.ReadBytes（平台无关），
            // 生成的文件里已无 Path / File / Directory 用法，留着只会刷 CS8019。
            sb.AppendLine("using Unity.Collections;");
            sb.AppendLine("using Unity.Entities;");
            // 不再输出 using Unity.Entities.Serialization：内存解析 unsafe 代码已收在框架
            // HYC.Framework.IO.BlobReader 内，生成体只做一次安全调用，宿主无需开 /unsafe。
            sb.AppendLine("using HYC.Framework.Config;");
            sb.AppendLine("using HYC.Framework.IO;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Blob");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// 加载 Cfg{className}.blob 并注册进 ConfigRegistry；运行期查询走生成式门面 {ns}.ConfigManager.{className}（零拷贝 ref，详见 ConfigManager.cs）。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public partial class {className}BlobSystem : SystemBase");
            sb.AppendLine("    {");
            sb.AppendLine("        protected override void OnCreate()");
            sb.AppendLine("        {");
            sb.AppendLine("            base.OnCreate();");
            sb.AppendLine("            Load();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        protected override void OnUpdate() { }");
            sb.AppendLine();
            sb.AppendLine("        public static void Load()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var relative = $\"{ConfigDataSettings.BlobLoadPath}/Cfg{className}.blob\";");
            sb.AppendLine($"            var bytes = HYC.Framework.IO.StreamingAssets.ReadBytes(relative);");
            sb.AppendLine("            if (bytes == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogWarning($\"Blob not found: {relative}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (HYC.Framework.IO.BlobReader.TryRead<BlobRoot<Cfg" + className + ">>(bytes, 0, out var blob))");
            sb.AppendLine("            {");
            // 按模板是否有 ID 字段决定索引方式
            var hasIdField = GetExportFields(template, true).Any(f => f.name == "ID");
            if (hasIdField)
                sb.AppendLine($"                var table = ConfigBlobTable<Cfg{className}>.FromBlob(blob, r => r.ID);");
            else
                sb.AppendLine($"                var table = ConfigBlobTable<Cfg{className}>.FromBlob(blob);");
            sb.AppendLine($"                ConfigRegistry.Register(table);");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogError($\"Failed to read blob: {relative}\");");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            var dir = $"{ConfigDataSettings.OutputDir}/Blob";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), dir));
            var path = $"{dir}/{className}BlobSystem.cs";
            WriteAndImport(path, sb.ToString());
        }

        /// <summary>生成 XxxConfigEditor.cs：继承 BaseConfigEditor&lt;T&gt; 走默认分组渲染。</summary>
        private static void WriteEditorFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;

            var editorDir = ConfigDataSettings.EditorDir;
            if (string.IsNullOrEmpty(editorDir))
                return;

            var className = template.className;
            var ns = ConfigDataSettings.Namespace;

            var sb = new StringBuilder();
            sb.AppendLine($"using HYC.Framework.Config.Editor;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Editor");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 的配置编辑器。继承默认分组渲染；如需自定义请另建编辑器文件，");
            sb.AppendLine($"    /// 本文件会被重新生成覆盖（头壳已声明禁手工改）。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    [CfgEditor(typeof({ns}.{className}))]");
            sb.AppendLine($"    public class {className}Editor : BaseConfigEditor<{ns}.{className}>");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var absoluteEditorDir = Path.Combine(Directory.GetCurrentDirectory(), editorDir);
            Directory.CreateDirectory(absoluteEditorDir);

            var editorPath = $"{editorDir}/{className}Editor.cs";
            // 始终覆盖：本文件带「自动生成」禁改头，语义上就不允许手工扩展；若「已存在则不覆盖」，
            // 命名空间 root 切换后它会永远停在旧命名空间，[CfgEditor] 指向的类型也随之失效。
            WriteAndImport(editorPath, sb.ToString());
        }

        /// <summary>旧生成文件路径（lastGeneratedClassName 对应的 .cs），不存在返回 null。</summary>
        public static string GetOldGeneratedFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.lastGeneratedClassName))
                return null;
            if (template.lastGeneratedClassName == template.className)
                return null;
            var path = $"{ConfigDataSettings.OutputDir}/{template.lastGeneratedClassName}.cs";
            return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), path)) ? path : null;
        }

        /// <summary>
        /// 处理类名改名：保持链接（重命名旧文件保留 GUID）或断开链接（删除旧文件）。
        /// 返回 false 表示未完成（调用方应中止生成）。
        /// </summary>
        public static bool HandleClassNameRename(ConfigTemplate template, bool keepLink, out string error)
        {
            error = null;
            var oldPath = GetOldGeneratedFile(template);
            if (oldPath == null)
                return true; // 无旧文件，无需处理

            if (keepLink)
            {
                // 保持链接：重命名旧 .cs 保留 .meta（GUID 不变），资产引用不断
                var newPath = $"{ConfigDataSettings.OutputDir}/{template.className}.cs";
                var result = AssetDatabase.MoveAsset(oldPath, newPath);
                if (!string.IsNullOrEmpty(result))
                {
                    error = $"重命名旧文件失败: {result}";
                    return false;
                }
            }
            else
            {
                // 断开链接：删除旧文件（已有资产脚本引用将丢失）
                if (!AssetDatabase.DeleteAsset(oldPath))
                {
                    error = "删除旧生成文件失败";
                    return false;
                }
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Creates a new empty template asset under <paramref name="folder"/> and returns it.
        /// <para>folder 为空（或不是有效文件夹）时自动归置到「配置模板」类型文件夹
        /// （根目录/配置模板，没有则创建，见 <see cref="ConfigAssetLayout"/>）。</para>
        /// </summary>
        public static ConfigTemplate CreateTemplateAsset(string folder = null)
        {
            ConfigDataSettings.EnsureRootFolder();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                folder = ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder;

            // 文件名用 displayName（净化后），保证对象选择器里可区分
            var name = DisplayNameToAssetName("分类/新配置");
            var path = folder + "/" + name + ".asset";
            var index = 1;
            while (File.Exists(path))
            {
                path = $"{folder}/{name} {index}.asset";
                index++;
            }

            var tpl = ScriptableObject.CreateInstance<ConfigTemplate>();
            tpl.displayName = "分类/新配置";
            tpl.className = "NewConfig";
            // 默认基类 = ConfigBase（框架级，保证所有配置带 ID/GUID）
            tpl.baseTemplate = GetConfigBaseTemplate();
            AssetDatabase.CreateAsset(tpl, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return tpl;
        }

        /// <summary>All saveable template assets under the config data root, sorted by class name。</summary>
        public static ConfigTemplate[] LoadAllTemplates()
        {
            return LoadAllTemplates(true);
        }

        /// <summary>
        /// 同 <see cref="LoadAllTemplates()"/>，但可选择不创建根目录。
        /// 纯校验路径必须传 <c>ensureFolder: false</c>——校验本身不该在磁盘上留下目录。
        /// 根目录不存在时直接返回框架级模板（此时也不存在用户模板，查重结果本就正确）。
        /// </summary>
        public static ConfigTemplate[] LoadAllTemplates(bool ensureFolder)
        {
            if (ensureFolder)
                ConfigDataSettings.EnsureRootFolder();
            var result = new List<ConfigTemplate>();
            var root = ConfigDataSettings.RootFolder;
            if (AssetDatabase.IsValidFolder(root))
            {
                var guids = AssetDatabase.FindAssets("t:ConfigTemplate", new[] { root });
                result.AddRange(guids
                    .Select(g => AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(t => t != null));
            }

            // 框架级模板（如 ConfigBase）
            var frameworkGuids = AssetDatabase.FindAssets("t:ConfigTemplate", new[] { FrameworkTemplatesFolder });
            foreach (var g in frameworkGuids)
            {
                var t = AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null && !result.Contains(t))
                    result.Add(t);
            }

            return result.OrderBy(t => t.className).ToArray();
        }

        /// <summary>
        /// 类名全局唯一性检查（模板自身与其编辑器面板共用这一份逻辑）。
        /// 返回占用同名 className 的其它模板；无冲突返回 null。不创建根目录。
        /// </summary>
        public static ConfigTemplate FindClassNameConflict(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return null;
            foreach (var other in LoadAllTemplates(false))
            {
                if (other != template && other.className == template.className)
                    return other;
            }
            return null;
        }

        /// <summary>框架级模板目录（ConfigBase 等，用户不可改）。</summary>
        public const string FrameworkTemplatesFolder = "Packages/com.hyc.framework/Editor/DataEditor/FrameworkTemplates";

        /// <summary>获取框架级 ConfigBase 模板（所有配置的默认基类），不存在返回 null。</summary>
        public static ConfigTemplate GetConfigBaseTemplate()
        {
            var guids = AssetDatabase.FindAssets("t:ConfigTemplate", new[] { FrameworkTemplatesFolder });
            foreach (var g in guids)
            {
                var t = AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null && t.className == "ConfigBase")
                    return t;
            }
            return null;
        }

        /// <summary>生成客户端/服务器类型文件（Client/Server 子目录）。</summary>
        private static void WriteSideFiles(ConfigTemplate template, bool forClient, out string error)
        {
            error = null;
            var code = GenerateSideType(template, forClient, out error);
            if (code == null)
                return;

            var side = forClient ? "Client" : "Server";
            var dir = $"{ConfigDataSettings.OutputDir}/{side}";
            var absoluteDir = Path.Combine(Directory.GetCurrentDirectory(), dir);
            Directory.CreateDirectory(absoluteDir);

            var path = $"{dir}/{template.className}{side}.cs";
            WriteAndImport(path, code);
        }

        /// <summary>生成 XxxConfigCheck.cs：按字段检查规则收集错误。</summary>
        private static void WriteCheckFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;

            var editorDir = ConfigDataSettings.EditorDir;
            if (string.IsNullOrEmpty(editorDir))
                return;

            var className = template.className;
            var ns = ConfigDataSettings.Namespace;

            // 仅自身字段的检查规则：基类字段由基类导出类的 CheckData（base.CheckData 向上链）负责，
            // 与主类 / 侧类型 / 导出类的分层继承保持一致——每层只查自己那层，不会重复报错。
            var allFields = template.fields;

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using HYC.Framework.Config.Editor;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Editor");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 配置检查：按{className}自身字段的检查规则（非空/非0/范围）收集错误；");
            sb.AppendLine($"    /// 基类字段由基类导出类的 CheckData 链负责（每层只查自己那层）。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static class {className}Check");
            sb.AppendLine("    {");
            sb.AppendLine($"        public static List<CheckError> Check({ns}.{className} data)");
            sb.AppendLine("        {");
            sb.AppendLine("            var errors = new List<CheckError>();");
            sb.AppendLine("            if (data == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                errors.Add(new CheckError(data, \"" + className + "\", CheckErrorLevel.Error, \"数据为空\"));");
            sb.AppendLine("                return errors;");
            sb.AppendLine("            }");

            foreach (var f in allFields)
            {
                var isString = f.type == ConfigFieldType.String || f.type == ConfigFieldType.LocalizedKey
                    || f.type == ConfigFieldType.Addressable || f.type == ConfigFieldType.AudioEvent;
                var isNumeric = IsNumericField(f.type);

                // 多语言 key 存在性检查（反射调 loc，未安装跳过）
                if (f.type == ConfigFieldType.LocalizedKey)
                {
                    var src = f.isList ? "k" : $"data.{f.name}";
                    if (f.isList)
                    {
                        sb.AppendLine($"            foreach (var k in data.{f.name})");
                        sb.AppendLine("            {");
                    }
                    sb.AppendLine($"            if (!string.IsNullOrEmpty({src}))");
                    sb.AppendLine("            {");
                    sb.AppendLine("                HYC.Framework.Config.Editor.LocAccess.EnsureLoaded();");
                    sb.AppendLine("                var locType = System.Type.GetType(\"HYC.Framework.Loc.LocalizationManager, HYC.Framework.Runtime\");");
                    sb.AppendLine("                if (locType != null)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    var hasKey = locType.GetMethod(\"HasKey\", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);");
                    sb.AppendLine($"                    if (hasKey != null && !(bool)hasKey.Invoke(null, new object[] {{ {src} }}))");
                    sb.AppendLine($"                        errors.Add(new CheckError(data, \"{className}\", CheckErrorLevel.Error, \"字段 {f.name} 的多语言 key 不存在\", \"{f.name}\"));");
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                    if (f.isList)
                        sb.AppendLine("            }");
                }

                // 非空检查（string 族用 IsNullOrEmpty；列表用 null/Count==0；引用/对象/行为树用 == default；值类型无空态，不发射）
                if (f.notEmptyCheck != ConfigCheckLevel.None && SupportsNotEmptyField(f.type))
                {
                    var lvl = f.notEmptyCheck == ConfigCheckLevel.Info ? "CheckErrorLevel.Info"
                        : f.notEmptyCheck == ConfigCheckLevel.Warning ? "CheckErrorLevel.Warning"
                        : "CheckErrorLevel.Error";
                    if (f.isList)
                        sb.AppendLine($"            if (data.{f.name} == null || data.{f.name}.Count == 0)");
                    else if (isString)
                        sb.AppendLine($"            if (string.IsNullOrEmpty(data.{f.name}))");
                    else
                        sb.AppendLine($"            if (data.{f.name} == default)");
                    sb.AppendLine($"                errors.Add(new CheckError(data, \"{className}\", {lvl}, \"字段 {f.name} 不能为空\", \"{f.name}\"));");
                }
                // 非0检查（仅数值）
                if (isNumeric && f.notZeroCheck != ConfigCheckLevel.None)
                {
                    var lvl = f.notZeroCheck == ConfigCheckLevel.Info ? "CheckErrorLevel.Info"
                        : f.notZeroCheck == ConfigCheckLevel.Warning ? "CheckErrorLevel.Warning"
                        : "CheckErrorLevel.Error";
                    sb.AppendLine($"            if (data.{f.name} == 0)");
                    sb.AppendLine($"                errors.Add(new CheckError(data, \"{className}\", {lvl}, \"字段 {f.name} 不能为0\", \"{f.name}\"));");
                }
                // 范围检查（仅数值）
                if (isNumeric && f.rangeCheck != ConfigCheckLevel.None && f.hasRange)
                {
                    var lvl = f.rangeCheck == ConfigCheckLevel.Info ? "CheckErrorLevel.Info"
                        : f.rangeCheck == ConfigCheckLevel.Warning ? "CheckErrorLevel.Warning"
                        : "CheckErrorLevel.Error";
                    sb.AppendLine($"            if (data.{f.name} < {f.minValue} || data.{f.name} > {f.maxValue})");
                    sb.AppendLine($"                errors.Add(new CheckError(data, \"{className}\", {lvl}, \"字段 {f.name} 超出范围 [{f.minValue}~{f.maxValue}]\", \"{f.name}\"));");
                }
            }

            sb.AppendLine("            return errors;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var absoluteEditorDir = Path.Combine(Directory.GetCurrentDirectory(), editorDir);
            Directory.CreateDirectory(absoluteEditorDir);
            var path = $"{editorDir}/{className}Check.cs";
            WriteAndImport(path, sb.ToString());
        }

        /// <summary>生成 XxxExport.cs：检查 → 无错 → 按设置格式导出客户端/服务器。
        /// 导出类继承链与配置主类链一致（直接基类是宿主模板 → 继承其导出类；是框架级基类 →
        /// 继承框架 {Base}Export；无基类 → 框架 ConfigBaseExport）。导出管线由基类负责，本类只填类型相关钩子。</summary>
        private static void WriteExportFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;

            var editorDir = ConfigDataSettings.EditorDir;
            if (string.IsNullOrEmpty(editorDir))
                return;

            var className = template.className;
            var ns = ConfigDataSettings.Namespace;
            var clientIsBlob = ConfigDataSettings.ClientFormat == 1 && GetExportFields(template, true).Length > 0;

            // 导出链基类：与配置主类链对齐（chain[0] 是直接基类模板）。
            // 宿主生成的基类 → 其导出类（同命名空间）；框架级基类（generateCode=false，如 ConfigBase）
            // → 框架手写的 {Base}Export（HYC.Framework.Config.Editor，全限定名引用）；无基类 → 框架 ConfigBaseExport。
            var chain = new List<ConfigTemplate>();
            var baseExportName = "ConfigBaseExport";
            if (TryBuildInheritanceChain(template, chain, out _) && chain.Count > 0)
            {
                baseExportName = chain[0].generateCode
                    ? $"{chain[0].className}Export"
                    : $"HYC.Framework.Config.Editor.{chain[0].className}Export";
            }

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using HYC.Framework.Config;");
            sb.AppendLine("using HYC.Framework.Config.Editor;");
            sb.AppendLine("using UnityEditor;");
            if (clientIsBlob)
            {
                sb.AppendLine("using System.IO;");
                sb.AppendLine("using Unity.Collections;");
                sb.AppendLine("using Unity.Entities;");
                sb.AppendLine("using Unity.Entities.Serialization;");
                sb.AppendLine("using UnityEngine;");
                sb.AppendLine("using " + ns + ".Blob;");
            }
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Editor");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 导出：先检查，无错误后按设置格式导出客户端/服务器。");
            sb.AppendLine($"    /// 导出管线（检查→客户端→服务器→落盘）由基类 {baseExportName} 负责；");
            sb.AppendLine($"    /// 本类只填 {className} 类型相关的钩子（检查 / DTO 投影 / Blob 分支）。");
            sb.AppendLine("    /// </summary>");
            // InitializeOnLoad 只能修饰 class（CS0592）：它的语义是"Unity 在程序集重载后
            // 调用本类的静态构造器"，所以必须挂类上而不是挂静态构造器上。
            sb.AppendLine("    [UnityEditor.InitializeOnLoad]");
            sb.AppendLine($"    public class {className}Export : {baseExportName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        /// <summary>向 ConfigExportService 注册本导出类，使查找走注册表而无需扫描程序集。</summary>");
            sb.AppendLine($"        static {className}Export() => ConfigExportService.Register(typeof({ns}.{className}), typeof({className}Export));");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>构造并声明本导出类对应的配置类型（基类据此收集资产、命名产物文件）。</summary>");
            sb.AppendLine($"        public {className}Export(Type configType) : base(configType) {{ }}");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>检查：基类链各自负责自身字段（base.CheckData 向上链），本类补 {className} 自身字段。</summary>");
            sb.AppendLine($"        protected override List<CheckError> CheckData(ConfigBase data)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var errors = base.CheckData(data);");
            sb.AppendLine($"            errors.AddRange({className}Check.Check(({ns}.{className})data));");
            sb.AppendLine("            return errors;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>客户端 DTO 投影：侧类型继承链已含基类字段，一次 From 整链投影。</summary>");
            sb.AppendLine($"        protected override object CreateClientDto(ConfigBase data)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var d = ({ns}.{className})data;");
            sb.AppendLine($"            var client = {ns}.Client.{className}Client.From(d);");
            AppendJsonAssetOverrides(sb, template, true, "client", "d", "            ");
            sb.AppendLine("            return client;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>服务器 DTO 投影：同上。</summary>");
            sb.AppendLine($"        protected override object CreateServerDto(ConfigBase data)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var d = ({ns}.{className})data;");
            sb.AppendLine($"            var server = {ns}.Server.{className}Server.From(d);");
            AppendJsonAssetOverrides(sb, template, false, "server", "d", "            ");
            sb.AppendLine("            return server;");
            sb.AppendLine("        }");
            if (clientIsBlob)
            {
                sb.AppendLine();
                sb.AppendLine($"        /// <summary>客户端为 Blob 格式（生成期按设置烘焙）。</summary>");
                sb.AppendLine($"        protected override bool ClientIsBlob => true;");
                sb.AppendLine();
                sb.AppendLine($"        /// <summary>Blob 客户端导出：构建 Cfg{className} 表（类型相关，无法上收基类）。</summary>");
                sb.AppendLine($"        protected override void WriteClientBlob(string dir, IList<ConfigBase> assets)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var builder = new Unity.Entities.BlobBuilder(Unity.Collections.Allocator.Temp);");
                sb.AppendLine($"            ref var root = ref builder.ConstructRoot<HYC.Framework.Config.BlobRoot<{ns}.Blob.Cfg{className}>>();");
                sb.AppendLine($"            var arr = builder.Allocate(ref root.Rows, assets.Count);");
                sb.AppendLine($"            for (var i = 0; i < assets.Count; i++)");
                sb.AppendLine($"                {ns}.Blob.{className}BlobBuilder.Write(ref builder, ref arr[i], ({ns}.{className})assets[i]);");
                sb.AppendLine($"            var path = System.IO.Path.Combine(dir, \"Cfg{className}.blob\");");
                sb.AppendLine($"            Unity.Entities.BlobAssetReference<HYC.Framework.Config.BlobRoot<{ns}.Blob.Cfg{className}>>.Write(builder, path, 0);");
                sb.AppendLine("            builder.Dispose();");
                sb.AppendLine("        }");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var absoluteEditorDir = Path.Combine(Directory.GetCurrentDirectory(), editorDir);
            Directory.CreateDirectory(absoluteEditorDir);
            var path = $"{editorDir}/{className}Export.cs";
            WriteAndImport(path, sb.ToString());
        }

    }
}

