using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 全部生成标记组件的公共基类。
    /// <para>
    /// 生成器让每个内容类型继承本类；编辑器侧用
    /// <c>[CustomEditor(typeof(SceneContentMarkerBase), true)]</c> 一次性接管全部派生类的 Inspector，
    /// 不必逐类生成 Editor，也不像拿 <c>MonoBehaviour</c> 当 targetType 那样会接管全工程组件。
    /// </para>
    /// <para>接口实现与 <c>ToPayloadJson</c> 仍由生成类自己出，本类只做统一标记与公共便利。</para>
    /// </summary>
    public abstract class SceneContentMarkerBase : MonoBehaviour
    {
        /// <summary>
        /// 预览用关卡根：Inspector / 工具无显式关卡根上下文时取所在层级的最顶层。
        /// 真正导出时 levelRoot 为标记自身所在的关卡根 GO（Prefab 根或 Scene 内根 GO）。
        /// </summary>
        public GameObject PreviewLevelRoot => transform.root != null ? transform.root.gameObject : null;
    }
}
