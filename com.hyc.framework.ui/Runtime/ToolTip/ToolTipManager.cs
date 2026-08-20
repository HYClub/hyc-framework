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
            if (mTipView != null)
                UIManager.Close(mTipView);
            mTipViewType = null;

            if (hotArea != null)
            {
                var data = hotArea.GetData();
                if (data != null)
                {
                    var dataType = data.GetType();
                    var viewType = QueryRenderer(dataType);
                    if (viewType != null)
                    {
                        if (viewType != mTipViewType)
                        {
                            if (mTipView != null)
                                UIManager.Close(mTipView);

                            mHotArea = hotArea;

                            mTipViewType = viewType;
                            mTipView = UIManager.OpenToolTip(viewType) as AbsTipView;

                            mTipView.HotArea = hotArea;
                        }
                        else
                        {
                            mTipView.HotArea = hotArea;
                        }

                        return;
                    }
                    else
                    {
                        Debug.LogError($"未找到数据对应的Tip渲染器! ({dataType})");
                    }
                }
            }
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