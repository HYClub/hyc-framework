using Unity.Entities;
using Unity.Mathematics;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 单条场景内容条目（blittable）。Blob 内禁止托管类型，故 prefab / 配置引用一律收成 int id，
    /// 运行期由 <see cref="ISceneContentSpawner"/> 经 id 解析回实际 prefab / entity。
    /// <para><see cref="Payload"/> 为 UTF8 JSON 字节，含义由游戏按 <see cref="ContentTypeId"/> 解释。</para>
    /// </summary>
    public struct SceneContentEntryBlob
    {
        public int ContentTypeId;
        public float3 Position;
        public quaternion Rotation;
        public float3 Scale;
        public BlobArray<byte> Payload;
    }

    /// <summary>
    /// 场景内容 Blob 根（blittable）。<see cref="Entries"/> 合并了「实例条目」与「标记组件」两种来源。
    /// </summary>
    public struct SceneContentBlob
    {
        /// <summary>Blob 格式版本。改动条目结构时 +1；运行期 <c>TryRead</c> 版本不符会拒绝加载。</summary>
        public const int BlobVersion = 1;

        public int LevelId;
        public BlobArray<SceneContentEntryBlob> Entries;
    }
}
