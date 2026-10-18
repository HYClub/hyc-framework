using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 编辑器采集：把场景中带 <see cref="SceneContentInstance"/> 的 GO 读回
    /// <see cref="SceneContentAuthoring.entries"/>（scene→data）。标记组件（B）是组件式作者，无需采集。
    /// </summary>
    public static class SceneContentCapturer
    {
        /// <summary>
        /// 扫描带 <see cref="SceneContentInstance"/> 的 GO，回写实例条目。
        /// <paramref name="append"/>=false 时覆盖全部条目；=true 时追加。
        /// </summary>
        public static void Capture(SceneContentAuthoring authoring, bool append = false)
        {
            if (authoring == null) return;
            var found = authoring.GetComponentsInChildren<SceneContentInstance>(true);
            var newEntries = new List<SceneContentEntry>();
            foreach (var inst in found)
            {
                if (inst.sourcePrefab == null) continue;
                var t = inst.transform;
                if (!SceneContentResolvers.TryResolve(inst.sourcePrefab, out int id))
                    id = inst.contentTypeId;
                newEntries.Add(new SceneContentEntry
                {
                    prefab = inst.sourcePrefab,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                    payloadJson = inst.payloadJson,
                });
            }
            // 直接写字段不走序列化流：必须 Undo 记录 + SetDirty，否则 prefab 资产的改动不落盘
            Undo.RecordObject(authoring, "Scene Content Capture");
            if (append) authoring.entries.AddRange(newEntries);
            else authoring.entries = newEntries;
            EditorUtility.SetDirty(authoring);
            Debug.Log($"[SceneContent] 采集自场景：{newEntries.Count} 个实例条目（{(append ? "追加" : "覆盖")}）。");
        }
    }
}
