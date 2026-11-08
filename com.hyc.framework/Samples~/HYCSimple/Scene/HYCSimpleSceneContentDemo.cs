using HYC.Framework.SceneContent;
using UnityEngine;

namespace HYC.Simple
{
    /// <summary>
    /// 示例：演示运行期加载。真实项目把这段放到「关卡加载完成」的钩子里，
    /// 由关卡加载器把<b>业务 key</b>（配置 id / Addressables 地址 / 资产名）交给
    /// <c>SceneContentLoader.LoadByKey</c>。
    /// <para>
    /// 解析器注册不在这里：<c>ISceneContentPrefabResolver</c> 在框架 Editor 程序集，
    /// 运行时 asmdef 引用不了它（编译期硬约束，与 <c>#if UNITY_EDITOR</c> 无关）——
    /// 已改由 <c>Scene/Editor/DemoResolver.cs</c> 在编辑器期自注册。
    /// </para>
    /// <para>
    /// 运行期拿不到资产 GUID（AssetDatabase 只在编辑器可用，且重建 prefab 会换 GUID）。
    /// 导出器把「业务 key → 资产 GUID」写进 <c>SceneContent/index.json</c>，
    /// 关卡重建后重新导出即自愈；不要在代码 / Inspector 里手抄 GUID。
    /// </para>
    /// </summary>
    public class HYCSimpleSceneContentDemo : MonoBehaviour
    {
        [Tooltip("本关卡的业务 key：与导出时登记的 alias 一致（显式 alias > Addressables 地址 > 资产名）。真实项目由关卡加载器从配置行取，不是资产 GUID。")]
        public string levelKey;

        private void Start()
        {
            if (string.IsNullOrEmpty(levelKey))
            {
                Debug.LogError("[HYCSimple] levelKey 为空：请填入业务 key（如 Addressables 地址或资产名），并确认已运行 Export Blob 生成 SceneContent/index.json。");
                return;
            }
            SceneContentLoader.LoadByKey(levelKey, new HYCSimpleSceneContentSpawner());
        }
    }
}
