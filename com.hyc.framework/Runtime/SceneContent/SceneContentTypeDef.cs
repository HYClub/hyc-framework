using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 场景内容类型定义（策划配的「场景内容模板」）。
    /// <para>
    /// 一个定义 ↔ 一个标记组件：代码生成器按本资产吐出
    /// <c>{className} : MonoBehaviour, ISceneContentMarker, ISceneContentMarkerPayload</c>，
    /// 策划把生成的组件挂在关卡 prefab 的出生点/开关等 GO 上，就能在导出时变成一条场景内容条目。
    /// </para>
    /// <para>
    /// <b>刻意不进配置数据编辑器</b>：它不是配置数据（没有 ID/GUID、不参与发号与导出），
    /// 因此<b>不标</b> <c>[CfgAsset]</c>——不走「配置创建器」也不出现在配置数据树里。
    /// 创建入口见菜单 <c>HYC Framework/Scene Content/New Scene Content Type</c>，
    /// 编辑用本资产的自定义 Inspector。
    /// </para>
    /// </summary>
    public class SceneContentTypeDef : ScriptableObject
    {
        /// <summary>显示名，支持 <c>分类/名称</c>（仅用于编辑器展示与生成代码注释）。</summary>
        [Tooltip("显示名，支持 分类/名称，例如 玩法/怪物出生点")]
        public string displayName = "场景内容/新类型";

        /// <summary>生成的 MonoBehaviour 类名。</summary>
        [Tooltip("生成的 MonoBehaviour 类名，例如 MonsterSpawn")]
        public string className = "NewSceneContent";

        /// <summary>上次生成时的类名，用于改名后清理旧生成文件。</summary>
        [HideInInspector]
        public string lastGeneratedClassName = "";

        /// <summary>
        /// 编辑器侧扫描对象：把关卡 prefab 拖进来，面板会列出其上所有标记组件、
        /// 以及本类型的字段能否与组件的序列化字段对上（反向填充用）。
        /// 仅编辑器使用，不进 Blob、不影响运行期。
        /// </summary>
        [Tooltip("编辑器侧扫描对象（关卡 prefab）。拖进来可列出其上全部标记组件并反填字段，仅编辑器用。")]
        public GameObject scanPrefab;

        /// <summary>策划可配的参数字段列表。</summary>
        public List<SceneContentFieldDef> fields = new List<SceneContentFieldDef>();

        /// <summary>
        /// 内容类型 id：由 <see cref="className"/> 做的 FNV-1a 32 位哈希。
        /// 生成代码把它写成 <c>public const int ContentTypeId</c>，导出与运行期共用同一个值。
        /// </summary>
        public int ContentTypeId => SceneContentTypeId.Compute(className);
    }

    /// <summary>场景内容字段的取值类型（只放<b>能落进 JSON payload</b> 的种类）。</summary>
    public enum SceneContentFieldType
    {
        /// <summary>整型标量。</summary>
        Int,
        /// <summary>浮点标量。</summary>
        Float,
        /// <summary>布尔开关（如「这个门是否开启」）。</summary>
        Bool,
        /// <summary>字符串（枚举之外的自由文本）。</summary>
        String,
        /// <summary>枚举（引用数据编辑器里的枚举定义，见 <see cref="SceneContentFieldDef.enumRefClassName"/>）。</summary>
        Enum,
        /// <summary>场景内 GameObject 引用（导出时按该侧导出方式转成路径/名字/地址）。</summary>
        GameObject,
    }

    /// <summary>
    /// 单个字段在<b>某一侧</b>导出产物里的表示方式。
    /// <para>
    /// 与配置模块的 <see cref="ConfigFieldExportMode"/> 不共用：那边投影的是 <b>Blob</b>（BlobString/FixedString/Member
    /// 这些档位为 blob 定长布局服务），这里投影的是 <b>JSON payload</b>，两侧语义不同；
    /// 共用枚举会把「Blob 定长串」这类与 JSON 无关的档位带进来。
    /// </para>
    /// </summary>
    public enum SceneContentExportMode
    {
        /// <summary>按字段自身类型原样写进 JSON。</summary>
        Direct,
        /// <summary>转字符串写进 JSON（枚举写名字，标量写文本）。</summary>
        Text,
        /// <summary>GameObject → 相对关卡根的层级路径（如 <c>Gate/Door#1</c>），运行期用 <see cref="SceneContentRefPath.Resolve"/> 反解。</summary>
        HierarchyPath,
        /// <summary>GameObject → 资源名（prefab 名）。</summary>
        AssetName,
        /// <summary>GameObject → 项目内资源路径（<c>Assets/...</c>）。</summary>
        AssetPath,
        /// <summary>GameObject → Addressable 地址。</summary>
        AssetAddressable,
        /// <summary>该侧不导出此字段（JSON 里不出现这个 key）。放在末尾，避免改动既有序号。</summary>
        None,
    }

    /// <summary>一个字段在一侧的导出设置。</summary>
    [Serializable]
    public class SceneContentFieldExport
    {
        /// <summary>该侧导出方式。</summary>
        public SceneContentExportMode mode = SceneContentExportMode.Direct;

        public SceneContentFieldExport()
        {
        }

        public SceneContentFieldExport(SceneContentExportMode mode)
        {
            this.mode = mode;
        }

        /// <summary>该侧是否导出本字段。</summary>
        public bool IsExported => mode != SceneContentExportMode.None;
    }

    /// <summary>一个可配字段的定义。</summary>
    [Serializable]
    public class SceneContentFieldDef
    {
        /// <summary>生成的 C# 字段名。</summary>
        [Tooltip("生成的 C# 字段名，例如 spawnCount")]
        public string name = "newField";

        /// <summary>中文说明，生成成 [Tooltip]。</summary>
        [Tooltip("字段说明，生成代码里作为 [Tooltip]")]
        public string description = "";

        /// <summary>取值类型。</summary>
        public SceneContentFieldType type = SceneContentFieldType.Int;

        /// <summary>是否生成为 <c>List&lt;T&gt;</c>（策划一次管一批 GO 时用）。</summary>
        [Tooltip("勾选则生成 List<T>（如一批门）")]
        public bool isList;

        /// <summary>
        /// 引用数据编辑器的枚举定义类名（<see cref="ConfigEnumDefinition.className"/>），
        /// 仅 <see cref="SceneContentFieldType.Enum"/> 时使用。
        /// </summary>
        [Tooltip("引用枚举定义的类名（仅 Enum 类型使用）")]
        public string enumRefClassName = "";

        /// <summary>客户端导出设置。</summary>
        public SceneContentFieldExport clientExport = new SceneContentFieldExport();

        /// <summary>服务端导出设置。默认不导出：服务端通常不需要可视对象引用。</summary>
        public SceneContentFieldExport serverExport = new SceneContentFieldExport(SceneContentExportMode.None);
    }
}
