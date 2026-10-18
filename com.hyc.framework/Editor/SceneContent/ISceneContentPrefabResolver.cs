using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 把实例条目的 prefab 引用解析成稳定 int id（写入 Blob）。
    /// 游戏在编辑器初始化时注册一个实现（例如内部查配置表 SpawnDef，或手动字典映射）。
    /// 未注册时导出会报错——框架不知道某个 prefab 对应哪个 <c>contentTypeId</c>。
    /// </summary>
    public interface ISceneContentPrefabResolver
    {
        /// <summary>返回 prefab 对应的内容 id；无法识别时返回负数。</summary>
        int GetContentTypeId(GameObject prefab);
    }

    /// <summary>解析器注册表（编辑器内全局唯一，游戏一次性注册）。</summary>
    public static class SceneContentResolvers
    {
        private static ISceneContentPrefabResolver _resolver;

        /// <summary>注册（或替换）当前解析器。</summary>
        public static void Register(ISceneContentPrefabResolver resolver) => _resolver = resolver;

        /// <summary>尝试把 prefab 解析成 id；未注册或无法识别返回 false。</summary>
        public static bool TryResolve(GameObject prefab, out int contentTypeId)
        {
            contentTypeId = -1;
            if (_resolver == null) return false;
            contentTypeId = _resolver.GetContentTypeId(prefab);
            return contentTypeId >= 0;
        }
    }
}
