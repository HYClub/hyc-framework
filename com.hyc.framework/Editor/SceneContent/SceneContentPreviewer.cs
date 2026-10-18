using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 编辑器预览：把 <see cref="SceneContentAuthoring.entries"/>（A. 实例条目）实例化进当前场景，
    /// 方便看图/微调。标记组件（B）本身就在 prefab 上，无需实例化。
    /// </summary>
    public static class SceneContentPreviewer
    {
        private const string ContainerName = "__SceneContentPreview";

        /// <summary>data→scene：清空旧预览，按实例条目实例化 prefab 预览。</summary>
        public static void Refresh(SceneContentAuthoring authoring)
        {
            if (authoring == null) return;
            // 关卡 prefab 作为 prefab 资产（Prefab Mode）时不往资产里塞预览容器，避免改脏资产；
            // 预览请在场景里的 prefab 实例上使用。
            if (PrefabUtility.IsPartOfPrefabAsset(authoring))
            {
                Debug.LogWarning("[SceneContent] 刷新预览仅在场景中的 prefab 实例上可用；当前目标是 prefab 资产，已跳过（避免改脏资产）。");
                return;
            }
            Clear(authoring);
            var container = GetContainer(authoring);
            var count = 0;
            foreach (var e in authoring.entries)
            {
                if (e == null || e.prefab == null) continue;
                var go = Object.Instantiate(e.prefab, container.transform);
                go.transform.localPosition = e.position;
                go.transform.localRotation = e.rotation;
                go.transform.localScale = e.scale;
                var inst = go.AddComponent<SceneContentInstance>();
                inst.sourcePrefab = e.prefab;
                inst.contentTypeId = -1;
                inst.payloadJson = e.payloadJson;
                count++;
            }
            Debug.Log($"[SceneContent] 刷新预览：{count} 个实例条目（标记组件已随 prefab 存在，不重复实例化）。");
        }

        /// <summary>清空上一次预览生成的容器。</summary>
        public static void Clear(SceneContentAuthoring authoring)
        {
            if (authoring == null) return;
            var existing = authoring.transform.Find(ContainerName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        private static Transform GetContainer(SceneContentAuthoring authoring)
        {
            var existing = authoring.transform.Find(ContainerName);
            if (existing != null) return existing;
            var go = new GameObject(ContainerName);
            go.hideFlags = HideFlags.DontSaveInEditor;
            go.transform.SetParent(authoring.transform, false);
            return go.transform;
        }
    }
}
