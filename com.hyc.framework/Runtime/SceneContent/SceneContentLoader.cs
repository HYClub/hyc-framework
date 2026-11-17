using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HYC.Framework.IO;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 运行期加载：游戏在关卡（场景 / prefab）加载完成后调
    /// <see cref="LoadByKey(string, ISceneContentSpawner, SceneContentSide)"/>（推荐）或
    /// <see cref="Load(string, ISceneContentSpawner, SceneContentSide)"/>（已有资产 GUID 时的底层通道）。
    /// 复用 <see cref="BlobReader"/> + <c>BlobAssetReference&lt;T&gt;.TryRead</c> 做版本校验，
    /// 逐条目调 <see cref="ISceneContentSpawner.Spawn"/>。
    /// <para>
    /// Blob 身份 = 资产 .meta 的 GUID（导出时以此命名文件）；运行期身份 = 业务 key，
    /// 由导出期生成的 <c>SceneContent/index.json</c> 把 key 映射成 GUID，无需手抄资产 GUID。
    /// </para>
    /// <para>运行期只加载客户端 Blob：服务端数据写在外，由服务端进程自行读取，
    /// 既不产 Blob 也不进客户端包体。</para>
    /// </summary>
    public static class SceneContentLoader
    {
        private const string BlobRelativeDir = "SceneContent/";

        /// <summary>
        /// 按<b>业务 key</b>（配置 id / Addressables 地址 / 资产名）加载：读导出期生成的
        /// <c>SceneContent/index.json</c> 把 key 换成关卡资产 GUID，再复用
        /// <see cref="Load(string, ISceneContentSpawner, SceneContentSide)"/>。
        /// <para>
        /// 关卡加载器运行期只持有业务身份（如 <c>LevelConfig.MapAddress</c>），拿不到资产 GUID；
        /// 这是推荐的运行期入口。清单由编辑器导出时自动维护，无需手抄 GUID。
        /// <paramref name="side"/> 只支持 Client；服务端数据不在客户端包体内。
        /// </para>
        /// </summary>
        /// <param name="key">导出时登记的业务 key（显式 alias > Addressables 地址 > 资产名）。</param>
        public static bool LoadByKey(string key, ISceneContentSpawner spawner,
            SceneContentSide side = SceneContentSide.Client)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[SceneContent] 业务 key 为空，无法加载关卡内容。");
                return false;
            }

            // 必须走统一通道：WebGL / Android 上清单由构建期 hyc_streaming_manifest.txt 预取进缓存
            var text = StreamingAssets.ReadText(SceneContentManifest.RelativePath);
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogError($"[SceneContent] 找不到映射清单 {SceneContentManifest.RelativePath}。" +
                               "请先在编辑器运行 HYC Framework/Scene Content/Export Blob（或 Export All Blobs）；" +
                               "WebGL / Android 还需确认已完成 StreamingAssets 预取（FrameworkBootstrap.InitializeAsync）。");
                return false;
            }

            SceneContentManifest manifest;
            try
            {
                // 容错：清单若被外部工具写成带 BOM，Newtonsoft.Json 会解析失败
                manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<SceneContentManifest>(
                    text.TrimStart('\uFEFF', '\u200B'));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneContent] 映射清单解析失败（{SceneContentManifest.RelativePath}）：{e.Message}");
                return false;
            }

            if (manifest == null || manifest.entries == null || manifest.entries.Count == 0)
            {
                Debug.LogError($"[SceneContent] 映射清单为空（{SceneContentManifest.RelativePath}），没有可用业务 key。请重新导出 Blob。");
                return false;
            }

            for (int i = 0; i < manifest.entries.Count; i++)
            {
                var entry = manifest.entries[i];
                if (entry != null && string.Equals(entry.key, key, System.StringComparison.Ordinal))
                    return Load(entry.guid, spawner, side);
            }

            // 未命中：把可用 key 全列出来，避免「不知道该传什么」的静默失败
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < manifest.entries.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(manifest.entries[i] != null ? manifest.entries[i].key : "<null>");
            }
            Debug.LogError($"[SceneContent] 业务 key「{key}」不在映射清单里。可用 key：{sb}。" +
                           "请检查导出时的 alias / Addressables 地址 / 资产名，或重新运行 Export Blob。");
            return false;
        }

        /// <summary>
        /// 从 StreamingAssets 读取 <c>SceneContent/&lt;guid&gt;.blob</c> 并生成。
        /// 需 StreamingAssets 已预取（走异步引导入口 <c>FrameworkBootstrap.InitializeAsync</c>）。
        /// <para><paramref name="side"/> 只支持 <see cref="SceneContentSide.Client"/>：服务端数据在
        /// <c>ServerExportDir/SceneContent/&lt;guid&gt;.json</c>，由服务端进程自行读取，不在客户端包体内。</para>
        /// </summary>
        /// <param name="guid">关卡资产 .meta 的 GUID（与导出文件名一致）。</param>
        /// <param name="spawner">生成实例的注入点（GameObject 或 Entity 由游戏实现）。</param>
        /// <param name="side">保留兼容；传 Server 会明确报错并返回 false。</param>
        public static bool Load(string guid, ISceneContentSpawner spawner,
            SceneContentSide side = SceneContentSide.Client)
        {
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("[SceneContent] guid 为空，无法加载关卡内容。");
                return false;
            }
            if (side == SceneContentSide.Server)
            {
                Debug.LogError("[SceneContent] 服务端数据不随客户端包体分发：请在服务端进程读取 " +
                               "ServerExportDir/SceneContent/<guid>.json。" +
                               "运行期 Load / LoadByKey 只支持 SceneContentSide.Client。");
                return false;
            }
            var bytes = StreamingAssets.ReadBytes(BlobRelativeDir + guid + ".blob");
            return LoadBytes(bytes, guid, spawner);
        }

        /// <summary>从已有字节加载（如 Addressables 拿到的字节）。</summary>
        public static bool LoadBytes(byte[] bytes, string guid, ISceneContentSpawner spawner)
        {
            if (spawner == null)
            {
                Debug.LogError("[SceneContent] spawner 为空，无法生成。");
                return false;
            }
            if (!BlobReader.TryRead<SceneContentBlob>(bytes, SceneContentBlob.BlobVersion, out var blobRef))
            {
                Debug.LogError($"[SceneContent] 读取关卡 {guid} 的 Blob 失败（版本不符或文件损坏）。");
                return false;
            }
            try
            {
                ref var root = ref blobRef.Value;
                // ★EA0001：Blob 成员（BlobArray）必须按非只读 ref 访问，不能按值拷出。
                //   自校验：Blob 内的 LevelGuid 是导出端 Encoding.UTF8.GetBytes(guid) 写入的，
                //   还原后与调用方给的 guid 比对——不符说明加载到了错误关卡的 blob，明确报错而非静默生成。
                ref var guidBytes = ref root.LevelGuid;
                var guidBuf = new byte[guidBytes.Length];
                for (int k = 0; k < guidBytes.Length; k++) guidBuf[k] = guidBytes[k];
                var blobGuid = System.Text.Encoding.UTF8.GetString(guidBuf);
                if (!string.Equals(blobGuid, guid, StringComparison.Ordinal))
                {
                    Debug.LogError($"[SceneContent] 关卡身份不匹配：请求 guid={guid}，blob 内 LevelGuid={blobGuid}。" +
                                   "已中止生成，避免加载错关卡。请重新导出或核对 index.json 映射。");
                    return false;
                }

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
                        // Blob 里是扁平 float（见 SceneContentEntryBlob 注释），此处重组为 math 类型交给 spawner
                        spawner.Spawn(
                            e.ContentTypeId,
                            new float3(e.PosX, e.PosY, e.PosZ),
                            new quaternion(e.RotX, e.RotY, e.RotZ, e.RotW),
                            new float3(e.ScaleX, e.ScaleY, e.ScaleZ),
                            na);
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
