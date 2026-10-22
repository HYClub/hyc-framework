using HYC.Framework.SceneContent;
using UnityEngine;

namespace HYC.Simple
{
    /// <summary>
    /// 示例：演示运行期加载。真实项目把这段放到「关卡加载完成」的钩子里。
    /// <para>
    /// 解析器注册不在这里：<c>ISceneContentPrefabResolver</c> 在框架 Editor 程序集，
    /// 运行时 asmdef 引用不了它（编译期硬约束，与 <c>#if UNITY_EDITOR</c> 无关）——
    /// 已改由 <c>Scene/Editor/DemoResolver.cs</c> 在编辑器期自注册。
    /// </para>
    /// </summary>
    public class HYCSimpleSceneContentDemo : MonoBehaviour
    {
        [Tooltip("关卡 id，须与 SceneContentAuthoring.levelId 及导出文件名一致。")]
        public int levelId = 1;

        private void Start()
        {
            SceneContentLoader.Load(levelId, new HYCSimpleSceneContentSpawner());
        }
    }
}
