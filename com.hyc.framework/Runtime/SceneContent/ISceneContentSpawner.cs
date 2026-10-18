using Unity.Collections;
using Unity.Mathematics;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 运行期生成实例的注入点。框架只负责读 Blob + 解析，具体生成 GameObject 还是 Entity
    /// 由游戏实现本接口并传入 <see cref="SceneContentLoader.Load(int, ISceneContentSpawner)"/>。
    /// <para>
    /// <paramref name="payload"/> 为该条目的自定义参数（UTF8 JSON 字节），由游戏按
    /// <paramref name="contentTypeId"/> 自行反序列化——框架只负责原样搬运。
    /// </para>
    /// </summary>
    public interface ISceneContentSpawner
    {
        /// <summary>
        /// 生成一条场景内容。
        /// </summary>
        /// <param name="contentTypeId">标记/实例类型 id（与导出时一致）。</param>
        /// <param name="position">局部坐标。</param>
        /// <param name="rotation">局部旋转。</param>
        /// <param name="scale">局部缩放。</param>
        /// <param name="payload">自定义参数字节（UTF8 JSON）；生命周期仅限本次调用。</param>
        void Spawn(int contentTypeId, float3 position, quaternion rotation, float3 scale, NativeArray<byte> payload);
    }
}
