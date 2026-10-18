namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 场景内容标记组件契约。任何挂在关卡 prefab（或其子物体）上的 MonoBehaviour 实现本接口，
    /// 即被视为「场景内容物标记」，被 <see cref="SceneContentAuthoring"/> 自动发现并纳入导出/加载。
    /// <para>
    /// <see cref="ContentTypeId"/> 必须是游戏定义的<b>稳定 int</b>（建议来自配置 id 或常量枚举值）：
    /// Blob 内禁止托管引用，prefab / 配置引用一律收成 int id，运行期由
    /// <see cref="ISceneContentSpawner"/> 经 id 解析回实际 prefab / entity。
    /// </para>
    /// </summary>
    public interface ISceneContentMarker
    {
        /// <summary>该标记类型对应的内容 id（游戏定义，稳定）。</summary>
        int ContentTypeId { get; }
    }
}
