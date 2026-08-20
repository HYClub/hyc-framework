using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Generates a C# <c>[CfgAsset]</c> config class from a <see cref="ConfigTemplate"/>.
    /// Output lands in <c>Assets/GeneratedConfigs</c> under a fixed namespace so
    /// the new type is picked up by <see cref="ConfigCreateWindow"/> immediately
    /// after a compile.
    /// </summary>
    public static class ConfigTemplateCodeGen
    {
        public const string LegacyOutputDir = "Assets/GeneratedConfigs";
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
            foreach (var other in LoadAllTemplates())
            {
                if (other == template)
                    continue;
                if (other.className == template.className)
                {
                    error = $"类名 {template.className} 已被其他模板使用（{AssetDatabase.GetAssetPath(other)}）";
                    return false;
                }
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

            return true;
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

                // 多语言 key：加 [LocKey] 特性（数据编辑器据此绘制联想/选择/tooltip/校验）
                if (field.type == ConfigFieldType.LocalizedKey)
                    fieldLines.AppendLine("        [LocKey]");

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

            var sb = new StringBuilder();
            foreach (var u in usings)
                sb.AppendLine($"using {u};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ConfigDataSettings.Namespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    [CfgAsset(\"{Escape(template.displayName)}\", 0)]");
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
                    if (MatchesTarget(f.exportTarget, forClient))
                        list.Add(f);
                }
            }
            foreach (var f in template.fields)
            {
                if (MatchesTarget(f.exportTarget, forClient))
                    list.Add(f);
            }
            return list.ToArray();
        }

        /// <summary>是否为数值字段类型（可配 Min/Max）。</summary>
        private static bool IsNumericField(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Int:
                case ConfigFieldType.Long:
                case ConfigFieldType.Float:
                case ConfigFieldType.Double:
                case ConfigFieldType.Short:
                case ConfigFieldType.Byte:
                case ConfigFieldType.UInt:
                case ConfigFieldType.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static bool MatchesTarget(ConfigExportTarget target, bool forClient)
        {
            return target == ConfigExportTarget.Both
                || (forClient && target == ConfigExportTarget.Client)
                || (!forClient && target == ConfigExportTarget.Server);
        }

        /// <summary>
        /// 生成客户端/服务器类型代码。struct 时字段平铺（子含父全部字段）；
        /// class 时保持继承链（仅声明自身字段，From 调用父类 From 再赋自身字段）。
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
                if (MatchesTarget(f.exportTarget, forClient))
                    ownFields.Add(f);
            }

            // From 赋值用全部导出字段：继承链字段（顶层→直接基类）+ 自身
            var flatFields = new List<ConfigTemplateField>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var f in chain[i].fields)
                {
                    if (MatchesTarget(f.exportTarget, forClient))
                        flatFields.Add(f);
                }
            }
            flatFields.AddRange(ownFields);

            var declaredFields = isStruct ? flatFields : ownFields;

            var fieldLines = new StringBuilder();
            foreach (var field in declaredFields)
            {
                var typeName = ResolveTypeName(field, usings, ref needsUnity, ref needsCollections);
                if (typeName == null)
                {
                    error = $"字段 {field.name} 的引用类型无法解析（可能已删除）";
                    return null;
                }
                fieldLines.AppendLine($"        public {typeName} {field.name};");
            }

            if (needsUnity)
                usings.Add("UnityEngine");
            if (needsCollections)
                usings.Add("System.Collections.Generic");

            var baseSideName = chain.Count > 0 ? $"{chain[0].className}{side}" : null;

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

            // From 赋值方法：始终平铺赋值全部导出字段（Editor 类字段公开可访问）
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>从编辑器配置类赋值（仅导出目标匹配的字段，含继承字段）。</summary>");
            sb.AppendLine($"        public static {className}{side} From({ns}.{className} editor)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var r = new {className}{side}();");
            foreach (var f in flatFields)
                sb.AppendLine($"            r.{f.name} = editor.{f.name};");
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

        /// <summary>编辑器配置类型 → Blob 类型名（string→BlobString, List→BlobArray）。</summary>
        private static string BlobTypeName(ConfigTemplateField f, out bool isString, out bool isArray)
        {
            isString = false;
            isArray = false;

            string baseName;
            switch (f.type)
            {
                case ConfigFieldType.String: baseName = "BlobString"; isString = true; break;
                case ConfigFieldType.LocalizedKey: baseName = "BlobString"; isString = true; break;
                case ConfigFieldType.Int: baseName = "int"; break;
                case ConfigFieldType.Long: baseName = "long"; break;
                case ConfigFieldType.Float: baseName = "float"; break;
                case ConfigFieldType.Double: baseName = "double"; break;
                case ConfigFieldType.Bool: baseName = "bool"; break;
                case ConfigFieldType.Short: baseName = "short"; break;
                case ConfigFieldType.Byte: baseName = "byte"; break;
                case ConfigFieldType.UInt: baseName = "uint"; break;
                default:
                    // 其他类型 Blob 暂不支持，退回 int 占位
                    baseName = "int";
                    break;
            }

            if (f.isList)
            {
                isArray = true;
                return $"BlobArray<{baseName}>";
            }
            return baseName;
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
                if (f.isList)
                {
                    sb.AppendLine($"            var arr{f.name} = builder.Allocate(ref target.{f.name}, data.{f.name}.Count);");
                    if (f.type == ConfigFieldType.Enum)
                        sb.AppendLine($"            for (var i = 0; i < data.{f.name}.Count; i++) arr{f.name}[i] = (int)data.{f.name}[i];");
                    else if (f.type == ConfigFieldType.String || f.type == ConfigFieldType.LocalizedKey)
                        sb.AppendLine($"            for (var i = 0; i < data.{f.name}.Count; i++) builder.AllocateString(ref arr{f.name}[i], data.{f.name}[i]);");
                    else
                        sb.AppendLine($"            for (var i = 0; i < data.{f.name}.Count; i++) arr{f.name}[i] = data.{f.name}[i];");
                }
                else if (f.type == ConfigFieldType.String || f.type == ConfigFieldType.LocalizedKey)
                    sb.AppendLine($"            builder.AllocateString(ref target.{f.name}, data.{f.name});");
                else if (f.type == ConfigFieldType.Enum)
                    sb.AppendLine($"            target.{f.name} = (int)data.{f.name};");
                else
                    sb.AppendLine($"            target.{f.name} = data.{f.name};");
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
                        var type = Type.GetType(field.refTypeFullName);
                        if (type == null)
                            return null;
                        if (!string.IsNullOrEmpty(type.Namespace))
                            usings.Add(type.Namespace);
                        baseName = type.Name;
                        break;
                    }

                    if (field.type == ConfigFieldType.Enum)
                    {
                        var e = ConfigEnumCodeGen.FindEnum(field.enumRefClassName);
                        if (e == null)
                            return null;
                        baseName = e.className;
                        usings.Add(ConfigDataSettings.Namespace);
                        break;
                    }

                    if (field.type == ConfigFieldType.LocalizedKey)
                    {
                        baseName = "string";
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
            var code = GenerateCode(template, out error);
            if (code == null)
                return false;

            var absoluteDir = Path.Combine(Directory.GetCurrentDirectory(), ConfigDataSettings.OutputDir);
            Directory.CreateDirectory(absoluteDir);

            var relativePath = $"{ConfigDataSettings.OutputDir}/{template.className}.cs";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), relativePath), code, Encoding.UTF8);

            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);

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

            AssetDatabase.Refresh();

            // 记录本次生成类名
            template.lastGeneratedClassName = template.className;
            EditorUtility.SetDirty(template);
            return true;
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
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), path), code, Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>生成 Blob 构建器文件（ns.Blob / {className}BlobBuilder.cs）。</summary>
        private static void WriteBlobBuilderFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;
            var code = GenerateBlobBuilder(template, out _);
            if (code == null)
                return;
            var dir = $"{ConfigDataSettings.OutputDir}/Blob";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), dir));
            var path = $"{dir}/{template.className}BlobBuilder.cs";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), path), code, Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>生成 Blob 运行时加载 System + 查询扩展（运行时把 .blob 挂进 ConfigManager）。</summary>
        private static void WriteBlobSystemFile(ConfigTemplate template)
        {
            if (template == null || string.IsNullOrEmpty(template.className))
                return;
            var className = template.className;
            var ns = ConfigDataSettings.Namespace;

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using Unity.Collections;");
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine("using Unity.Entities.Serialization;");
            sb.AppendLine("using HYC.Framework.Config;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Blob");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// 加载 Cfg{className}.blob 并注册进 ConfigManager，提供 GetCfg{className}(id) 查询。");
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
            sb.AppendLine($"            var path = Path.Combine(Application.streamingAssetsPath, \"{ConfigDataSettings.BlobLoadPath}\", \"Cfg{className}.blob\");");
            sb.AppendLine("            if (!File.Exists(path))");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogWarning($\"Blob file not found: {path}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine($"            if (BlobAssetReference<BlobRoot<Cfg{className}>>.TryRead(path, 0, out var blob))");
            sb.AppendLine("            {");
            // 按模板是否有 ID 字段决定索引方式
            var hasIdField = GetExportFields(template, true).Any(f => f.name == "ID");
            if (hasIdField)
                sb.AppendLine($"                var table = ConfigBlobTable<Cfg{className}>.FromBlob(blob, r => r.ID);");
            else
                sb.AppendLine($"                var table = ConfigBlobTable<Cfg{className}>.FromBlob(blob);");
            sb.AppendLine($"                ConfigManager.Register(table);");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogError($\"Failed to read blob: {path}\");");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Blob");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class {className}Extensions");
            sb.AppendLine("    {");
            sb.AppendLine($"        /// <summary>按 ID 查询 {className} 配置。</summary>");
            sb.AppendLine($"        public static Cfg{className} GetCfg{className}(this World world, long id)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return ConfigManager.TryGet<Cfg{className}>(id, out var row) ? row : default;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>查询全部 {className} 配置。</summary>");
            sb.AppendLine($"        public static BlobArray<Cfg{className}> GetCfg{className}List(this World world)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return ConfigManager.GetAllRows<Cfg{className}>();");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var dir = $"{ConfigDataSettings.OutputDir}/Blob";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), dir));
            var path = $"{dir}/{className}BlobSystem.cs";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), path), sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
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
            sb.AppendLine($"    /// {className} 的配置编辑器。继承默认分组渲染；如需自定义请在此扩展。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    [CfgEditor(typeof({ns}.{className}))]");
            sb.AppendLine($"    public class {className}Editor : BaseConfigEditor<{ns}.{className}>");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var absoluteEditorDir = Path.Combine(Directory.GetCurrentDirectory(), editorDir);
            Directory.CreateDirectory(absoluteEditorDir);

            var editorPath = $"{editorDir}/{className}Editor.cs";
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), editorPath);

            // 已存在则覆盖（保持用户对文件的修改？不——重新生成覆盖，用户扩展内容应写在类内但会被冲掉。
            // 简单方案：已存在则不覆盖，避免用户自定义丢失。首次生成创建。）
            if (File.Exists(fullPath))
                return;

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(editorPath, ImportAssetOptions.ForceUpdate);
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

        /// <summary>Creates a new empty template asset under <paramref name="folder"/> and returns it.</summary>
        public static ConfigTemplate CreateTemplateAsset(string folder)
        {
            ConfigDataSettings.EnsureRootFolder();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                folder = ConfigDataSettings.RootFolder;

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

        /// <summary>All saveable template assets under the config data root, sorted by class name.</summary>
        public static ConfigTemplate[] LoadAllTemplates()
        {
            ConfigDataSettings.EnsureRootFolder();
            var guids = AssetDatabase.FindAssets("t:ConfigTemplate", new[] { ConfigDataSettings.RootFolder });
            var result = new List<ConfigTemplate>(guids
                .Select(g => AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null));

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

        /// <summary>框架级模板目录（ConfigBase 等，用户不可改）。</summary>
        public const string FrameworkTemplatesFolder = "Packages/com.hyc.framework.config/Editor/DataEditor/FrameworkTemplates";

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
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), path), code, Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
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

            // 继承链全部字段（含检查规则）
            var allFields = new List<ConfigTemplateField>();
            var chain = new List<ConfigTemplate>();
            if (TryBuildInheritanceChain(template, chain, out _))
            {
                for (var i = chain.Count - 1; i >= 0; i--)
                    allFields.AddRange(chain[i].fields);
            }
            allFields.AddRange(template.fields);

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using HYC.Framework.Config.Editor;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Editor");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 配置检查：按字段检查规则（非空/非0）收集错误。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static class {className}Check");
            sb.AppendLine("    {");
            sb.AppendLine($"        public static List<CheckError> Check({ns}.{className} data)");
            sb.AppendLine("        {");
            sb.AppendLine("            var errors = new List<CheckError>();");
            sb.AppendLine("            if (data == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                errors.Add(new CheckError(null, \"" + className + "\", CheckErrorLevel.Error, \"数据为空\"));");
            sb.AppendLine("                return errors;");
            sb.AppendLine("            }");

            foreach (var f in allFields)
            {
                var isString = f.type == ConfigFieldType.String || f.type == ConfigFieldType.LocalizedKey;
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
                    sb.AppendLine($"                        errors.Add(new CheckError(null, \"{className}\", CheckErrorLevel.Error, \"字段 {f.name} 的多语言 key 不存在\", \"{f.name}\"));");
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                    if (f.isList)
                        sb.AppendLine("            }");
                }

                // 非空检查（string 用 IsNullOrEmpty；数值用 == default）
                if (f.notEmptyCheck != ConfigCheckLevel.None)
                {
                    var lvl = f.notEmptyCheck == ConfigCheckLevel.Info ? "CheckErrorLevel.Info"
                        : f.notEmptyCheck == ConfigCheckLevel.Warning ? "CheckErrorLevel.Warning"
                        : "CheckErrorLevel.Error";
                    if (isString)
                        sb.AppendLine($"            if (string.IsNullOrEmpty(data.{f.name}))");
                    else
                        sb.AppendLine($"            if (data.{f.name} == default)");
                    sb.AppendLine($"                errors.Add(new CheckError(null, \"{className}\", {lvl}, \"字段 {f.name} 不能为空\", \"{f.name}\"));");
                }
                // 非0检查（仅数值）
                if (isNumeric && f.notZeroCheck != ConfigCheckLevel.None)
                {
                    var lvl = f.notZeroCheck == ConfigCheckLevel.Info ? "CheckErrorLevel.Info"
                        : f.notZeroCheck == ConfigCheckLevel.Warning ? "CheckErrorLevel.Warning"
                        : "CheckErrorLevel.Error";
                    sb.AppendLine($"            if (data.{f.name} == 0)");
                    sb.AppendLine($"                errors.Add(new CheckError(null, \"{className}\", {lvl}, \"字段 {f.name} 不能为0\", \"{f.name}\"));");
                }
                // 范围检查（仅数值）
                if (isNumeric && f.rangeCheck != ConfigCheckLevel.None && f.hasRange)
                {
                    var lvl = f.rangeCheck == ConfigCheckLevel.Info ? "CheckErrorLevel.Info"
                        : f.rangeCheck == ConfigCheckLevel.Warning ? "CheckErrorLevel.Warning"
                        : "CheckErrorLevel.Error";
                    sb.AppendLine($"            if (data.{f.name} < {f.minValue} || data.{f.name} > {f.maxValue})");
                    sb.AppendLine($"                errors.Add(new CheckError(null, \"{className}\", {lvl}, \"字段 {f.name} 超出范围 [{f.minValue}~{f.maxValue}]\", \"{f.name}\"));");
                }
            }

            sb.AppendLine("            return errors;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var absoluteEditorDir = Path.Combine(Directory.GetCurrentDirectory(), editorDir);
            Directory.CreateDirectory(absoluteEditorDir);
            var path = $"{editorDir}/{className}Check.cs";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), path), sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>生成 XxxConfigExport.cs：检查 → 无错 → 按设置格式导出客户端/服务器。</summary>
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

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using Newtonsoft.Json;");
            sb.AppendLine("using HYC.Framework.Config.Editor;");
            sb.AppendLine("using UnityEditor;");
            if (clientIsBlob)
            {
                sb.AppendLine("using Unity.Collections;");
                sb.AppendLine("using Unity.Entities;");
                sb.AppendLine("using Unity.Entities.Serialization;");
                sb.AppendLine("using " + ns + ".Blob;");
            }
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Editor");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {className} 导出：先检查，无错误后按设置格式导出客户端/服务器。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static class {className}Export");
            sb.AppendLine("    {");
            sb.AppendLine($"        /// <summary>导出单个配置实例。返回 true 表示成功。</summary>");
            sb.AppendLine($"        public static bool Export({ns}.{className} data, bool exportClient, bool exportServer)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var errors = {className}Check.Check(data);");
            sb.AppendLine("            if (errors.Count > 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                BuildErrorWindow.OpenWindow(errors);");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            var ok = true;");
            sb.AppendLine("            if (exportClient) ok &= ExportClient(data);");
            sb.AppendLine("            if (exportServer) ok &= ExportServer(data);");
            sb.AppendLine("            AssetDatabase.Refresh();");
            sb.AppendLine("            return ok;");
            sb.AppendLine("        }");
            sb.AppendLine();
            // 批量导出全部实例
            sb.AppendLine($"        /// <summary>批量导出该类型全部配置实例（客户端+服务器）。返回 true 表示成功。</summary>");
            sb.AppendLine($"        public static bool ExportAll(bool exportClient, bool exportServer)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var all = AssetDatabase.FindAssets(\"t:{ns}.{className}\");");
            sb.AppendLine("            var ok = true;");
            if (clientIsBlob)
            {
                sb.AppendLine("            if (exportClient)");
                sb.AppendLine("            {");
                sb.AppendLine("                var dir = ConfigDataSettings.ClientExportDir;");
                sb.AppendLine("                if (string.IsNullOrEmpty(dir)) return false;");
                sb.AppendLine("                Directory.CreateDirectory(dir);");
                sb.AppendLine();
                sb.AppendLine("                var builder = new Unity.Entities.BlobBuilder(Unity.Collections.Allocator.Temp);");
                sb.AppendLine($"                ref var root = ref builder.ConstructRoot<HYC.Framework.Config.BlobRoot<{ns}.Blob.Cfg{className}>>();");
                sb.AppendLine("                var arr = builder.Allocate(ref root.Rows, all.Length);");
                sb.AppendLine("                for (var i = 0; i < all.Length; i++)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    var asset = AssetDatabase.LoadAssetAtPath<{ns}.{className}>(AssetDatabase.GUIDToAssetPath(all[i]));");
                sb.AppendLine("                    if (asset == null) continue;");
                sb.AppendLine($"                    var errs = {className}Check.Check(asset);");
                sb.AppendLine("                    if (errs.Count > 0) { BuildErrorWindow.OpenWindow(errs); builder.Dispose(); return false; }");
                sb.AppendLine($"                    {ns}.Blob.{className}BlobBuilder.Write(ref builder, ref arr[i], asset);");
                sb.AppendLine("                }");
                sb.AppendLine($"                var path = Path.Combine(dir, \"Cfg{className}.blob\");");
                sb.AppendLine($"                Unity.Entities.BlobAssetReference<HYC.Framework.Config.BlobRoot<{ns}.Blob.Cfg{className}>>.Write(builder, path, 0);");
                sb.AppendLine("                builder.Dispose();");
                sb.AppendLine("            }");
            }
            else
            {
                sb.AppendLine("            if (exportClient)");
                sb.AppendLine("            {");
                sb.AppendLine("                var dir = ConfigDataSettings.ClientExportDir;");
                sb.AppendLine("                if (string.IsNullOrEmpty(dir)) return false;");
                sb.AppendLine("                Directory.CreateDirectory(dir);");
                sb.AppendLine("                for (var i = 0; i < all.Length; i++)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    var asset = AssetDatabase.LoadAssetAtPath<{ns}.{className}>(AssetDatabase.GUIDToAssetPath(all[i]));");
                sb.AppendLine("                    if (asset == null) continue;");
                sb.AppendLine($"                    var errs = {className}Check.Check(asset);");
                sb.AppendLine("                    if (errs.Count > 0) { BuildErrorWindow.OpenWindow(errs); return false; }");
                sb.AppendLine($"                    var client = {ns}.Client.{className}Client.From(asset);");
                sb.AppendLine("                    var json = JsonConvert.SerializeObject(client, Formatting.Indented);");
                sb.AppendLine("                    File.WriteAllText(Path.Combine(dir, asset.name + \".json\"), json);");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
            sb.AppendLine("            if (exportServer)");
            sb.AppendLine("            {");
            sb.AppendLine("                var dir = ConfigDataSettings.ServerExportDir;");
            sb.AppendLine("                if (string.IsNullOrEmpty(dir)) return false;");
            sb.AppendLine("                Directory.CreateDirectory(dir);");
            sb.AppendLine($"                var list = new List<{ns}.Server.{className}Server>();");
            sb.AppendLine("                for (var i = 0; i < all.Length; i++)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var asset = AssetDatabase.LoadAssetAtPath<{ns}.{className}>(AssetDatabase.GUIDToAssetPath(all[i]));");
            sb.AppendLine("                    if (asset == null) continue;");
            sb.AppendLine($"                    var errs = {className}Check.Check(asset);");
            sb.AppendLine("                    if (errs.Count > 0) { BuildErrorWindow.OpenWindow(errs); return false; }");
            sb.AppendLine($"                    list.Add({ns}.Server.{className}Server.From(asset));");
            sb.AppendLine("                }");
            sb.AppendLine("                var json = JsonConvert.SerializeObject(list, Formatting.Indented);");
            sb.AppendLine($"                File.WriteAllText(Path.Combine(dir, \"{className}.json\"), json);");
            sb.AppendLine("            }");
            sb.AppendLine("            AssetDatabase.Refresh();");
            sb.AppendLine("            return ok;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>导出客户端（按设置格式：JSON 或 Blob）。</summary>");
            sb.AppendLine($"        private static bool ExportClient({ns}.{className} data)");
            sb.AppendLine("        {");
            sb.AppendLine("            var dir = ConfigDataSettings.ClientExportDir;");
            sb.AppendLine("            if (string.IsNullOrEmpty(dir)) return false;");
            sb.AppendLine("            Directory.CreateDirectory(dir);");
            if (clientIsBlob)
            {
                sb.AppendLine("            var builder = new Unity.Entities.BlobBuilder(Unity.Collections.Allocator.Temp);");
                sb.AppendLine($"            ref var root = ref builder.ConstructRoot<HYC.Framework.Config.BlobRoot<{ns}.Blob.Cfg{className}>>();");
                sb.AppendLine($"            var arr = builder.Allocate(ref root.Rows, 1);");
                sb.AppendLine($"            {ns}.Blob.{className}BlobBuilder.Write(ref builder, ref arr[0], data);");
                sb.AppendLine($"            var path = Path.Combine(dir, \"Cfg{className}.blob\");");
                sb.AppendLine($"            Unity.Entities.BlobAssetReference<HYC.Framework.Config.BlobRoot<{ns}.Blob.Cfg{className}>>.Write(builder, path, 0);");
                sb.AppendLine("            builder.Dispose();");
                sb.AppendLine("            return true;");
            }
            else
            {
                sb.AppendLine("            var client = " + ns + ".Client." + className + "Client.From(data);");
                sb.AppendLine("            var json = JsonConvert.SerializeObject(client, Formatting.Indented);");
                sb.AppendLine($"            File.WriteAllText(Path.Combine(dir, \"{className}.json\"), json);");
                sb.AppendLine("            return true;");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>导出服务器（JSON）。</summary>");
            sb.AppendLine($"        private static bool ExportServer({ns}.{className} data)");
            sb.AppendLine("        {");
            sb.AppendLine("            var dir = ConfigDataSettings.ServerExportDir;");
            sb.AppendLine("            if (string.IsNullOrEmpty(dir)) return false;");
            sb.AppendLine("            Directory.CreateDirectory(dir);");
            sb.AppendLine("            var server = " + ns + ".Server." + className + "Server.From(data);");
            sb.AppendLine("            var json = JsonConvert.SerializeObject(server, Formatting.Indented);");
            sb.AppendLine($"            File.WriteAllText(Path.Combine(dir, \"{className}.json\"), json);");
            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var absoluteEditorDir = Path.Combine(Directory.GetCurrentDirectory(), editorDir);
            Directory.CreateDirectory(absoluteEditorDir);
            var path = $"{editorDir}/{className}Export.cs";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), path), sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

    }
}

