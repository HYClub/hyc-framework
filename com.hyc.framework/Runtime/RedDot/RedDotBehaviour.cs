using TMPro;
using UnityEngine;

namespace HYC.Framework.RedDot
{
    /// <summary>
    /// 预制体上的红点绑定器（L1 零代码消费）：挂在红点/数字所在的节点上，
    /// 由所属 <see cref="UI.AbsUISystem"/> 在视图打开时自动收集并绑定，关闭时自动解绑。
    ///
    /// <para><b>零代码用法</b>：在预制体里建一个「红点」子物体（Image 圆点或带数字的底板），
    /// 挂上本组件，填 <see cref="Key"/> 与（可选的）<see cref="CountLabel"/> 即可。
    /// 视图打开 → 自动订阅；聚合值变化 → 自动显隐；视图关闭 → 自动退订。</para>
    ///
    /// <para><b>动态列表项</b>：列表项是运行时实例化的，无法靠「打开视图时收集」，
    /// 用 <c>AbsUISystem.BindRedDot(key, behaviour)</c> 显式绑定（L2）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RedDotBehaviour : MonoBehaviour
    {
        [Tooltip("红点节点键。0 = 未配置，会被忽略。")]
        [SerializeField] private RedDotKey key;

        [Tooltip("是否显示聚合计数（需同时指定 CountLabel）。关闭则只做显隐。")]
        [SerializeField] private bool showCount;

        [Tooltip("红点图形的根节点。留空 = 本组件所在物体（此时本物体应只承载红点，不要放其它内容）。")]
        [SerializeField] private GameObject badge;

        [Tooltip("计数文本（TMP）。showCount = false 时忽略。")]
        [SerializeField] private TMP_Text countLabel;

        /// <summary>绑定的红点键。</summary>
        public RedDotKey Key => key;

        /// <summary>是否显示计数。</summary>
        public bool ShowCount => showCount;

        private bool _applied;
        private bool _lastActive;
        private int _lastCount;

        /// <summary>新挂上组件时把 badge 默认指向自身，省一次手动拖拽。</summary>
        private void Reset() => badge = gameObject;

        /// <summary>按聚合结果刷新显示。由 <see cref="UI.AbsUISystem"/> 调用，业务侧不需要手动调。</summary>
        internal void Apply(bool active, int count)
        {
            // 值未变不写 SetActive / text：避免每帧无谓的 UI 重建
            if (_applied && _lastActive == active && _lastCount == count)
                return;

            _applied = true;
            _lastActive = active;
            _lastCount = count;

            var target = badge != null ? badge : gameObject;
            target.SetActive(active);

            if (!showCount || countLabel == null)
                return;

            countLabel.text = count > 0 ? count.ToString() : string.Empty;
        }

        /// <summary>解绑时恢复隐藏，避免下次打开残留上一次的亮态。</summary>
        internal void ResetApplied()
        {
            _applied = false;
            var target = badge != null ? badge : gameObject;
            target.SetActive(false);
        }
    }
}
