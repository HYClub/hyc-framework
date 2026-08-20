using UnityEngine;
using UnityEngine.EventSystems;

namespace HYC.Framework.UI
{
    /// <summary>
    /// A "hot area" element that shows a tool-tip while the pointer is over it.
    /// Subclasses override <see cref="GetData"/> to return the data object whose
    /// registered renderer (<see cref="ToolTipManager.RegisterService"/>) draws
    /// the tip. Decoupled QK port strips the game-only <c>[Visible]</c> attribute;
    /// the <see cref="Direction"/> field is only meaningful when
    /// <see cref="TrackingType"/> is <see cref="TrackingType.HotArea"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public abstract class AbsTipComponent : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [InspectorName("追踪类型")]
        public TrackingType TrackingType;

        [InspectorName("停靠位置")]
        public DirectionType Direction;

        public bool DirectionVisible => TrackingType == TrackingType.HotArea;

        public abstract object GetData();

        public void OnPointerEnter(PointerEventData eventData)
        {
            ToolTipManager.OnEnterToolTip(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ToolTipManager.OnExitToolTip(this);
        }
    }

    /// <summary>Tracking type.</summary>
    public enum TrackingType
    {
        [InspectorName("热区")]
        HotArea,
        [InspectorName("鼠标")]
        Mouse,
    }

    /// <summary>Dock alignment.</summary>
    public enum DirectionType
    {
        [InspectorName("自动")]
        None,

        [InspectorName("上边左对齐")]
        TopLeft,
        [InspectorName("上边居中对齐")]
        TopCenter,
        [InspectorName("上边右对齐")]
        TopRight,

        [InspectorName("左边上对齐")]
        LeftTop,
        [InspectorName("左边居中对齐")]
        LeftCenter,
        [InspectorName("左边下对齐")]
        LeftBottom,

        [InspectorName("右边上对齐")]
        RightTop,
        [InspectorName("右边居中对齐")]
        RightCenter,
        [InspectorName("右边下对齐")]
        RightBottom,

        [InspectorName("下边左对齐")]
        BottomLeft,
        [InspectorName("下边居中对齐")]
        BottomCenter,
        [InspectorName("下边右对齐")]
        BottomRight,
    }
}