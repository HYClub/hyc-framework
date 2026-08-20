using HYC.Framework.Input;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Base class for tool-tip views opened via <see cref="ToolTipManager"/>.
    /// Positions itself beside the owning <see cref="AbsTipComponent"/> hot area
    /// (or the cursor) and lays out against one of many dock directions.
    /// Decoupled QK port: <c>InputManager.CurrentCursorPosition</c> is served by
    /// <see cref="HotkeyManager.CurrentCursorPosition"/>.
    /// </summary>
    public abstract partial class AbsTipView : BaseWindowSystem
    {
        protected AbsTipComponent mHotArea;

        private bool mViewUpdated = false;

        private double mOpenTime = 0.0f;
        private Vector2 mPrevDir = Vector2.zero;

        private static Vector2 mMouseOffset = new Vector2() { x = 68, y = 68 };

        public override bool Focusable => false;

        /// <summary>Rendered hot area.</summary>
        public virtual AbsTipComponent HotArea
        {
            get
            {
                return mHotArea;
            }
            set
            {
                mHotArea = value;
                mViewUpdated = false;
            }
        }

        /// <summary>On open.</summary>
        public override void OnViewOpen()
        {
            base.OnViewOpen();

            if (View.transform.childCount > 0)
            {
                var tip = View.transform.GetChild(0) as RectTransform;
                if (tip != null)
                    tip.anchoredPosition = Vector2.one * 100000;
            }

            InitView();

            UpdateView();

            View.gameObject.SetActive(true);

            mViewUpdated = true;
            mOpenTime = SystemAPI.Time.ElapsedTime;
        }

        /// <summary>Per-frame update.</summary>
        public override void OnViewUpdate()
        {
            if (View == null)
                return;

            if (!mViewUpdated)
            {
                UpdateView();

                mViewUpdated = true;
            }

            if (mHotArea == null || mHotArea.gameObject == null)
                ToolTipManager.OnHotAreaLost(this);
            else if (!mHotArea.gameObject.activeInHierarchy)
                ToolTipManager.OnExitToolTip(mHotArea);
            else if (View)
                UpdatePosition();
        }

        /// <summary>Initialize the view.</summary>
        protected abstract void InitView();

        /// <summary>Update the view.</summary>
        protected abstract void UpdateView();

        /// <summary>Update the position.</summary>
        protected virtual void UpdatePosition()
        {
            if (View.transform.childCount <= 0)
                return;

            var box = View.transform as RectTransform;
            if (box == null)
                return;

            var tip = View.transform.GetChild(0) as RectTransform;
            if (tip == null)
                return;

            if (mHotArea.transform == null)
                return;

            Rect hotAreaRect = GetHotAreaRect(box);
            if (mHotArea.TrackingType == TrackingType.HotArea)
            {
                Layout(box, hotAreaRect, tip);
            }
            else
            {
                LayoutMouse(box, hotAreaRect, tip);
            }
        }

        /// <summary>Get the hot area rect.</summary>
        private Rect GetHotAreaRect(RectTransform box)
        {
            // Track the hot area.
            if (mHotArea.TrackingType == TrackingType.HotArea)
            {
                var fourCornersArray = new Vector3[4];
                RectTransform hotArea = mHotArea.transform as RectTransform;
                hotArea.GetWorldCorners(fourCornersArray);

                fourCornersArray[1] = Camera.WorldToScreenPoint(fourCornersArray[1]);
                fourCornersArray[3] = Camera.WorldToScreenPoint(fourCornersArray[3]);

                RectTransformUtility.ScreenPointToLocalPointInRectangle(box, fourCornersArray[1], Camera, out var point1);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(box, fourCornersArray[3], Camera, out var point3);

                return new Rect(new Vector2(point1.x, -point1.y), new Vector2(Mathf.Abs(point3.x - point1.x), Mathf.Abs(point3.y - point1.y)));
            }
            else // Track the mouse.
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(box, HotkeyManager.CurrentCursorPosition, Camera, out var point1);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(box, HotkeyManager.CurrentCursorPosition + mMouseOffset, Camera, out var point3);
                return new Rect(new Vector2(point1.x, -point1.y), new Vector2(Mathf.Abs(point3.x - point1.x), Mathf.Abs(point3.y - point1.y)));
            }
        }

        private void LayoutMouse(RectTransform box, Rect hotAreaRect, RectTransform tip)
        {
            var boxRect = box.rect;
            var tipSize = tip.rect.size;
            var layoutPoint = Vector2.zero;
            var direction = Vector2.zero;

            if (LayoutForOR(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
            {
                LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
            }
            else if (LayoutForOL(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
            {
                LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
            }
        }

        private void Layout(RectTransform box, Rect hotAreaRect, RectTransform tip)
        {
            var boxRect = box.rect;
            var tipSize = tip.rect.size;

            if (mHotArea.Direction == DirectionType.None)
            {
                var layoutPoint = Vector2.zero;
                var direction = Vector2.zero;

                if (LayoutForRT(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForRC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForRB(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForLT(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForLC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForLB(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForTL(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForTC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForTR(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForBL(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForBC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else if (LayoutForBR(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction))
                {
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
                else
                {
                    LayoutForRT(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                    LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
                }
            }
            else
            {
                var layoutPoint = Vector2.zero;
                var direction = Vector2.zero;

                switch (mHotArea.Direction)
                {
                    case DirectionType.RightTop:
                        LayoutForRT(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.RightCenter:
                        LayoutForRC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.RightBottom:
                        LayoutForRB(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;

                    case DirectionType.LeftTop:
                        LayoutForLT(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.LeftCenter:
                        LayoutForLC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.LeftBottom:
                        LayoutForLB(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;

                    case DirectionType.TopLeft:
                        LayoutForTL(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.TopCenter:
                        LayoutForTC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.TopRight:
                        LayoutForTR(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;

                    case DirectionType.BottomLeft:
                        LayoutForBL(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.BottomCenter:
                        LayoutForBC(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                    case DirectionType.BottomRight:
                        LayoutForBR(boxRect, hotAreaRect, tipSize, out layoutPoint, out direction);
                        break;
                }

                LayoutTip(box, tip, boxRect, hotAreaRect, layoutPoint, direction);
            }
        }

        private void LayoutTip(RectTransform box, RectTransform tip, Rect boxRect, Rect hotAreaRect, Vector2 position, Vector2 direction)
        {
            var group = tip.gameObject.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = tip.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var time = EasingOut(direction);
            var positionEnd = position - new Vector2(boxRect.xMin, boxRect.yMin);
            var positionBegin = positionEnd + direction * 8;

            position = Vector2.Lerp(positionBegin, positionEnd, time);
            group.alpha = math.lerp(0.0f, 1.0f, time);

            tip.pivot = tip.anchorMin = tip.anchorMax = new Vector2(0, 1);
            tip.anchoredPosition = new Vector2(position.x, -position.y);
        }

        /// <summary>Easing function.</summary>
        private float EasingOut(Vector2 direction)
        {
            if (mPrevDir != direction)
            {
                mOpenTime = SystemAPI.Time.ElapsedTime;
                mPrevDir = direction;
            }
            var duation = 0.15f;

            var time = (float)(SystemAPI.Time.ElapsedTime - mOpenTime);

            if (time > duation)
                time = duation;

            time = time / duation;
            time = 1 - (1 - time) * (1 - time);

            return time;
        }

        private bool LayoutForLT(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin - tipSize.x;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(-1, 0);

            return xMin >= boxRect.xMin && yMin >= boxRect.yMin && yMax <= boxRect.yMax;
        }
        private bool LayoutForLC(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin - tipSize.x;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin - (tipSize.y - hotAreaRect.height) * 0.5f;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(-1, 0);

            return xMin >= boxRect.xMin && yMin >= boxRect.yMin && yMax <= boxRect.yMax;
        }
        private bool LayoutForLB(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin - tipSize.x;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMax - tipSize.y;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(-1, 0);

            return xMin >= boxRect.xMin && yMin >= boxRect.yMin && yMax <= boxRect.yMax;
        }

        private bool LayoutForRT(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMax;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(1, 0);

            return xMax <= boxRect.xMax && yMin >= boxRect.yMin && yMax <= boxRect.yMax;
        }
        private bool LayoutForRC(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMax;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin - (tipSize.y - hotAreaRect.height) * 0.5f;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(1, 0);

            return xMax <= boxRect.xMax && yMin >= boxRect.yMin && yMax <= boxRect.yMax;
        }
        private bool LayoutForRB(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMax;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMax - tipSize.y;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(1, 0);

            return xMax <= boxRect.xMax && yMin >= boxRect.yMin && yMax <= boxRect.yMax;
        }

        private bool LayoutForTL(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin - tipSize.y;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, -1);

            return yMin >= boxRect.yMin && xMin >= boxRect.xMin && xMax <= boxRect.xMax;
        }
        private bool LayoutForTC(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin - (tipSize.x - hotAreaRect.width) * 0.5f;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin - tipSize.y;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, -1);

            return yMin >= boxRect.yMin && xMin >= boxRect.xMin && xMax <= boxRect.xMax;
        }
        private bool LayoutForTR(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMax - tipSize.x;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin - tipSize.y;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, -1);

            return yMin >= boxRect.yMin && xMin >= boxRect.xMin && xMax <= boxRect.xMax;
        }

        private bool LayoutForBL(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMax;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, 1);

            return yMax <= boxRect.yMax && xMin >= boxRect.xMin && xMax <= boxRect.xMax;
        }
        private bool LayoutForBC(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin - (tipSize.x - hotAreaRect.width) * 0.5f;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMax;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, 1);

            return yMax <= boxRect.yMax && xMin >= boxRect.xMin && xMax <= boxRect.xMax;
        }
        private bool LayoutForBR(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMax - tipSize.x;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMax;
            var yMax = yMin + tipSize.y;

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, 1);

            return yMax <= boxRect.yMax && xMin >= boxRect.xMin && xMax <= boxRect.xMax;
        }

        private bool LayoutForOL(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMin - tipSize.x;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin;
            var yMax = yMin + tipSize.y;

            if (yMax > boxRect.yMax)
            {
                yMin -= yMax - boxRect.yMax;
            }
            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, 1);

            return xMin >= boxRect.xMin;
        }

        private bool LayoutForOR(Rect boxRect, Rect hotAreaRect, Vector2 tipSize, out Vector2 result, out Vector2 direction)
        {
            var xMin = hotAreaRect.xMax;
            var xMax = xMin + tipSize.x;
            var yMin = hotAreaRect.yMin;
            var yMax = yMin + tipSize.y;

            if (yMax > boxRect.yMax)
            {
                yMin -= yMax - boxRect.yMax;
            }

            result = new Vector2(xMin, yMin);
            direction = new Vector2(0, 1);

            return xMax <= boxRect.xMax;
        }
    }
}