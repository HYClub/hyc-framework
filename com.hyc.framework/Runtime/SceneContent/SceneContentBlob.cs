using Unity.Entities;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 单条场景内容条目（blittable）。Blob 内禁止托管类型，故 prefab / 配置引用一律收成 int id，
    /// 运行期由 <see cref="ISceneContentSpawner"/> 经 id 解析回实际 prefab / entity。
    /// <para>
    /// 坐标/旋转/缩放刻意用<b>扁平 float</b> 而非 <c>float3</c>/<c>quaternion</c>：
    /// 导出器位于 Editor 程序集（asmdef 不引用 Unity.Mathematics），触碰 math 字段会
    /// CS0012 / EA0003。纯 float 布局让两侧都零依赖，也是更稳的磁盘格式。
    /// </para>
    /// <para><see cref="Payload"/> 为 UTF8 JSON 字节，含义由游戏按 <see cref="ContentTypeId"/> 解释。</para>
    /// </summary>
    public struct SceneContentEntryBlob
    {
        public int ContentTypeId;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float RotX;
        public float RotY;
        public float RotZ;
        public float RotW;

        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;

        public BlobArray<byte> Payload;
    }

    /// <summary>
    /// 场景内容 Blob 根（blittable）。<see cref="Entries"/> 合并了「实例条目」与「标记组件」两种来源。
    /// </summary>
    public struct SceneContentBlob
    {
        /// <summary>
        /// Blob 格式版本。改动条目结构时 +1；运行期 <c>TryRead</c> 版本不符会拒绝加载。
        /// <para>v2：条目坐标从 <c>float3</c>/<c>quaternion</c> 改为扁平 float（消除 Editor 侧对 Unity.Mathematics 的依赖）。</para>
        /// </summary>
        public const int BlobVersion = 2;

        public int LevelId;
        public BlobArray<SceneContentEntryBlob> Entries;
    }
}
