using HYC.Framework.SceneContent;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace HYC.Simple
{
    /// <summary>示例 spawner：运行期把每条内容生成到场景里（这里仅打日志，真实项目按 id 生成 prefab/entity）。</summary>
    public class HYCSimpleSceneContentSpawner : ISceneContentSpawner
    {
        public void Spawn(int contentTypeId, float3 position, quaternion rotation, float3 scale, NativeArray<byte> payload)
        {
            var json = payload.Length > 0
                ? System.Text.Encoding.UTF8.GetString(payload.ToArray())
                : "";
            Debug.Log($"[HYCSimple] 生成内容 typeId={contentTypeId} @ ({position.x:F1},{position.y:F1},{position.z:F1}) payload={json}");
        }
    }
}
