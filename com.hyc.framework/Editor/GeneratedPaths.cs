// ============================================================
// HYC Framework - Editor
// 文件: Editor/GeneratedPaths.cs
// 说明: 生成代码落点的**单一真源**。所有生成器（配置/UI/属性/天赋/BT 自定义节点）
//       的输出目录常量统一从这里派生——挪总根只改本文件一处。
//       历史散落过 5 个目录（Assets/GeneratedConfigs、Assets/GeneratedUI、
//       Assets/HYC/Generated/Attributes、Assets/Talent/Generated、Assets/BTTrees/Nodes），
//       已统一收口到 Root 下按模块分文件夹。
// ============================================================

namespace HYC.Framework.Editor
{
    /// <summary>
    /// 生成代码目录常量（工程相对路径）。各生成器输出目录必须引用本类，
    /// 禁止在生成器文件里另写死路径（曾因 5 处各自为政导致宿主工程里生成代码四处散落）。
    /// <para>二进制 Blob（配置 / BT / 本地化）落 StreamingAssets，是运行期加载契约，
    /// 不属于代码收口范围，不在本类内。</para>
    /// </summary>
    public static class GeneratedPaths
    {
        /// <summary>生成代码总根目录。</summary>
        public const string Root = "Assets/HYC/Generated";

        /// <summary>配置模块（原 <c>Assets/GeneratedConfigs</c>）。主类 / 枚举 / Blob / Client / Server / 门面直接落此目录。</summary>
        public const string Config = Root + "/Config";

        /// <summary>
        /// 配置 Editor 工具子目录（XxxConfigEditor / Check / BlobBuilder）。
        /// 必须保持在 Editor 特殊目录下——Unity 程序集约定（任意层级的 Editor 文件夹进 Editor 程序集），
        /// 配置 Editor 工具要引用 AssetDatabase/GUIDrawer，不能在运行时程序集。
        /// </summary>
        public const string ConfigEditor = Config + "/Editor";

        /// <summary>UI 层常量模块（原 <c>Assets/GeneratedUI</c>），单文件 <c>UILayer.g.cs</c>。</summary>
        public const string UI = Root + "/UI";

        /// <summary>属性三件套（原 <c>Assets/HYC/Generated/Attributes</c>）：AttrKey / AttrKeyMap / AttrBag。</summary>
        public const string Attr = Root + "/Attr";

        /// <summary>天赋三件套 + prefab（原 <c>Assets/Talent/Generated</c>）。</summary>
        public const string Talent = Root + "/Talent";

        /// <summary>BT 自定义节点代码（原 <c>Assets/BTTrees/Nodes</c>，与 BT 资产目录分离）。</summary>
        public const string BTNodes = Root + "/BT/Nodes";
    }
}
