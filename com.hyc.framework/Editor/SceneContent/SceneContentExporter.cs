using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using HYC.Framework.IO;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容导出：合并 A（实例条目，经 <see cref="SceneContentResolvers"/> 解析 id）+ B（标记组件）
    /// 为统一条目，按导出侧写
    /// <c>StreamingAssets/SceneContent/&lt;levelId&gt;.blob</c>（客户端）与
    /// <c>StreamingAssets/SceneContent/server/&lt;levelId&gt;.blob</c>（服务端），并做 TryRead 往返自校验。
    /// <para>
    /// 两侧的差异只体现在 payload：标记组件按 <see cref="ISceneContentMarkerPayload"/> 收到 <see cref="SceneContentSide"/>，
    /// 自行决定哪些字段进这份 JSON。A 类手写条目只有一个 JSON，两侧共用（手写内容不受字段级导出设置管辖）。
    /// </para>
    /// </summary>
    public static class SceneContentExporter
    {
        /// <summary>客户端 Blob 输出目录（必须位于 StreamingAssets 内，运行期才能读取）。</summary>
        public const string BlobDir = "Assets/StreamingAssets/SceneContent";

        /// <summary>服务端 Blob 输出子目录（与 <see cref="SceneContentLoader"/> 的读取路径一致）。</summary>
        public const string ServerSubDir = "server";

        private const string Ext = ".blob";

        /// <summary>合并 A + B 为统一条目列表（客户端侧，不做 Blob 写盘）。</summary>
        public static List<SceneContentEntryData> Collect(SceneContentAuthoring authoring)
            => Collect(authoring, SceneContentSide.Client);

        /// <summary>合并 A + B 为统一条目列表（指定导出侧，不做 Blob 写盘）。</summary>
        public static List<SceneContentEntryData> Collect(SceneContentAuthoring authoring, SceneContentSide side)
        {
            var all = new List<SceneContentEntryData>();
            // A. 实例条目：经解析器把 prefab → contentTypeId；payload 为手写 JSON，两侧共用
            foreach (var e in authoring.entries)
            {
                if (e == null || e.prefab == null) continue;
                if (!SceneContentResolvers.TryResolve(e.prefab, out int id))
                {
                    Debug.LogError($"[SceneContent] 实例条目 prefab「{e.prefab.name}」无法解析 contentTypeId" +
                                   "（prefab 上没有 ISceneContentMarker？），跳过。");
                    continue;
                }
                all.Add(new SceneContentEntryData
                {
                    contentTypeId = id,
                    position = e.position,
                    rotation = e.rotation,
                    scale = e.scale,
                    payloadJson = e.payloadJson ?? "",
                });
            }
            // B. 标记组件：自动发现，payload 按侧投影
            all.AddRange(authoring.GetMarkerEntries(side));
            return all;
        }

        /// <summary>导出单个关卡描述器（客户端；<see cref="SceneContentAuthoring.exportServer"/> 为真时连服务端一起）。</summary>
        public static bool Export(SceneContentAuthoring authoring)
        {
            if (authoring == null) return false;
            var ok = ExportSide(authoring, SceneContentSide.Client);
            if (authoring.exportServer)
                ok &= ExportSide(authoring, SceneContentSide.Server);
            return ok;
        }

        /// <summary>导出工程内全部 SceneContentAuthoring。返回成功数。</summary>
        public static int ExportAll()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(SceneContentAuthoring).FullName);
            var ok = 0;
            foreach (var g in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var a = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneContentAuthoring>(path);
                if (a == null) continue;
                if (Export(a)) ok++;
            }
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[SceneContent] 全量导出：{ok}/{guids.Length} 个关卡描述器成功。");
            return ok;
        }

        /// <summary>导出某一侧，并读回自校验（版本 / levelId / 条数）。</summary>
        public static bool ExportSide(SceneContentAuthoring authoring, SceneContentSide side)
        {
            var data = Collect(authoring, side);
            var dir = side == SceneContentSide.Server ? BlobDir + "/" + ServerSubDir : BlobDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, authoring.levelId + Ext);
            WriteBlob(path, authoring.levelId, data);

            // 自校验：读回确认版本、levelId 与条数一致
            var bytes = File.ReadAllBytes(path);
            if (!BlobReader.TryRead<SceneContentBlob>(bytes, SceneContentBlob.BlobVersion, out var check))
            {
                Debug.LogError($"[SceneContent] 导出后自校验失败（无法读回）：{path}");
                return false;
            }
            // 先取快照再 Dispose：BlobAssetReference 释放后再访问 .Value 属非法使用
            var readBackLevelId = check.Value.LevelId;
            var readBackCount = check.Value.Entries.Length;
            check.Dispose();
            var ok = readBackCount == data.Count && readBackLevelId == authoring.levelId;
            if (ok)
                Debug.Log($"[SceneContent] 导出完成（{side}）：levelId={authoring.levelId}，{data.Count} 条 → {path}");
            else
                Debug.LogError($"[SceneContent] 导出自校验不一致（{side}）：期望 levelId={authoring.levelId}/{data.Count} 条，" +
                               $"实际 {readBackLevelId}/{readBackCount} 条。");
            return ok;
        }

        private static void WriteBlob(string path, int levelId, List<SceneContentEntryData> data)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref builder.ConstructRoot<SceneContentBlob>();
                root.LevelId = levelId;
                var arr = builder.Allocate(ref root.Entries, data.Count);
                for (int i = 0; i < data.Count; i++)
                {
                    var d = data[i];
                    var payloadBytes = Encoding.UTF8.GetBytes(d.payloadJson ?? "");
                    // 只写扁平 float（UnityEngine.Vector3/Quaternion 的 x/y/z/w），
                    // 不出现任何 Unity.Mathematics 类型——Editor 程序集（asmdef）没有该引用，
                    // 触碰 math 字段会 CS0012 / EA0003（见 SceneContentEntryBlob 注释）。
                    arr[i] = new SceneContentEntryBlob
                    {
                        ContentTypeId = d.contentTypeId,

                        PosX = d.position.x,
                        PosY = d.position.y,
                        PosZ = d.position.z,

                        RotX = d.rotation.x,
                        RotY = d.rotation.y,
                        RotZ = d.rotation.z,
                        RotW = d.rotation.w,

                        ScaleX = d.scale.x,
                        ScaleY = d.scale.y,
                        ScaleZ = d.scale.z,
                    };
                    var payloadArr = builder.Allocate(ref arr[i].Payload, payloadBytes.Length);
                    for (int k = 0; k < payloadBytes.Length; k++)
                        payloadArr[k] = payloadBytes[k];
                }
                BlobAssetReference<SceneContentBlob>.Write(builder, path, SceneContentBlob.BlobVersion);
            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}
