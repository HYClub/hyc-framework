using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HYC.Framework.IO;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 运行期加载：游戏在场景加载完成后调 <see cref="Load(int, ISceneContentSpawner)"/>。
    /// 复用 <see cref="BlobReader"/> + <c>BlobAssetReference&lt;T&gt;.TryRead</c> 做版本校验，
    /// 逐条目调 <see cref="ISceneContentSpawner.Spawn"/>。
    /// </summary>
    public static class SceneContentLoader
    {
        private const string BlobRelativeDir = "SceneContent/";

        /// <summary>
        /// 从 StreamingAssets 读取 <c>SceneContent/&lt;levelId&gt;.blob</c> 并生成。
        /// 需 StreamingAssets 已预取（走异步引导入口 <c>FrameworkBootstrap.InitializeAsync</c>）。
        /// </summary>
        public static bool Load(int levelId, ISceneContentSpawner spawner)
        {
            var bytes = StreamingAssets.ReadBytes(BlobRelativeDir + levelId + ".blob");
            return LoadBytes(bytes, levelId, spawner);
        }

        /// <summary>从已有字节加载（如 Addressables 拿到的字节）。</summary>
        public static bool LoadBytes(byte[] bytes, int levelId, ISceneContentSpawner spawner)
        {
            if (spawner == null)
            {
                Debug.LogError("[SceneContent] spawner 为空，无法生成。");
                return false;
            }
            if (!BlobReader.TryRead<SceneContentBlob>(bytes, SceneContentBlob.BlobVersion, out var blobRef))
            {
                Debug.LogError($"[SceneContent] 读取关卡 {levelId} 的 Blob 失败（版本不符或文件损坏）。");
                return false;
            }
            try
            {
                ref var root = ref blobRef.Value;
                // ★EA0001：Blob 成员可能只活在 blob storage 里，必须以（非只读）ref 访问，
                //   不能按值拷出（`var arr = root.Entries` 会报 EA0001）。
                ref var arr = ref root.Entries;
                for (int i = 0; i < arr.Length; i++)
                {
                    ref var e = ref arr[i];
                    ref var payload = ref e.Payload;
                    // BlobArray 不是 NativeArray；复制进临时 NativeArray，生命周期覆盖本次 Spawn 调用。
                    // ★不能用 `using var na`：using 变量是只读的，写入 na[j] 会报 CS1654 ——
                    //   改为显式 try/finally 保证释放。
                    var na = new NativeArray<byte>(payload.Length, Allocator.Temp);
                    try
                    {
                        for (int j = 0; j < payload.Length; j++)
                            na[j] = payload[j];
                        spawner.Spawn(e.ContentTypeId, e.Position, e.Rotation, e.Scale, na);
                    }
                    finally
                    {
                        na.Dispose();
                    }
                }
                return true;
            }
            finally
            {
                blobRef.Dispose();
            }
        }
    }
}
