namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 导出期生成的「业务 key → 关卡资产 GUID」映射清单，落
    /// <c>StreamingAssets/SceneContent/index.json</c>。
    /// <para>
    /// 为什么需要它：<c>AssetDatabase.AssetPathToGUID</c> 只在编辑器可用，打包后关卡加载器
    /// 手里只有业务身份（配置 id / Addressables 地址 / 资产名），拿不到资产 GUID。
    /// 本清单在导出时把两者绑起来，运行期由 <see cref="SceneContentLoader.LoadByKey"/> 查表换 GUID。
    /// </para>
    /// <para>Editor 与 Runtime 共用同一份类型，两侧都用 Newtonsoft.Json 读写。</para>
    /// </summary>
    [System.Serializable]
    public class SceneContentManifest
    {
        /// <summary>相对 StreamingAssets 根的路径（与 <c>SceneContentExporter.BlobDir</c> 对应）。</summary>
        public const string RelativePath = "SceneContent/index.json";

        /// <summary>清单格式版本；结构变更时 +1。</summary>
        public const int ManifestVersion = 1;

        public int version = ManifestVersion;

        // 刻意不预初始化：Newtonsoft.Json 的 ObjectCreationHandling.Auto 对已初始化的集合
        // 会「复用并追加」而非替换。保持 null，让反序列化完全决定内容；写入侧已判空。
        public System.Collections.Generic.List<SceneContentManifestEntry> entries;
    }

    /// <summary>一条映射：业务 key → 关卡资产 GUID（assetPath 仅用于诊断）。</summary>
    [System.Serializable]
    public class SceneContentManifestEntry
    {
        public string key;
        public string guid;
        public string assetPath;
    }
}
