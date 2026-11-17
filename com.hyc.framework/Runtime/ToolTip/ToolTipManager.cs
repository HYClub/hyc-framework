using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Registry + controller that maps tool-tip data types to renderer views and
    /// drives the currently visible tool-tip from hot-area enter/exit events.
    /// </summary>
    public static class ToolTipManager
    {
        /// <summary>View type registry.</summary>
        private static readonly Dictionary<Type, Type> mViewTypeTable = new Dictionary<Type, Type>();
        /// <summary>Current hot area.</summary>
        private static AbsTipComponent mHotArea;
        /// <summary>Current view.</summary>
        private static AbsTipView mTipView;
        /// <summary>Current view type.</summary>
        private static Type mTipViewType;

        /// <summary>Register a renderer service for a data type.</summary>
        public static void RegisterService(Type toolTipType, Type rendererType)
        {
            if (!mViewTypeTable.ContainsKey(toolTipType))
                mViewTypeTable.Add(toolTipType, rendererType);
        }

        /// <summary>On entering a hot area.</summary>
        public static void OnEnterToolTip(AbsTipComponent hotArea)
        {
            // 先算出目标渲染器类型，再决定「复用 or 换」——不要在方法开头无条件清空 mTipViewType，
            // 否则下面的类型比较恒为真，同一个 Tip 会被 Close 两次。
            // GetData 是 abstract，只调一次取结果（子类实现不保证幂等）。
            var data = hotArea == null ? null : hotArea.GetData();
            var viewType = data == null ? null : QueryRenderer(data.GetType());
            if (viewType == null)
            {
                if (data != null)
                    Debug.LogError($"未找到数据对应的Tip渲染器! ({data.GetType()})");
                // 无有效渲染器：收掉可能存在的旧 Tip（成对清 mTipView / mTipViewType）。
                CloseCurrentTip();
                return;
            }

            if (viewType == mTipViewType && mTipView != null)
            {
                // 同类型直接复用，只换数据。
                mHotArea = hotArea;
                mTipView.HotArea = hotArea;
                return;
            }

            // 类型不同：关旧的（成对清状态），再开新的。
            CloseCurrentTip();
            mHotArea = hotArea;
            mTipViewType = viewType;
            var tip = UIManager.OpenToolTip(viewType) as AbsTipView;
            if (tip == null)
            {
                // 渲染器已登记但窗口打不开（层/资源缺失）：不能让 mTipViewType 悬空指向一个不存在的 Tip，
                // 否则下次同类型进入会误走「复用」分支并 NRE。
                mTipViewType = null;
                mHotArea = null;
                Debug.LogError($"[ToolTip] 无法打开 Tip 视图: {viewType.Name}");
                return;
            }
            mTipView = tip;
            mTipView.HotArea = hotArea;
        }

        private static void CloseCurrentTip()
        {
            if (mTipView != null)
            {
                UIManager.Close(mTipView);
                mTipView = null;
            }
            mTipViewType = null;
        }

        /// <summary>On leaving a hot area.</summary>
        public static void OnExitToolTip(AbsTipComponent hotArea)
        {
            if (hotArea == mHotArea)
                OnExitToolTip(hotArea, false);
        }

        /// <summary>When a hot area is lost.</summary>
        public static void OnHotAreaLost(AbsTipView tip)
        {
            if (mHotArea == null && mTipView == tip)
            {
                UIManager.Close(mTipView);
                mTipViewType = null;
            }
        }

        private static async void OnExitToolTip(AbsTipComponent hotArea, bool delay)
        {
            if (delay)
                await Task.Delay(100);

            if (hotArea == mHotArea)
            {
                if (mTipView != null)
                {
                    UIManager.Close(mTipView);
                }

                mTipViewType = null;
            }
        }

        /// <summary>Query the renderer type for a data type (walks base types).</summary>
        private static Type QueryRenderer(Type dataType)
        {
            while (dataType != null)
            {
                if (mViewTypeTable.TryGetValue(dataType, out Type rendererType))
                {
                    return rendererType;
                }
                dataType = dataType.BaseType;
            }

            return null;
        }
    }
}