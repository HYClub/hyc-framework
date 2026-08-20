using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HYC.Framework.Input
{
    /// <summary>
    /// Abstract UI element that renders a hotkey binding. Combines a uGUI
    /// Button with pointer hooks and the <see cref="IHotkeyElement"/> contract;
    /// concrete subclasses define the visual layout. Games instantiate the
    /// supplied prefab styles from StartupSetting or roll their own subclass.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public abstract class BaseHotkeyElement : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IHotkeyElement
    {
        protected Button m_Button;
        protected HotkeyManager.HotkeyState m_State;

        public virtual Sprite KeyIcon { get; set; }
        public virtual string DescText { get; set; }
        public virtual float Progress { get; set; }
        public virtual bool Visible { get; set; }
        public virtual bool Enabled { get; set; }
        public virtual bool Interactable { get; set; }
        public virtual bool HaveHightPriority { get; set; }

        public virtual Button.ButtonClickedEvent onClick => m_Button.onClick;

        protected virtual void Awake()
        {
            m_Button = GetComponent<Button>();
        }

        public virtual void PlayStartAnim() { }
        public virtual void PlayFinishAnim() { }
        public virtual void RefreshIcon() { }

        public abstract void OnPointerDown(PointerEventData eventData);
        public abstract void OnPointerUp(PointerEventData eventData);

        public abstract void Reset(HotkeyManager.HotkeyState hotkeyState);
        public abstract void Clear();

        protected T FindComponent<T>(string path) where T : Component
        {
            if (transform)
            {
                var t = transform.Find(path);
                if (t) return t.GetComponent<T>();
            }
            return null;
        }
    }
}
