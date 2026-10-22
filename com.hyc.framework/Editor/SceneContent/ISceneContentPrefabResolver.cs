using HYC.Framework.SceneContent;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 把实例条目的 prefab 引用解析成稳定 int id（写入 Blob）。
    /// 游戏可注册一个实现覆盖默认行为（例如内部查配置表 SpawnDef，或手动字典映射）。
    /// </summary>
    public interface ISceneContentPrefabResolver
    {
        /// <summary>返回 prefab 对应的内容 id；无法识别时返回负数。</summary>
        int GetContentTypeId(GameObject prefab);
    }

    /// <summary>解析器注册表（编辑器内全局唯一）。</summary>
    public static class SceneContentResolvers
    {
        private static ISceneContentPrefabResolver _resolver;

        /// <summary>注册（或替换）自定义解析器；传 null 恢复内置兜底。</summary>
        public static void Register(ISceneContentPrefabResolver resolver) => _resolver = resolver;

        /// <summary>
        /// 尝试把 prefab 解析成 id。先问自定义解析器，未命中再走内置兜底
        /// （<see cref="SceneContentMarkerResolver"/>：读 prefab 上标记组件自己的 id），
        /// 因此挂了生成标记组件的 prefab <b>无需任何注册</b>即可导出。
        /// </summary>
        public static bool TryResolve(GameObject prefab, out int contentTypeId)
        {
            contentTypeId = -1;
            if (prefab == null) return false;

            if (_resolver != null)
            {
                contentTypeId = _resolver.GetContentTypeId(prefab);
                if (contentTypeId >= 0) return true;
            }

            contentTypeId = SceneContentMarkerResolver.Instance.GetContentTypeId(prefab);
            return contentTypeId >= 0;
        }
    }

    /// <summary>
    /// 内置兜底解析器：prefab（含子物体）上挂的标记组件自己的 <see cref="ISceneContentMarker.ContentTypeId"/>
    /// 就是它的内容类型 id。这样「把标记组件挂到 prefab 上」本身就是注册。
    /// </summary>
    public sealed class SceneContentMarkerResolver : ISceneContentPrefabResolver
    {
        /// <summary>进程内唯一实例（无状态，可安全共享）。</summary>
        public static readonly SceneContentMarkerResolver Instance = new SceneContentMarkerResolver();

        public int GetContentTypeId(GameObject prefab)
        {
            if (prefab == null) return -1;
            var components = prefab.GetComponentsInChildren<Component>(true);
            foreach (var c in components)
            {
                if (c == null) continue;
                if (c is ISceneContentMarker marker)
                    return marker.ContentTypeId;
            }
            return -1;
        }
    }
}
