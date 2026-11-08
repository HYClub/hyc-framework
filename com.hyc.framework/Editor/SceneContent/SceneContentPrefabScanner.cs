using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>关卡 prefab 上一个标记组件的扫描结果。</summary>
    public sealed class SceneContentMarkerInfo
    {
        /// <summary>组件本身（编辑器里可 Ping / 选中）。</summary>
        public Component Component;

        /// <summary>组件类型名。</summary>
        public string TypeName;

        /// <summary>组件声明的内容类型 id。</summary>
        public int ContentTypeId;

        /// <summary>组件 id 是否等于调用方传入的 targetContentTypeId（全局总览传 -1 时恒为 false）。</summary>
        public bool MatchesTarget;

        /// <summary>组件在关卡里的层级路径（相对扫描根）。</summary>
        public string Path;
    }

    /// <summary>
    /// 关卡 prefab / 场景里的标记组件<b>只读发现</b>。
    /// <para>
    /// 用途：列出「这个关卡 / prefab 上挂了哪些场景内容、各是什么类型、几个、层级路径」。
    /// 纯只读，不写资产、不反填字段定义（反填功能已移除——新功能没有旧组件要迁移）。
    /// 内容总览页签与类型设计器共用本扫描。
    /// </para>
    /// </summary>
    public static class SceneContentPrefabScanner
    {
        /// <summary>扫描 <paramref name="root"/> 及其子物体上的全部标记组件。</summary>
        /// <param name="root">扫描根（关卡 prefab 或场景里的关卡根）。</param>
        /// <param name="targetContentTypeId">当前编辑的类型 id，用于标记 <see cref="SceneContentMarkerInfo.MatchesTarget"/>。</param>
        public static List<SceneContentMarkerInfo> Scan(GameObject root, int targetContentTypeId)
        {
            var result = new List<SceneContentMarkerInfo>();
            if (root == null)
                return result;

            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (!(c is ISceneContentMarker marker)) continue;
                result.Add(new SceneContentMarkerInfo
                {
                    Component = c,
                    TypeName = c.GetType().Name,
                    ContentTypeId = marker.ContentTypeId,
                    MatchesTarget = marker.ContentTypeId == targetContentTypeId,
                    Path = SceneContentRefPath.GetPath(root, c.transform),
                });
            }
            return result;
        }
    }
}
