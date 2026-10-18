using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容导出：合并 A（实例条目，经 <see cref="SceneContentResolvers"/> 解析 id）+ B（标记组件）
    /// 为统一条目，写 <c>StreamingAssets/SceneContent/&lt;levelId&gt;.blob</c>，并做 TryRead 往返自校验。
    /// </summary>
    public static class SceneContentExporter
    {
        /// <summary>Blob 输出目录（必须位于 StreamingAssets 内，运行期才能读取）。</summary>
        public const string BlobDir = "Assets/StreamingAssets/SceneContent";
        private const string Ext = ".blob";

        /// <summary>合并 A + B 为统一 <see cref="SceneContentEntryData"/> 列表（不做 Blob 写盘）。</summary>
        public static List<SceneContentEntryData> Collect(SceneContentAuthoring authoring)
        {
            var all = new List<SceneContentEntryData>();
            // A. 实例条目：经解析器把 prefab → contentTypeId
            foreach (var e in authoring.entries)
            {
                if (e == null || e.prefab == null) continue;
                if (!SceneContentResolvers.TryResolve(e.prefab, out int id))
                {
                    Debug.LogError($"[SceneContent] 实例条目 prefab「{e.prefab.name}」无法解析 contentTypeId（未注册 ISceneContentPrefabResolver？），跳过。");
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
            // B. 标记组件：自动发现
            all.AddRange(authoring.GetMarkerEntries());
            return all;
        }

        /// <summary>导出单个关卡描述器为 Blob（含自校验）。</summary>
        public static bool Export(SceneContentAuthoring authoring)
        {
            if (authoring == null) return false;
            var data = Collect(authoring);
            if (!Directory.Exists(BlobDir)) Directory.CreateDirectory(BlobDir);
            var path = Path.Combine(BlobDir, authoring.levelId + Ext);
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
                Debug.Log($"[SceneContent] 导出完成：levelId={authoring.levelId}，{data.Count} 条 → {path}");
            else
                Debug.LogError($"[SceneContent] 导出自校验不一致：levelId 或条数不符（期望 levelId={authoring.levelId}/{data.Count} 条，实际 {readBackLevelId}/{readBackCount} 条）。");
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
                    // 刻意不 using Unity.Mathematics：本程序集（asmdef）无该引用。
                    // Blob 字段是 float3/quaternion，由 UnityEngine  Vector3/Quaternion 隐式转换写入
                    // （math_unity_conversion.cs 实证 implicit operator float3(Vector3) / quaternion(Quaternion)）。
                    arr[i] = new SceneContentEntryBlob
                    {
                        ContentTypeId = d.contentTypeId,
                        Position = d.position,
                        Rotation = d.rotation,
                        Scale = d.scale,
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
