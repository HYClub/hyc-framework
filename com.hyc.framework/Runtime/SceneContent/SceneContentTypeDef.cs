using System;
using System.Collections.Generic;
using HYC.Framework.Config;
using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 场景内容类型定义（策划配的「场景内容模板」）。
    /// <para>
    /// 一个定义 ↔ 一个标记组件：代码生成器按本资产吐出
    /// <c>{className} : SceneContentMarkerBase, ISceneContentMarker, ISceneContentMarkerPayload</c>，
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

        /// <summary>策划可配的参数字段列表。</summary>
        public List<SceneContentFieldDef> fields = new List<SceneContentFieldDef>();

        /// <summary>
        /// 内容类型 id：由 <see cref="className"/> 做的 FNV-1a 32 位哈希。
        /// 生成代码把它写成 <c>public const int ContentTypeId</c>，导出与运行期共用同一个值。
        /// </summary>
        public int ContentTypeId => SceneContentTypeId.Compute(className);
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

        /// <summary>
        /// 取值类型，与配置模板共用 <see cref="ConfigFieldType"/>；菜单里不支持的项在编辑器内置灰，
        /// 生成器另有一份「可生成」白名单做兜底校验。
        /// </summary>
        public ConfigFieldType type = ConfigFieldType.Int;

        /// <summary>是否生成为 <c>List&lt;T&gt;</c>（策划一次管一批 GO 时用）。</summary>
        [Tooltip("勾选则生成 List<T>（如一批门）")]
        public bool isList;

        /// <summary>
        /// 枚举定义类名（<see cref="ConfigEnumDefinition.className"/>），仅
        /// <see cref="ConfigFieldType.Enum"/> 使用；在类型下拉里选「引用/枚举引用/…」自动写入。
        /// </summary>
        [Tooltip("枚举定义类名（在类型下拉里选）")]
        public string enumRefClassName = "";

        /// <summary>
        /// 配置引用目标类型的全名（<c>AssemblyQualifiedName</c>），仅
        /// <see cref="ConfigFieldType.Reference"/> 使用；生成的字段是 <c>long</c>（配置成员 id）。
        /// </summary>
        [Tooltip("配置引用目标类型（在类型下拉里选）")]
        public string refTypeFullName = "";

        /// <summary>客户端导出设置。</summary>
        public SceneContentFieldExport clientExport = new SceneContentFieldExport();

        /// <summary>服务端导出设置。默认不导出：服务端通常不需要可视对象引用。</summary>
        public SceneContentFieldExport serverExport = new SceneContentFieldExport(SceneContentExportMode.None);

        /// <summary>
        /// 组件 Inspector 中该字段只读（生成代码打 <c>[ReadOnly]</c>，绘制时禁用输入）。
        /// 仅影响展示，不影响导出。与配置模板字段的 <c>readOnly</c> 同语义。
        /// </summary>
        public bool readOnly;

        /// <summary>
        /// 关联参数：条件显示规则（AND）。生成代码转成字段上的 <c>[CfgVisibleWhen]</c>，
        /// 生成组件的 Inspector 据此按条件绘制 / 跳过本字段。目标字段只能是 bool / enum 标量字段；
        /// 无效条件在生成时跳过、求值时视为满足（保持显示），不会把字段吞掉。
        /// 与配置模板字段的 <c>visibility</c> 共用同一份条件结构。
        /// </summary>
        public List<ConfigVisibilityCondition> visibility = new List<ConfigVisibilityCondition>();
    }
}
