using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>Field primitive/reference kinds supported by config templates.</summary>
    public enum ConfigFieldType
    {
        String,
        Int,
        Long,
        Float,
        Double,
        Bool,
        Short,
        Byte,
        UInt,
        Char,
        Decimal,
        Sprite,
        Texture2D,
        GameObject,
        AudioClip,
        Material,
        Mesh,
        PhysicMaterial,
        Font,
        Shader,
        TextAsset,
        Object,
        Vector2,
        Vector3,
        Vector4,
        Vector2Int,
        Vector3Int,
        Quaternion,
        Color,
        Color32,
        Rect,
        RectInt,
        RectOffset,
        Bounds,
        BoundsInt,
        Gradient,
        AnimationCurve,
        LayerMask,
        /// <summary>枚举类型（引用 <see cref="ConfigEnumDefinition"/>，见 <see cref="ConfigTemplateField.EnumRefClassName"/>）。</summary>
        Enum,
        /// <summary>多语言 key（需要 loc 包；生成 string + [LocKey]，见 <see cref="LocKeyAttribute"/>）。</summary>
        LocalizedKey,
        /// <summary>Asset reference to another <c>[CfgAsset]</c> type (see <see cref="ConfigTemplateField.RefTypeFullName"/>).</summary>
        Reference,
        /// <summary>行为树引用（存 BTTreeAsset.TreeId，long）。运行时经 BTManager 加载执行。</summary>
        BehaviourTree,
        /// <summary>动画片段（拖 .anim 文件, 生成 AnimationClip 字段）。</summary>
        AnimationClip,
        /// <summary>动画控制器（拖 .controller 文件, 生成 AnimatorController 字段）。</summary>
        AnimatorController,
        /// <summary>Addressable 资源地址（拖资源自动填地址, 生成 string 字段, 运行时按地址加载）。</summary>
        Addressable,
    }

    /// <summary>字段导出目标：客户端 / 服务器 / 两者。</summary>
    public enum ConfigExportTarget
    {
        Client,
        Server,
        Both,
    }

    /// <summary>检查规则级别：None = 该规则不启用。</summary>
    public enum ConfigCheckLevel
    {
        None,
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// 字段"导出设置"：决定导出的客户端/服务端数据里，这个字段取哪个值、用什么类型表示。
    /// 编辑器里存原值，导出时按此设置投影。
    /// </summary>
    public enum ConfigFieldExportMode
    {
        /// <summary>直接导出（保持现有行为，按字段自身类型/对象原样导出）。</summary>
        Direct,
        /// <summary>转字符串（数值/布尔/枚举/结构用 ToString，导出 string）。</summary>
        ToString,
        /// <summary>字符串字段在 Blob 里用 BlobString（变长，BlobBuilder 分配）。</summary>
        BlobString,
        /// <summary>字符串字段在 Blob 里用 FixedString（定长内联，见 <see cref="ConfigFieldExportSetting.fixedStringSize"/>）。</summary>
        FixedString,
        /// <summary>配置引用字段导出其某个标量成员（如 ID/GUID，见 <see cref="ConfigFieldExportSetting.memberName"/>）。</summary>
        Member,
        /// <summary>资源/对象字段导出资源名字（导出 string）。</summary>
        AssetName,
        /// <summary>资源/对象字段导出项目内资源路径（Assets/...，导出 string，需在导出时解析）。</summary>
        AssetPath,
        /// <summary>资源/对象字段导出 Addressable 地址（导出 string，需在导出时解析）。</summary>
        AssetAddressable,
    }

    /// <summary>FixedString 定长容量档位（映射 Unity.Collections.FixedString{size}Bytes）。</summary>
    public enum ConfigFixedStringSize
    {
        Size32 = 32,
        Size64 = 64,
        Size128 = 128,
        Size512 = 512,
        Size4096 = 4096,
    }

    /// <summary>一个字段在一侧的"导出设置"。</summary>
    [Serializable]
    public class ConfigFieldExportSetting
    {
        /// <summary>导出方式。</summary>
        public ConfigFieldExportMode mode = ConfigFieldExportMode.Direct;

        /// <summary><see cref="ConfigFieldExportMode.Member"/> 时引用的成员名（如 ID/GUID/…）。</summary>
        public string memberName = "";

        /// <summary><see cref="ConfigFieldExportMode.FixedString"/> 时选用的容量。</summary>
        public ConfigFixedStringSize fixedStringSize = ConfigFixedStringSize.Size128;

        public bool IsDirect => mode == ConfigFieldExportMode.Direct;
        public bool IsStringValue => mode == ConfigFieldExportMode.BlobString
                                     || mode == ConfigFieldExportMode.FixedString;
    }

    /// <summary>A single field definition inside a <see cref="ConfigTemplate"/>.</summary>
    [Serializable]
    public class ConfigTemplateField
    {
        public string name = "newField";
        public string description = "";
        public ConfigFieldType type = ConfigFieldType.String;
        public bool isList;
        /// <summary>Assembly-qualified name of the referenced config type when <see cref="type"/> is <see cref="ConfigFieldType.Reference"/>.</summary>
        public string refTypeFullName = "";
        /// <summary>引用的枚举类名（<see cref="ConfigEnumDefinition.className"/>），当 <see cref="type"/> 为 <see cref="ConfigFieldType.Enum"/> 时使用。</summary>
        public string enumRefClassName = "";
        /// <summary>导出目标：客户端 / 服务器 / 两者。</summary>
        public ConfigExportTarget exportTarget = ConfigExportTarget.Both;

        /// <summary>是否启用数值范围限制。</summary>
        public bool hasRange;
        /// <summary>范围下限（未启用忽略）。</summary>
        public float minValue;
        /// <summary>范围上限（未启用忽略）。</summary>
        public float maxValue;

        /// <summary>检查规则：非空（级别，None=不检查）。</summary>
        public ConfigCheckLevel notEmptyCheck = ConfigCheckLevel.None;
        /// <summary>检查规则：非0（级别，None=不检查）。</summary>
        public ConfigCheckLevel notZeroCheck = ConfigCheckLevel.None;
        /// <summary>检查规则：范围（级别，None=不检查）。</summary>
        public ConfigCheckLevel rangeCheck = ConfigCheckLevel.None;

        /// <summary>客户端导出设置（决定导出的客户端数据里本字段取什么值/什么类型）。</summary>
        public ConfigFieldExportSetting clientExport = new ConfigFieldExportSetting();

        /// <summary>服务端导出设置（决定导出的服务端数据里本字段取什么值/什么类型）。</summary>
        public ConfigFieldExportSetting serverExport = new ConfigFieldExportSetting();
    }

    /// <summary>
    /// Saveable config-type template used by the data editor to generate a new
    /// <c>[CfgAsset]</c> C# class. It is itself a config asset (visible in the
    /// data editor tree) so templates can be reopened and re-generated later.
    /// </summary>
    [CfgAsset("模板/配置模板", 0)]
    public class ConfigTemplate : ScriptableObject
    {
        [Tooltip("显示名，支持 分类/名称，例如 装备/时装")]
        public string displayName = "分类/名称";

        [Tooltip("生成的 C# 类名，例如 FashionConfig")]
        public string className = "NewConfig";

        /// <summary>上次生成代码时的类名。用于检测 className 改名，触发改名处理流程。</summary>
        [HideInInspector]
        public string lastGeneratedClassName = "";

        [Tooltip("基类模板：生成类将继承基类模板生成的类。基类字段以只读形式展示")]
        public ConfigTemplate baseTemplate;

        [Tooltip("内置图标名（EditorGUIUtility.IconContent），与 iconCustom 二选一")]
        public string iconBuiltInName = "";

        [Tooltip("自定义图标（项目内 Texture2D），优先级高于内置图标")]
        public Texture2D iconCustom;

        public List<ConfigTemplateField> fields = new List<ConfigTemplateField>();
    }
}
