// =============================================================================
//  HYCSimple · Scene Content 编辑器期解析器示例
//
//  ⚠️ 为什么这个文件必须在 Editor/ 子目录（而不是原来嵌在 Demo 里）：
//  `ISceneContentPrefabResolver` / `SceneContentResolvers` 定义在框架 Editor 程序集
//  （HYC.Framework.Editor）。Unity 的 asmdef 规则下，**运行时程序集不能引用 Editor
//  程序集**——这是编译期硬约束，不是风格选择。所以哪怕用 #if UNITY_EDITOR 包着，
//  写在运行时 asmdef（HYCSimple）里也照样 CS0234。编辑器期类型只能住 Editor asmdef。
//
//  ⚠️ 为什么用 [InitializeOnLoadMethod] 自注册（而不是让 Demo.Start 注册）：
//  注册是**编辑器期一次性动作**（导出 Blob 前需要，运行期 Load 不需要），
//  且框架内置兜底（SceneContentMarkerResolver：读 prefab 上标记组件自己的 id）
//  已能让挂了生成标记的 prefab 免注册导出——本解析器只是演示「自定义派生规则」。
//  放编辑器期自注册不影响 player 构建（Editor-only asmdef 不进包）。
// =============================================================================
using HYC.Framework.SceneContent.Editor;
using UnityEditor;
using UnityEngine;

namespace HYC.Simple.Scene.Editor
{
    /// <summary>
    /// 示例解析器：直接按 prefab 名派生稳定 id。真实项目应接配置表 SpawnDef 或常量映射表。
    /// </summary>
    internal static class DemoResolver
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            SceneContentResolvers.Register(new PrefabNameResolver());
            Debug.Log("[HYCSimple] DemoResolver registered (SceneContentResolvers).");
        }

        /// <summary>按 prefab 名派生稳定 id 的解析器（示例用，非唯一来源）。</summary>
        private sealed class PrefabNameResolver : ISceneContentPrefabResolver
        {
            public int GetContentTypeId(GameObject prefab)
                => prefab == null ? -1 : prefab.name.GetHashCode();
        }
    }
}
