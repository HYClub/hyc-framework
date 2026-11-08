using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HYC.Framework.Config;
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
        /// 能生成 C# 字段、也能写进 JSON payload 的类型白名单。
        /// <para>类型下拉用的是配置模板那份<b>最全</b>的 <see cref="ConfigFieldType"/> 菜单；
        /// 不在这里的项在菜单里保留但置灰（见 <see cref="UnsupportedReason"/>），生成期再兜底拦一次。</para>
        /// </summary>
        public static readonly HashSet<ConfigFieldType> SupportedTypes = new HashSet<ConfigFieldType>
        {
            // 值类型
            ConfigFieldType.String, ConfigFieldType.Int, ConfigFieldType.Long,
            ConfigFieldType.Float, ConfigFieldType.Double, ConfigFieldType.Bool,
            ConfigFieldType.Short, ConfigFieldType.Byte, ConfigFieldType.UInt,
            ConfigFieldType.Char, ConfigFieldType.Decimal,
            // Unity 数学（写成数字数组）
            ConfigFieldType.Vector2, ConfigFieldType.Vector3, ConfigFieldType.Vector4,
            ConfigFieldType.Vector2Int, ConfigFieldType.Vector3Int, ConfigFieldType.Quaternion,
            ConfigFieldType.Color, ConfigFieldType.Color32, ConfigFieldType.Rect,
            ConfigFieldType.RectInt, ConfigFieldType.RectOffset, ConfigFieldType.Bounds,
            ConfigFieldType.BoundsInt, ConfigFieldType.LayerMask,
            // Unity 对象里只有 GameObject 能导出成稳定引用
            ConfigFieldType.GameObject,
            // 引用 / 枚举 / 框架能力（都落成标量）
            ConfigFieldType.Enum, ConfigFieldType.Reference,
            ConfigFieldType.BehaviourTree, ConfigFieldType.Addressable,
            ConfigFieldType.AudioEvent, ConfigFieldType.LocalizedKey,
        };

        /// <summary>null = 可用；非 null = 类型下拉里置灰时追加到标签末尾的原因。</summary>
        public static string UnsupportedReason(ConfigFieldType type)
        {
            if (SupportedTypes.Contains(type))
                return null;
            switch (type)
            {
                case ConfigFieldType.Gradient:
                case ConfigFieldType.AnimationCurve:
                    return "（暂不支持：曲线不能写进 payload）";
                case ConfigFieldType.Sprite:
                case ConfigFieldType.Texture2D:
                case ConfigFieldType.AudioClip:
                case ConfigFieldType.Material:
                case ConfigFieldType.Mesh:
                case ConfigFieldType.PhysicMaterial:
                case ConfigFieldType.Font:
                case ConfigFieldType.Shader:
                case ConfigFieldType.TextAsset:
                case ConfigFieldType.Object:
                case ConfigFieldType.AnimationClip:
                case ConfigFieldType.AnimatorController:
                    return "（暂不支持：资源引用只有 GameObject 可导出）";
                default:
                    return "（场景内容暂不支持）";
            }
        }

        // ---------------------------------------------------------------- 标识符规范化

        /// <summary>
        /// 从聊天 / 网页 / 文档复制类名时容易带进零宽字符（U+200B~200D、U+FEFF 等）；
        /// 肉眼与 Unity 文本框都看不见，但 char.IsLetterOrDigit 判 false，
        /// 生成期只报「不是合法的 C# 标识符」看不出哪里错。生成前统一清掉并 Trim。
        /// </summary>
        private static bool IsInvisibleChar(char c)
            => c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF'
            || c == '\u2060' || c == '\u00A0' || c == '\u00AD' || char.IsControl(c);

        private static string CleanIdentifier(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (!IsInvisibleChar(c))
                    sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>把名字里的不可见字符显形为 \uXXXX，用于报错文案。</summary>
        private static string VisibleIdentifier(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(IsInvisibleChar(c) ? $"\\u{(int)c:X4}" : c.ToString());
            return sb.ToString();
        }

        private static bool HasInvisibleChar(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            foreach (var c in raw)
            {
                if (IsInvisibleChar(c))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 规范化定义资产里的类名 / 字段名（去掉零宽字符与首尾空白），有改动返回 true 并标脏。
        /// 幂等，可在任意生成 / 校验入口前调用。
        /// </summary>
        public static bool NormalizeIdentifiers(SceneContentTypeDef def)
        {
            if (def == null)
                return false;
            var changed = false;
            var cls = CleanIdentifier(def.className);
            if (cls != def.className)
            {
                def.className = cls;
                changed = true;
            }
            if (def.fields != null)
            {
                foreach (var f in def.fields)
                {
                    if (f == null)
                        continue;
                    var n = CleanIdentifier(f.name);
                    if (n != f.name)
                    {
                        f.name = n;
                        changed = true;
                    }
                }
            }
            if (changed)
                EditorUtility.SetDirty(def);
            return changed;
        }

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
                error = $"类名「{VisibleIdentifier(def.className)}」不是合法的 C# 标识符（或为关键字）"
                      + (HasInvisibleChar(def.className) ? "：含不可见字符，点「保存」或「生成代码」会自动清理" : "");
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
                        error = $"字段名「{VisibleIdentifier(f.name)}」不是合法的 C# 标识符（或为关键字）";
                        return false;
                    }
                    if (ReservedMemberNames.Contains(f.name))
                    {
                        error = $"字段名「{f.name}」是保留名（内定参数或 MonoBehaviour/生成代码成员），请改名";
                        return false;
                    }
                    if (!names.Add(f.name))
                    {
                        error = $"字段名「{f.name}」重复";
                        return false;
                    }
                    if (!SupportedTypes.Contains(f.type))
                    {
                        error = $"字段「{f.name}」的类型 {f.type} 暂不支持，请在类型下拉里选一个可用项";
                        return false;
                    }
                    if (f.type == ConfigFieldType.Enum)
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
                    if (f.type == ConfigFieldType.Reference && string.IsNullOrWhiteSpace(f.refTypeFullName))
                    {
                        error = $"字段「{f.name}」是配置引用，但没有指定引用目标类型";
                        return false;
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
                case ConfigFieldType.GameObject:
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
        /// ToPayloadJson 恒写的内定参数 JSON key：用户不填、Inspector 只读、导出时从 transform 与关卡根现场算。
        /// 生成期保留名校验与 Inspector 的隐藏参数展示共用这一份清单。
        /// </summary>
        public static readonly string[] BuiltInParameterNames =
        {
            "localPos", "worldPos", "path", "localEuler",
        };

        /// <summary>
        /// 生成代码里不能用的字段名：MonoBehaviour/Object 的成员，生成方法体内的局部名
        /// （<c>w</c>/<c>i</c>/<c>levelRoot</c>/<c>side</c>），以及
        /// <see cref="BuiltInParameterNames"/> 里的内定参数 JSON key ——撞上会
        /// CS0102 / CS0136 或产出重复 JSON key。
        /// </summary>
        private static readonly HashSet<string> ReservedMemberNames = new HashSet<string>(BuiltInParameterNames)
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
            sb.AppendLine("using HYC.Framework.Config;");
            sb.AppendLine("using HYC.Framework.SceneContent;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {Namespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {XmlEscape(def.displayName)}");
            sb.AppendLine("    /// </summary>");
            // Add Component 下拉里的分组：HYC > Scene Content > 全部生成类型，点一下即挂组件
            sb.AppendLine($"    [AddComponentMenu(\"HYC/Scene Content/{def.className}\")]");
            sb.AppendLine($"    public class {def.className} : SceneContentMarkerBase, ISceneContentMarker, ISceneContentMarkerPayload");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>内容类型 id（由类名哈希得出，生成期固定）。改类名 = 换 id，已导出的 blob 需重导。</summary>");
            sb.AppendLine($"        public const int ContentTypeId = {def.ContentTypeId};");
            sb.AppendLine();
            sb.AppendLine("        int ISceneContentMarker.ContentTypeId => ContentTypeId;");

            var fields = def.fields ?? new List<SceneContentFieldDef>();
            for (var i = 0; i < fields.Count; i++)
            {
                sb.AppendLine();
                AppendFieldDeclaration(sb, def, fields[i]);
            }

            sb.AppendLine();
            sb.AppendLine("        /// <summary>按导出侧产出 payload JSON（代码生成，勿手改）。</summary>");
            sb.AppendLine("        public string ToPayloadJson(GameObject levelRoot, SceneContentSide side)");
            sb.AppendLine("        {");
            sb.AppendLine("            var w = new SceneContentJsonWriter();");
            sb.AppendLine("            w.BeginObject();");
            sb.AppendLine("            // 内定参数：恒导出，用户不填；坐标/路径从 transform 与关卡根现场算");
            sb.AppendLine("            w.Key(\"localPos\"); w.BeginArray();");
            sb.AppendLine("            w.Value(transform.localPosition.x); w.Value(transform.localPosition.y); w.Value(transform.localPosition.z);");
            sb.AppendLine("            w.EndArray();");
            sb.AppendLine("            w.Key(\"worldPos\"); w.BeginArray();");
            sb.AppendLine("            w.Value(transform.position.x); w.Value(transform.position.y); w.Value(transform.position.z);");
            sb.AppendLine("            w.EndArray();");
            sb.AppendLine("            w.Key(\"path\"); w.Value(levelRoot != null ? SceneContentRefPath.GetPath(levelRoot, transform) : null);");
            sb.AppendLine("            w.Key(\"localEuler\"); w.BeginArray();");
            sb.AppendLine("            w.Value(transform.localEulerAngles.x); w.Value(transform.localEulerAngles.y); w.Value(transform.localEulerAngles.z);");
            sb.AppendLine("            w.EndArray();");
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

        /// <summary>
        /// 只读 / 关联参数 → 字段特性。与配置模板生成器同一套机制：规则随生成物走，
        /// 不依赖定义资产在运行期存在；无效条件跳过（求值时视为满足，字段保持显示）。
        /// </summary>
        private static void AppendFieldRules(StringBuilder sb, SceneContentTypeDef def, SceneContentFieldDef f)
        {
            if (f.readOnly)
                sb.AppendLine("        [ReadOnly]");

            if (f.visibility == null)
                return;
            var all = def.fields ?? new List<SceneContentFieldDef>();
            foreach (var cond in f.visibility)
            {
                if (cond == null || string.IsNullOrEmpty(cond.targetField))
                    continue;
                var target = all.FirstOrDefault(x => x != null && x != f && !x.isList && x.name == cond.targetField);
                if (target == null)
                    continue;
                if (target.type == ConfigFieldType.Bool)
                {
                    sb.AppendLine($"        [CfgVisibleWhen(\"{cond.targetField}\", {(cond.boolValue ? "true" : "false")})]");
                }
                else if (target.type == ConfigFieldType.Enum && cond.enumValues != null && cond.enumValues.Count > 0)
                {
                    var parts = new List<string> { $"\"{cond.targetField}\"" };
                    foreach (var v in cond.enumValues)
                    {
                        if (!string.IsNullOrEmpty(v))
                            parts.Add($"\"{CodeEscape(v)}\"");
                    }
                    if (parts.Count > 1)
                        sb.AppendLine($"        [CfgVisibleWhen({string.Join(", ", parts)})]");
                }
            }
        }

        private static void AppendFieldDeclaration(StringBuilder sb, SceneContentTypeDef def, SceneContentFieldDef f)
        {
            AppendFieldRules(sb, def, f);
            var type = FieldTypeName(f);
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
            else if (IsStringField(f.type))
                sb.AppendLine($"        public string {f.name} = \"\";");
            else if (f.type == ConfigFieldType.RectOffset)
                // 引用类型字段不初始化会是 null，导出时读 left/right/top/bottom 直接空引用
                sb.AppendLine($"        public {type} {f.name} = new {type}();");
            else
                sb.AppendLine($"        public {type} {f.name};");
        }

        /// <summary>生成字段是 <c>string</c> 的类型（框架能力项都落成字符串标量）。</summary>
        private static bool IsStringField(ConfigFieldType type)
            => type == ConfigFieldType.String
               || type == ConfigFieldType.LocalizedKey
               || type == ConfigFieldType.Addressable
               || type == ConfigFieldType.AudioEvent;

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
                    AppendValueExpression(sb, f, f.name, export.mode, "                ");
                    continue;
                }

                sb.AppendLine($"                w.Key(\"{f.name}\");");
                sb.AppendLine("                w.BeginArray();");
                sb.AppendLine($"                for (var i = 0; i < {f.name}.Count; i++)");
                sb.AppendLine("                {");
                AppendValueExpression(sb, f, $"{f.name}[i]", export.mode, "                    ");
                sb.AppendLine("                }");
                sb.AppendLine("                w.EndArray();");
            }

            if (!wrote)
                sb.AppendLine("                // 该侧没有需要导出的字段");
        }

        /// <summary>把一个字段值写成一到多条 <c>w.Value(...)</c> 语句（复合结构写成数组）。</summary>
        private static void AppendValueExpression(StringBuilder sb, SceneContentFieldDef f,
            string accessor, SceneContentExportMode mode, string indent)
        {
            // 字符串类字段：Direct / Text 都写 JSON 字符串，null 由 writer 转成 JSON null
            if (IsStringField(f.type))
            {
                sb.AppendLine($"{indent}w.Value({accessor});");
                return;
            }
            if (mode == SceneContentExportMode.Text && f.type != ConfigFieldType.GameObject)
            {
                sb.AppendLine($"{indent}w.Value({accessor}.ToString());");
                return;
            }
            AppendDirectExpression(sb, f, accessor, mode, indent);
        }

        /// <summary>按字段自身类型写值；Unity 复合结构按固定分量顺序写成数字数组。</summary>
        private static void AppendDirectExpression(StringBuilder sb, SceneContentFieldDef f,
            string accessor, SceneContentExportMode mode, string indent)
        {
            switch (f.type)
            {
                case ConfigFieldType.GameObject:
                {
                    string expr;
                    switch (mode)
                    {
                        case SceneContentExportMode.HierarchyPath:
                            expr = $"SceneContentRefPath.GetPath(levelRoot, {accessor} != null ? {accessor}.transform : null)";
                            break;
                        case SceneContentExportMode.AssetName:
                            expr = $"({accessor} != null ? {accessor}.name : null)";
                            break;
                        case SceneContentExportMode.AssetPath:
                            expr = $"SceneContentRefPath.GetAssetPath({accessor})";
                            break;
                        case SceneContentExportMode.AssetAddressable:
                            expr = $"SceneContentRefPath.GetAddressableAddress({accessor})";
                            break;
                        default:
                            // 校验阶段已拦住非法组合，这里只是兜底
                            expr = "(string)null";
                            break;
                    }
                    sb.AppendLine($"{indent}w.Value({expr});");
                    return;
                }

                // 标量：writer 有对应重载（long 覆盖配置引用 / 行为树 id）
                case ConfigFieldType.Int:
                case ConfigFieldType.Long:
                case ConfigFieldType.Float:
                case ConfigFieldType.Double:
                case ConfigFieldType.Decimal:
                case ConfigFieldType.Bool:
                case ConfigFieldType.Reference:
                case ConfigFieldType.BehaviourTree:
                    sb.AppendLine($"{indent}w.Value({accessor});");
                    return;

                case ConfigFieldType.Short:
                case ConfigFieldType.Byte:
                    sb.AppendLine($"{indent}w.Value((int){accessor});");
                    return;
                case ConfigFieldType.UInt:
                    sb.AppendLine($"{indent}w.Value((long){accessor});");
                    return;
                case ConfigFieldType.Char:
                    sb.AppendLine($"{indent}w.Value({accessor}.ToString());");
                    return;
                case ConfigFieldType.Enum:
                    sb.AppendLine($"{indent}w.Value((int){accessor});");
                    return;

                // 复合结构：固定分量顺序写数组，运行期按同样顺序读
                case ConfigFieldType.Vector2:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y");
                    return;
                case ConfigFieldType.Vector3:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y", $"{accessor}.z");
                    return;
                case ConfigFieldType.Vector4:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y", $"{accessor}.z", $"{accessor}.w");
                    return;
                case ConfigFieldType.Vector2Int:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y");
                    return;
                case ConfigFieldType.Vector3Int:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y", $"{accessor}.z");
                    return;
                case ConfigFieldType.Quaternion:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y", $"{accessor}.z", $"{accessor}.w");
                    return;
                case ConfigFieldType.Color:
                    AppendArray(sb, indent, $"{accessor}.r", $"{accessor}.g", $"{accessor}.b", $"{accessor}.a");
                    return;
                case ConfigFieldType.Color32:
                    AppendArray(sb, indent, $"(int){accessor}.r", $"(int){accessor}.g",
                        $"(int){accessor}.b", $"(int){accessor}.a");
                    return;
                case ConfigFieldType.Rect:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y",
                        $"{accessor}.width", $"{accessor}.height");
                    return;
                case ConfigFieldType.RectInt:
                    AppendArray(sb, indent, $"{accessor}.x", $"{accessor}.y",
                        $"{accessor}.width", $"{accessor}.height");
                    return;
                case ConfigFieldType.RectOffset:
                    AppendArray(sb, indent, $"{accessor}.left", $"{accessor}.right",
                        $"{accessor}.top", $"{accessor}.bottom");
                    return;
                case ConfigFieldType.Bounds:
                    AppendArray(sb, indent, $"{accessor}.center.x", $"{accessor}.center.y", $"{accessor}.center.z",
                        $"{accessor}.size.x", $"{accessor}.size.y", $"{accessor}.size.z");
                    return;
                case ConfigFieldType.BoundsInt:
                    AppendArray(sb, indent, $"{accessor}.position.x", $"{accessor}.position.y", $"{accessor}.position.z",
                        $"{accessor}.size.x", $"{accessor}.size.y", $"{accessor}.size.z");
                    return;
                case ConfigFieldType.LayerMask:
                    sb.AppendLine($"{indent}w.Value({accessor}.value);");
                    return;

                default:
                    // 校验阶段已拦住不支持类型，这里只兜底，别吐出编译不过的码
                    sb.AppendLine($"{indent}w.Value((string)null);");
                    return;
            }
        }

        /// <summary>把固定分量的复合结构写成一个数字数组。</summary>
        private static void AppendArray(StringBuilder sb, string indent, params string[] values)
        {
            sb.AppendLine($"{indent}w.BeginArray();");
            for (var i = 0; i < values.Length; i++)
                sb.AppendLine($"{indent}w.Value({values[i]});");
            sb.AppendLine($"{indent}w.EndArray();");
        }

        /// <summary>字段类型在生成代码里的写法（枚举写成全限定名，避免依赖 using）。</summary>
        private static string FieldTypeName(SceneContentFieldDef f)
        {
            switch (f.type)
            {
                case ConfigFieldType.String:
                case ConfigFieldType.LocalizedKey:
                case ConfigFieldType.Addressable:
                case ConfigFieldType.AudioEvent: return "string";
                case ConfigFieldType.Int: return "int";
                case ConfigFieldType.Long:
                case ConfigFieldType.Reference:
                case ConfigFieldType.BehaviourTree: return "long";
                case ConfigFieldType.Float: return "float";
                case ConfigFieldType.Double: return "double";
                case ConfigFieldType.Decimal: return "decimal";
                case ConfigFieldType.Bool: return "bool";
                case ConfigFieldType.Short: return "short";
                case ConfigFieldType.Byte: return "byte";
                case ConfigFieldType.UInt: return "uint";
                case ConfigFieldType.Char: return "char";
                case ConfigFieldType.GameObject: return "GameObject";
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
                case ConfigFieldType.LayerMask: return "LayerMask";
                case ConfigFieldType.Enum:
                    return $"{ConfigDataSettings.EnumNamespace}.{f.enumRefClassName}";
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
            NormalizeIdentifiers(def);
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
        /// 保存 / 创建后把 .asset 文件名同步成（净化的）显示名，与数据配置
        /// <c>ConfigTemplateEditor.SyncTemplateAssetName</c> 同逻辑：用 <see cref="AssetDatabase.MoveAsset"/>
        /// 搬家（保留 GUID、不破坏引用），冲突自动加序号；已是目标名则不动。返回最终路径（失败返回原路径）。
        /// 显示名（如「场景内容/出生点」）净化成文件系统安全名（<c>/</c><c>\</c> → <c>-</c>，非法字符 → <c>_</c>）。
        /// </summary>
        public static string SyncTypeAssetName(SceneContentTypeDef def)
        {
            if (def == null)
                return null;
            var currentPath = AssetDatabase.GetAssetPath(def);
            if (string.IsNullOrEmpty(currentPath))
                return null;

            var dir = Path.GetDirectoryName(currentPath);
            var desired = SanitizeTypeAssetName(def.displayName);
            var fileName = desired + ".asset";
            var targetPath = Path.Combine(dir, fileName);

            var index = 1;
            while (!string.Equals(targetPath, currentPath, System.StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                fileName = $"{desired} {index}.asset";
                targetPath = Path.Combine(dir, fileName);
                index++;
            }

            if (string.Equals(targetPath, currentPath, System.StringComparison.OrdinalIgnoreCase))
                return currentPath;

            var result = AssetDatabase.MoveAsset(currentPath, targetPath);
            if (!string.IsNullOrEmpty(result))
                return currentPath;

            AssetDatabase.SaveAssets();
            return targetPath;
        }

        /// <summary>
        /// 显示名 → 资产文件名：去首尾空白、<c>/</c><c>\</c> 换成 <c>-</c>，其余文件系统非法字符换成 <c>_</c>；
        /// 空结果回落 <c>未命名</c>。与 <c>ConfigTemplateCodeGen.DisplayNameToAssetName</c> 同语义。
        /// </summary>
        private static string SanitizeTypeAssetName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "未命名";
            var sb = new StringBuilder(displayName.Length);
            foreach (var c in displayName.Trim())
            {
                if (c == '/' || c == '\\')
                    sb.Append('-');
                else if (c == '<' || c == '>' || c == ':' || c == '"' || c == '|' || c == '?' || c == '*')
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "未命名" : s;
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
