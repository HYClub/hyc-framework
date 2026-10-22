using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HYC.Framework.Config.Editor;
using HYC.Framework.Editor;
using HYC.Framework.SceneContent;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容类型定义 → C# 代码。
    /// <para>每个定义产出 <b>两个</b>东西：</para>
    /// <list type="number">
    ///   <item><c>{GeneratedPaths.SceneContent}/{className}.cs</c> —— 挂在 GO 上的标记组件
    ///         （实现 <see cref="ISceneContentMarker"/> + <see cref="ISceneContentMarkerPayload"/>，
    ///         字段来自定义资产里配的 <see cref="SceneContentFieldDef"/> 列表）；</item>
    ///   <item><c>{GeneratedPaths.SceneContent}/SceneContentTypes.g.cs</c> —— 全类型的 id 常量表 + id→显示名。</item>
    /// </list>
    /// <para>
    /// 生成物一律走 <see cref="GeneratedCodeFile"/> 写盘（自动套禁改壳、内容未变则跳过）。
    /// 命名空间取 <see cref="HYCSettings.SceneContentCodeNamespace"/>（<c>{root}.scene</c>）。
    /// </para>
    /// <para>
    /// 定义资产<b>不在配置数据编辑器里</b>（无 <c>[CfgAsset]</c>），所以收集按类型全工程扫，
    /// 不依赖配置根目录；默认落点 <see cref="DefaultTypeFolder"/> 也与配置目录无关。
    /// </para>
    /// </summary>
    public static class SceneContentTypeCodeGen
    {
        /// <summary>id 常量表文件名（与各类型的标记组件同目录）。</summary>
        public const string RegistryFileName = "SceneContentTypes.g.cs";

        /// <summary>新建定义资产的默认落点（与生成代码目录分开：一个是资产，一个是代码）。</summary>
        public const string DefaultTypeFolder = "Assets/HYC/SceneContentTypes";

        /// <summary>生成目录（相对工程根）。</summary>
        public static string OutputDir => GeneratedPaths.SceneContent;

        /// <summary>命名空间（由 HYCSettings 的命名空间 root 派生）。</summary>
        public static string Namespace => HYCSettings.SceneContentCodeNamespace;

        /// <summary>某个类的生成文件相对路径。</summary>
        public static string FilePathFor(string className) => $"{OutputDir}/{className}.cs";

        // ---------------------------------------------------------------- 资产收集

        /// <summary>加载工程内全部场景内容类型定义（按类型扫，与配置目录无关）。</summary>
        public static List<SceneContentTypeDef> LoadAllTypes()
        {
            var result = new List<SceneContentTypeDef>();
            foreach (var guid in AssetDatabase.FindAssets("t:SceneContentTypeDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<SceneContentTypeDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null)
                    result.Add(def);
            }
            return result;
        }

        /// <summary>类名是否已被别的定义占用。</summary>
        public static bool IsClassNameTaken(string className, SceneContentTypeDef self)
            => LoadAllTypes().Any(d => d != self && d.className == className);

        /// <summary>生成文件是否已存在。</summary>
        public static bool IsGenerated(SceneContentTypeDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.className))
                return false;
            return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), FilePathFor(def.className)));
        }

        // ---------------------------------------------------------------- 校验

        /// <summary>
        /// 校验一个定义能否生成代码。失败返回 false 并给出<b>可直接展示</b>的中文原因。
        /// </summary>
        public static bool Validate(SceneContentTypeDef def, out string error)
        {
            error = null;
            if (def == null)
            {
                error = "定义为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(def.displayName))
            {
                error = "显示名不能为空";
                return false;
            }
            if (!ConfigTemplateCodeGen.IsValidIdentifier(def.className))
            {
                error = $"类名「{def.className}」不是合法的 C# 标识符（或为关键字）";
                return false;
            }
            if (ReservedClassNames.Contains(def.className))
            {
                error = $"类名「{def.className}」被生成器自身占用（类型 id 常量表），请改名";
                return false;
            }
            if (IsClassNameTaken(def.className, def))
            {
                error = $"类名「{def.className}」已被其他场景内容类型使用";
                return false;
            }

            // id 撞车：哈希碰撞概率极低，但一旦发生必须拦下来（否则导出数据会串类型）
            var myId = def.ContentTypeId;
            foreach (var other in LoadAllTypes())
            {
                if (other == null || other == def) continue;
                if (other.ContentTypeId == myId)
                {
                    error = $"内容类型 id 与其他定义冲突（{other.className}，id={myId}），请改类名";
                    return false;
                }
            }

            var names = new HashSet<string>();
            if (def.fields != null)
            {
                for (var i = 0; i < def.fields.Count; i++)
                {
                    var f = def.fields[i];
                    if (f == null)
                    {
                        error = $"第 {i + 1} 个字段为空";
                        return false;
                    }
                    if (!ConfigTemplateCodeGen.IsValidIdentifier(f.name))
                    {
                        error = $"字段名「{f.name}」不是合法的 C# 标识符（或为关键字）";
                        return false;
                    }
                    if (ReservedMemberNames.Contains(f.name))
                    {
                        error = $"字段名「{f.name}」与 MonoBehaviour/生成代码的成员重名，请改名";
                        return false;
                    }
                    if (!names.Add(f.name))
                    {
                        error = $"字段名「{f.name}」重复";
                        return false;
                    }
                    if (f.type == SceneContentFieldType.Enum)
                    {
                        if (string.IsNullOrWhiteSpace(f.enumRefClassName))
                        {
                            error = $"字段「{f.name}」是枚举类型，但没有指定枚举定义";
                            return false;
                        }
                        if (ConfigEnumCodeGen.FindEnum(f.enumRefClassName) == null)
                        {
                            error = $"字段「{f.name}」引用的枚举定义「{f.enumRefClassName}」不存在";
                            return false;
                        }
                    }
                    if (!ValidateExportSide(def, f, f.clientExport, "客户端", out error))
                        return false;
                    if (!ValidateExportSide(def, f, f.serverExport, "服务端", out error))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 校验一侧导出设置是否与字段类型相容。非法组合（如 GameObject 直接 JSON）在生成前就拦住，
        /// 免得吐出一份编译不过或导出即抛错的代码。
        /// </summary>
        private static bool ValidateExportSide(SceneContentTypeDef def, SceneContentFieldDef f,
            SceneContentFieldExport export, string sideLabel, out string error)
        {
            error = null;
            if (export == null || !export.IsExported)
                return true;

            switch (f.type)
            {
                case SceneContentFieldType.GameObject:
                    if (export.mode == SceneContentExportMode.Direct
                        || export.mode == SceneContentExportMode.Text)
                    {
                        error = $"字段「{f.name}」({def.className}) 的{sideLabel}导出方式不合法：" +
                                "GameObject 只能是 层级路径 / 资源名 / 资源路径 / Addressable 地址";
                        return false;
                    }
                    return true;

                default:
                    if (export.mode == SceneContentExportMode.HierarchyPath
                        || export.mode == SceneContentExportMode.AssetName
                        || export.mode == SceneContentExportMode.AssetPath
                        || export.mode == SceneContentExportMode.AssetAddressable)
                    {
                        error = $"字段「{f.name}」({def.className}) 的{sideLabel}导出方式不合法：" +
                                "只有 GameObject 字段能导出成路径 / 名字 / 地址";
                        return false;
                    }
                    return true;
            }
        }

        /// <summary>
        /// 生成代码里不能用的字段名：MonoBehaviour/Object 的成员，以及生成方法体内的
        /// 局部名（<c>w</c>/<c>i</c>/<c>levelRoot</c>/<c>side</c>）——撞上会 CS0102 / CS0136。
        /// </summary>
        private static readonly HashSet<string> ReservedMemberNames = new HashSet<string>
        {
            "name", "hideFlags", "transform", "gameObject", "tag",
            "enabled", "isActiveAndEnabled", "useGUILayout", "runInEditMode",
            "ContentTypeId", "ToPayloadJson",
            "w", "i", "levelRoot", "side",
        };

        /// <summary>生成器自己占用的类名（id 常量表），定义不能与之同名。</summary>
        private static readonly HashSet<string> ReservedClassNames = new HashSet<string>
        {
            "SceneContentTypes", "SceneContentTypeNames",
        };

        // ---------------------------------------------------------------- 生成

        /// <summary>生成标记组件的 C# 源码（不含自动生成头，写盘时由 <see cref="GeneratedCodeFile"/> 套）。</summary>
        public static string GenerateCode(SceneContentTypeDef def)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using HYC.Framework.SceneContent;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {Namespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {XmlEscape(def.displayName)}");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    [AddComponentMenu(\"HYC/SceneContent/{def.className}\")]");
            sb.AppendLine($"    public class {def.className} : MonoBehaviour, ISceneContentMarker, ISceneContentMarkerPayload");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>内容类型 id（由类名哈希得出，生成期固定）。改类名 = 换 id，已导出的 blob 需重导。</summary>");
            sb.AppendLine($"        public const int ContentTypeId = {def.ContentTypeId};");
            sb.AppendLine();
            sb.AppendLine("        int ISceneContentMarker.ContentTypeId => ContentTypeId;");

            var fields = def.fields ?? new List<SceneContentFieldDef>();
            for (var i = 0; i < fields.Count; i++)
            {
                sb.AppendLine();
                AppendFieldDeclaration(sb, fields[i]);
            }

            sb.AppendLine();
            sb.AppendLine("        /// <summary>按导出侧产出 payload JSON（代码生成，勿手改）。</summary>");
            sb.AppendLine("        public string ToPayloadJson(GameObject levelRoot, SceneContentSide side)");
            sb.AppendLine("        {");
            sb.AppendLine("            var w = new SceneContentJsonWriter();");
            sb.AppendLine("            w.BeginObject();");
            sb.AppendLine("            if (side == SceneContentSide.Client)");
            sb.AppendLine("            {");
            AppendSideBody(sb, fields, SceneContentSide.Client);
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            AppendSideBody(sb, fields, SceneContentSide.Server);
            sb.AppendLine("            }");
            sb.AppendLine("            w.EndObject();");
            sb.AppendLine("            return w.ToString();");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendFieldDeclaration(StringBuilder sb, SceneContentFieldDef f)
        {
            var type = FieldTypeName(f.type, f.enumRefClassName);
            var hasDesc = !string.IsNullOrWhiteSpace(f.description);
            if (hasDesc)
            {
                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// {XmlEscape(f.description)}");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        [Tooltip(\"{CodeEscape(f.description)}\")]");
            }

            if (f.isList)
                sb.AppendLine($"        public List<{type}> {f.name} = new List<{type}>();");
            else if (f.type == SceneContentFieldType.String)
                sb.AppendLine($"        public string {f.name} = \"\";");
            else
                sb.AppendLine($"        public {type} {f.name};");
        }

        /// <summary>生成某一侧写 payload 的全部语句。</summary>
        private static void AppendSideBody(StringBuilder sb, List<SceneContentFieldDef> fields, SceneContentSide side)
        {
            var wrote = false;
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                var export = side == SceneContentSide.Client ? f.clientExport : f.serverExport;
                if (export == null || !export.IsExported)
                    continue;
                wrote = true;

                if (!f.isList)
                {
                    sb.AppendLine($"                w.Key(\"{f.name}\");");
                    sb.AppendLine($"                {ValueExpression(f, f.name, export.mode)}");
                    continue;
                }

                sb.AppendLine($"                w.Key(\"{f.name}\");");
                sb.AppendLine("                w.BeginArray();");
                sb.AppendLine($"                for (var i = 0; i < {f.name}.Count; i++)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {ValueExpression(f, $"{f.name}[i]", export.mode)}");
                sb.AppendLine("                }");
                sb.AppendLine("                w.EndArray();");
            }

            if (!wrote)
                sb.AppendLine("                // 该侧没有需要导出的字段");
        }

        /// <summary>把一个字段值写成 <c>w.Value(...)</c> 语句。</summary>
        private static string ValueExpression(SceneContentFieldDef f, string accessor, SceneContentExportMode mode)
        {
            switch (f.type)
            {
                case SceneContentFieldType.GameObject:
                    switch (mode)
                    {
                        case SceneContentExportMode.HierarchyPath:
                            return $"w.Value(SceneContentRefPath.GetPath(levelRoot, {accessor} != null ? {accessor}.transform : null));";
                        case SceneContentExportMode.AssetName:
                            return $"w.Value({accessor} != null ? {accessor}.name : null);";
                        case SceneContentExportMode.AssetPath:
                            return $"w.Value(SceneContentRefPath.GetAssetPath({accessor}));";
                        case SceneContentExportMode.AssetAddressable:
                            return $"w.Value(SceneContentRefPath.GetAddressableAddress({accessor}));";
                        default:
                            // 校验阶段已拦住非法组合，这里只是兜底
                            return "w.Value((string)null);";
                    }

                case SceneContentFieldType.Enum:
                    return mode == SceneContentExportMode.Text
                        ? $"w.Value({accessor}.ToString());"
                        : $"w.Value((int){accessor});";

                default:
                    return mode == SceneContentExportMode.Text
                        ? $"w.Value({accessor}.ToString());"
                        : $"w.Value({accessor});";
            }
        }

        /// <summary>字段类型在生成代码里的写法（枚举写成全限定名，避免依赖 using）。</summary>
        private static string FieldTypeName(SceneContentFieldType type, string enumRefClassName)
        {
            switch (type)
            {
                case SceneContentFieldType.Int: return "int";
                case SceneContentFieldType.Float: return "float";
                case SceneContentFieldType.Bool: return "bool";
                case SceneContentFieldType.String: return "string";
                case SceneContentFieldType.GameObject: return "GameObject";
                case SceneContentFieldType.Enum:
                    return $"{ConfigDataSettings.EnumNamespace}.{enumRefClassName}";
                default: return "int";
            }
        }

        // ---------------------------------------------------------------- id 常量表

        /// <summary>生成 id 常量表 + id→显示名（全类型统一一份）。</summary>
        public static string GenerateRegistry(List<SceneContentTypeDef> defs)
        {
            var list = defs.Where(d => d != null && ConfigTemplateCodeGen.IsValidIdentifier(d.className))
                .OrderBy(d => d.className, System.StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"namespace {Namespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>场景内容类型 id 常量表（生成）。运行期按常量比较，避免魔法数字。</summary>");
            sb.AppendLine("    public static class SceneContentTypes");
            sb.AppendLine("    {");
            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (i > 0) sb.AppendLine();
                sb.AppendLine($"        /// <summary>{XmlEscape(d.displayName)}</summary>");
                sb.AppendLine($"        public const int {d.className} = {d.ContentTypeId};");
            }
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>场景内容类型 id → 显示名（生成，供日志与调试）。</summary>");
            sb.AppendLine("    public static class SceneContentTypeNames");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>取显示名；未知 id 返回 null。</summary>");
            sb.AppendLine("        public static string Get(int contentTypeId)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (contentTypeId)");
            sb.AppendLine("            {");
            foreach (var d in list)
                sb.AppendLine($"                case SceneContentTypes.{d.className}: return \"{CodeEscape(d.displayName)}\";");
            sb.AppendLine("                default: return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 写盘

        /// <summary>生成并写入单个定义的标记组件代码。失败返回 false 并给出原因。</summary>
        public static bool WriteFile(SceneContentTypeDef def, out string error)
        {
            if (!Validate(def, out error))
                return false;

            EnsureOutputDir();

            // 类名改过：先删旧生成文件，避免留下一个指向已不存在类型的孤儿组件
            var old = def.lastGeneratedClassName;
            if (!string.IsNullOrEmpty(old) && old != def.className)
            {
                var oldPath = FilePathFor(old);
                if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), oldPath)))
                    AssetDatabase.DeleteAsset(oldPath);
            }

            GeneratedCodeFile.WriteAndImport(FilePathFor(def.className), GenerateCode(def));

            if (def.lastGeneratedClassName != def.className)
            {
                def.lastGeneratedClassName = def.className;
                EditorUtility.SetDirty(def);
            }
            return true;
        }

        /// <summary>重新生成全部类型 + id 常量表。返回失败原因列表（空 = 全部成功）。</summary>
        public static List<string> WriteAll()
        {
            var errors = new List<string>();
            var defs = LoadAllTypes();

            foreach (var def in defs)
            {
                if (WriteFile(def, out var err))
                    continue;
                errors.Add($"{def.name}：{err}");
            }

            // 常量表只在 id 全局唯一时敢写：重复 case 标签会让生成文件编译不过
            var ids = new HashSet<int>();
            var duplicate = defs.Any(d => d != null && !ids.Add(d.ContentTypeId));
            if (!duplicate)
            {
                EnsureOutputDir();
                GeneratedCodeFile.WriteAndImport($"{OutputDir}/{RegistryFileName}", GenerateRegistry(defs));
            }
            else
            {
                errors.Add("存在内容类型 id 冲突的定义，已跳过 id 常量表生成");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return errors;
        }

        private static void EnsureOutputDir()
        {
            var full = Path.Combine(Directory.GetCurrentDirectory(), OutputDir);
            if (!Directory.Exists(full))
                Directory.CreateDirectory(full);
        }

        // ---------------------------------------------------------------- 资产创建

        /// <summary>
        /// 创建场景内容类型定义资产（数据树右键 / 数据编辑器创建器两条入口共用）。
        /// <para>targetFolder 为空时自动归置到「场景内容类型」类型文件夹（见 <see cref="ConfigAssetLayout"/>）。</para>
        /// </summary>
        public static SceneContentTypeDef CreateTypeAsset(string targetFolder = null)
        {
            if (string.IsNullOrEmpty(targetFolder))
                targetFolder = DefaultTypeFolder;
            EnsureFolder(targetFolder);

            var path = $"{targetFolder}/NewSceneContent.asset";
            var index = 1;
            while (AssetDatabase.LoadAssetAtPath<SceneContentTypeDef>(path) != null)
            {
                path = $"{targetFolder}/NewSceneContent {index}.asset";
                index++;
            }

            var def = ScriptableObject.CreateInstance<SceneContentTypeDef>();
            def.displayName = "场景内容/新类型";
            def.className = "NewSceneContent" + index;
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return def;
        }

        /// <summary>逐级创建目录（必须在 Assets 下）；已存在则跳过。</summary>
        private static void EnsureFolder(string folder)
        {
            folder = (folder ?? "").Replace("\\", "/").Trim('/');
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

        // ---------------------------------------------------------------- 转义

        private static string XmlEscape(string s)
            => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        /// <summary>把文本塞进 C# 字符串字面量（引号/反斜杠/换行）。</summary>
        private static string CodeEscape(string s)
            => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", " ").Replace("\n", " ");
    }
}
