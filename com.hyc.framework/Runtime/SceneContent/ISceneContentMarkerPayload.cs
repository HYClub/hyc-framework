using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 标记组件的<b>自定义 payload 契约</b>。
    /// <para>
    /// 不实现它时，框架退回 <c>JsonUtility.ToJson(component)</c>——只适合纯标量字段：
    /// 那个 API 会把 <c>GameObject</c>/<c>Transform</c> 引用写成 <c>instanceID</c>，
    /// 跨会话不稳定，不能进 blob。
    /// </para>
    /// <para>
    /// 实现它，就能自己决定 payload 长什么样，并按<b>导出侧</b>区分内容
    /// （如服务端不导 GO 引用）。生成的标记组件会自动实现本接口。
    /// </para>
    /// </summary>
    public interface ISceneContentMarkerPayload
    {
        /// <summary>
        /// 产出本组件的 payload JSON 文本。
        /// </summary>
        /// <param name="levelRoot">关卡根（<see cref="SceneContentAuthoring"/> 所在 GO），用于把 GO 引用算成相对层级路径。</param>
        /// <param name="side">导出侧：决定哪些字段进这份 payload、GO 引用用哪种表示。</param>
        /// <returns>紧凑 JSON 文本；无字段可写时返回空对象 <c>{}</c>。</returns>
        string ToPayloadJson(GameObject levelRoot, SceneContentSide side);
    }
}
