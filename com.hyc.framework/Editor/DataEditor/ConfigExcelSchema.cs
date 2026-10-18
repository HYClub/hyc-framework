using System;
using System.Collections.Generic;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Excel 导入/导出共用的**表结构约定**（唯一真源，防止两侧各写一套类型串漂移）：
    /// <list type="bullet">
    /// <item>表头 3 行：第 1 行中文显示名、第 2 行英文字段名、第 3 行类型；数据从第 4 行起。</item>
    /// <item>框架列：首列 <c>ID</c>（业务主键）、末列 <c>GUID</c>（仅备份，不对账）。</item>
    /// <item>列表值用 <c>|</c> 连接单个元素。</item>
    /// </list>
    /// 类型串与代码生成器使用的 C# 类型名一致（枚举写类名、引用写短名），
    /// 解析侧 <see cref="TryParseTypeName"/> 接受别名（int32→Int、str→String…）。
    /// <para><b>维护约定</b>：新增 <see cref="ConfigFieldType"/> 成员时，
    /// <see cref="TypeDisplayName"/> / <see cref="TryParseTypeName"/> / <see cref="DraftTypes"/>
    /// 三处必须同步（AGENTS.md「新增 ConfigFieldType 需同步」铁律）。</para>
    /// </summary>
    public static class ConfigExcelSchema
    {
        /// <summary>表头行数（中文 / 英文 / 类型）。数据从下一行开始。</summary>
        public const int HeaderRows = 3;

        /// <summary>列表单元格内多个值的连接符。</summary>
        public const char ListSeparator = '|';

        /// <summary>框架主键列名（第 2 行的英文字段名）。</summary>
        public const string IdColumn = "ID";

        /// <summary>框架 GUID 列名（第 2 行的英文字段名，仅备份不对账）。</summary>
        public const string GuidColumn = "GUID";

        /// <summary>表头格式识别结果：3 行表头（中文/英文/类型）。</summary>
        public const string FormatTyped3 = "typed3";
        /// <summary>表头格式识别结果：2 行表头（英文/数据，无类型行）。</summary>
        public const string FormatEn2 = "en2";
        /// <summary>表头格式识别结果：1 行表头（中文/数据，无英文名与类型行）。</summary>
        public const string FormatCn1 = "cn1";

        // ---------- 显示名 → 类型（导出写、导入读） ----------

        /// <summary>别名 → 规范 <see cref="ConfigFieldType"/>（大小写不敏感）。</summary>
        private static readonly Dictionary<string, ConfigFieldType> Aliases =
            new Dictionary<string, ConfigFieldType>(StringComparer.OrdinalIgnoreCase)
            {
                // 数值
                { "int", ConfigFieldType.Int }, { "int32", ConfigFieldType.Int },
                { "int16", ConfigFieldType.Short }, { "short", ConfigFieldType.Short },
                { "long", ConfigFieldType.Long }, { "int64", ConfigFieldType.Long },
                { "uint", ConfigFieldType.UInt }, { "uint32", ConfigFieldType.UInt },
                { "byte", ConfigFieldType.Byte }, { "sbyte", ConfigFieldType.Short },
                { "char", ConfigFieldType.Char },
                { "float", ConfigFieldType.Float }, { "single", ConfigFieldType.Float },
                { "double", ConfigFieldType.Double }, { "decimal", ConfigFieldType.Decimal },
                // 布尔 / 字符串
                { "bool", ConfigFieldType.Bool }, { "boolean", ConfigFieldType.Bool },
                { "string", ConfigFieldType.String }, { "str", ConfigFieldType.String },
                // 特殊字段类型（类型串即类型名）
                { "localizedkey", ConfigFieldType.LocalizedKey },
                { "reference", ConfigFieldType.Reference },
                { "behaviourtree", ConfigFieldType.BehaviourTree },
                { "addressable", ConfigFieldType.Addressable },
                { "audioevent", ConfigFieldType.AudioEvent },
                // Unity 结构体
                { "vector2", ConfigFieldType.Vector2 },
                { "vector3", ConfigFieldType.Vector3 },
                { "vector4", ConfigFieldType.Vector4 },
                { "vector2int", ConfigFieldType.Vector2Int },
                { "vector3int", ConfigFieldType.Vector3Int },
                { "quaternion", ConfigFieldType.Quaternion },
                { "color", ConfigFieldType.Color },
                { "color32", ConfigFieldType.Color32 },
                { "rect", ConfigFieldType.Rect },
                { "rectint", ConfigFieldType.RectInt },
                { "rectoffset", ConfigFieldType.RectOffset },
                { "bounds", ConfigFieldType.Bounds },
                { "boundsint", ConfigFieldType.BoundsInt },
                { "gradient", ConfigFieldType.Gradient },
                { "animationcurve", ConfigFieldType.AnimationCurve },
                { "layermask", ConfigFieldType.LayerMask },
                // Unity 资源/对象
                { "sprite", ConfigFieldType.Sprite },
                { "texture2d", ConfigFieldType.Texture2D },
                { "gameobject", ConfigFieldType.GameObject },
                { "audioclip", ConfigFieldType.AudioClip },
                { "material", ConfigFieldType.Material },
                { "mesh", ConfigFieldType.Mesh },
                { "physicmaterial", ConfigFieldType.PhysicMaterial },
                { "font", ConfigFieldType.Font },
                { "shader", ConfigFieldType.Shader },
                { "textasset", ConfigFieldType.TextAsset },
                { "object", ConfigFieldType.Object },
                { "animationclip", ConfigFieldType.AnimationClip },
                { "animatorcontroller", ConfigFieldType.AnimatorController },
            };

        /// <summary>
        /// 解析表头第 3 行的类型串。<c>Enum</c> / <c>Reference</c> 的类型串是**类名**
        /// （枚举定义 / 配置模板的 className），按现有资产解析；解析不到返回 false（调用方给警告）。
        /// </summary>
        public static bool TryParseTypeName(string text, out ConfigFieldType type)
        {
            type = ConfigFieldType.String;
            var s = (text ?? "").Trim();
            if (s.Length == 0)
                return false;

            if (Aliases.TryGetValue(s, out type))
                return true;

            // 类型串 = 枚举类名 / 配置类名（Reference 存短名，类名即全名末段）
            if (ConfigEnumCodeGen.FindEnum(s) != null)
            {
                type = ConfigFieldType.Enum;
                return true;
            }
            foreach (var t in ConfigTemplateCodeGen.LoadAllTemplates(false))
            {
                if (t != null && t.generateCode && t.className == s)
                {
                    type = ConfigFieldType.Reference;
                    return true;
                }
            }
            return false;
        }

        /// <summary>类型串是否是已知类型（别名 / Unity 类型 / 已有枚举或配置类名）。</summary>
        public static bool IsKnownTypeName(string text) => TryParseTypeName(text, out _);

        // ---------- 类型 → 显示名（表头第 3 行） ----------

        /// <summary>
        /// <see cref="ConfigFieldType"/> → 表头第 3 行的类型串，与生成代码使用的类型名一致。
        /// <see cref="ConfigFieldType.Enum"/> 写 <paramref name="enumRefClassName"/>，
        /// <see cref="ConfigFieldType.Reference"/> 写引用类型短名（都为空时回落类型名本身）。
        /// </summary>
        public static string TypeDisplayName(ConfigFieldType type, string enumRefClassName, string refTypeFullName)
        {
            switch (type)
            {
                case ConfigFieldType.String: return "string";
                case ConfigFieldType.Int: return "int";
                case ConfigFieldType.Long: return "long";
                case ConfigFieldType.Float: return "float";
                case ConfigFieldType.Double: return "double";
                case ConfigFieldType.Bool: return "bool";
                case ConfigFieldType.Short: return "short";
                case ConfigFieldType.Byte: return "byte";
                case ConfigFieldType.UInt: return "uint";
                case ConfigFieldType.Char: return "char";
                case ConfigFieldType.Decimal: return "decimal";
                case ConfigFieldType.BehaviourTree: return "BehaviourTree";
                case ConfigFieldType.Enum:
                    return string.IsNullOrEmpty(enumRefClassName) ? "Enum" : enumRefClassName;
                case ConfigFieldType.LocalizedKey: return "LocalizedKey";
                case ConfigFieldType.Addressable: return "Addressable";
                case ConfigFieldType.AudioEvent: return "AudioEvent";
                case ConfigFieldType.Reference:
                    var shortName = ConfigTemplateCodeGen.RefTypeShortName(refTypeFullName);
                    return string.IsNullOrEmpty(shortName) ? "Reference" : shortName;
                default:
                    return UnityTypeName(type) ?? type.ToString();
            }
        }

        /// <summary>Unity 结构体 / 资源类型的显示名（与生成器 <c>UnityTypeName</c> 同一集合）。</summary>
        public static string UnityTypeName(ConfigFieldType type)
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

        // ---------- 字段草拟表的类型下拉 ----------

        /// <summary>
        /// 字段草拟表类型下拉的可选类型（与模板编辑器同集合，按常用度排序）。
        /// <see cref="ConfigFieldType.Enum"/> / <see cref="ConfigFieldType.Reference"/>
        /// 选中后由用户补类名（见导入窗口），不在此列展开。
        /// </summary>
        public static readonly ConfigFieldType[] DraftTypes =
        {
            ConfigFieldType.String,
            ConfigFieldType.Int,
            ConfigFieldType.Long,
            ConfigFieldType.Float,
            ConfigFieldType.Double,
            ConfigFieldType.Bool,
            ConfigFieldType.Short,
            ConfigFieldType.Byte,
            ConfigFieldType.UInt,
            ConfigFieldType.Char,
            ConfigFieldType.Decimal,
            ConfigFieldType.Enum,
            ConfigFieldType.LocalizedKey,
            ConfigFieldType.Reference,
            ConfigFieldType.BehaviourTree,
            ConfigFieldType.Addressable,
            ConfigFieldType.AudioEvent,
            ConfigFieldType.Sprite,
            ConfigFieldType.Texture2D,
            ConfigFieldType.GameObject,
            ConfigFieldType.AudioClip,
            ConfigFieldType.Material,
            ConfigFieldType.Mesh,
            ConfigFieldType.PhysicMaterial,
            ConfigFieldType.Font,
            ConfigFieldType.Shader,
            ConfigFieldType.TextAsset,
            ConfigFieldType.Object,
            ConfigFieldType.AnimationClip,
            ConfigFieldType.AnimatorController,
            ConfigFieldType.Vector2,
            ConfigFieldType.Vector3,
            ConfigFieldType.Vector4,
            ConfigFieldType.Vector2Int,
            ConfigFieldType.Vector3Int,
            ConfigFieldType.Quaternion,
            ConfigFieldType.Color,
            ConfigFieldType.Color32,
            ConfigFieldType.Rect,
            ConfigFieldType.RectInt,
            ConfigFieldType.RectOffset,
            ConfigFieldType.Bounds,
            ConfigFieldType.BoundsInt,
            ConfigFieldType.Gradient,
            ConfigFieldType.AnimationCurve,
            ConfigFieldType.LayerMask,
        };

        /// <summary>草拟表下拉项的显示名（Enum/Reference 带类名时显示类名）。</summary>
        public static string DraftTypeLabel(ConfigFieldType type, string enumRefClassName, string refTypeFullName)
            => TypeDisplayName(type, enumRefClassName, refTypeFullName);

        /// <summary>英文字段名清洗：只留标识符字符，首字符非字母/下杠时补下杠。</summary>
        public static string SanitizeFieldName(string name)
        {
            var s = (name ?? "").Trim();
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else if (c == ' ' || c == '-' || c == '/' || c == '.' || c == '（' || c == '）')
                    sb.Append('_');
            }
            var r = sb.ToString().Trim('_');
            if (r.Length == 0)
                return "field";
            if (!char.IsLetter(r[0]) && r[0] != '_')
                r = "_" + r;
            return r;
        }

        /// <summary>中文显示名 → 合法的 <see cref="ConfigTemplate.className"/>（PascalCase 标识符）。</summary>
        public static string SanitizeClassName(string name)
        {
            var s = SanitizeFieldName(name);
            if (s.Length == 0)
                return "NewConfig";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
