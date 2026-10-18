using HYC.Framework.SceneContent;
using UnityEngine;

namespace HYC.Simple
{
    /// <summary>示例标记组件：角色/怪物出生点。实现 ISceneContentMarker 即被自动发现并纳入导出。</summary>
    public class HYCSimpleSpawnPoint : MonoBehaviour, ISceneContentMarker
    {
        [Tooltip("出生点类型：0=角色 1=怪物。作为 ContentTypeId 写入 Blob。")]
        public int spawnKind;

        [Tooltip("初始朝向（度），会随 transform 一起被记录。")]
        public float facingDeg;

        // ISceneContentMarker：稳定 id 写进 Blob。真实项目建议来自配置 id 或常量枚举，而非派生值。
        public int ContentTypeId => 1000 + spawnKind;
    }
}
