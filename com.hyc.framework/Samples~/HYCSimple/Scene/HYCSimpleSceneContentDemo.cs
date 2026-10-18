using HYC.Framework.SceneContent;
using UnityEngine;

namespace HYC.Simple
{
    /// <summary>
    /// 示例：演示运行期加载。真实项目把这段放到「关卡加载完成」的钩子里。
    /// 解析器注册是<b>编辑器期</b>动作（导出 Blob 前需要），故用 #if UNITY_EDITOR 包裹，
    /// 避免运行期（player 构建）引用 Editor 程序集。
    /// </summary>
    public class HYCSimpleSceneContentDemo : MonoBehaviour
    {
        [Tooltip("关卡 id，须与 SceneContentAuthoring.levelId 及导出文件名一致。")]
        public int levelId = 1;

        private void Start()
        {
#if UNITY_EDITOR
            HYC.Framework.SceneContent.Editor.SceneContentResolvers.Register(new DemoResolver());
#endif
            SceneContentLoader.Load(levelId, new HYCSimpleSceneContentSpawner());
        }

#if UNITY_EDITOR
        // 示例解析器：直接按 prefab 名派生稳定 id。真实项目应接配置表 SpawnDef 或常量映射表。
        private class DemoResolver : HYC.Framework.SceneContent.Editor.ISceneContentPrefabResolver
        {
            public int GetContentTypeId(GameObject prefab)
                => prefab == null ? -1 : prefab.name.GetHashCode();
        }
#endif
    }
}
