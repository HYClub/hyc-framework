using System;
using UnityEngine;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Receives animation events ("Enter"/"Exit" and free-form keys) from a
    /// window view Animator so the window system can fire open/close callbacks
    /// from animation. Views may include an Animator calling these methods via
    /// Animation Events; the UIManager reacts to <see cref="OnAnimationExitEvent"/>
    /// to auto-close a window after its exit animation completes.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class UIAnimationHook : MonoBehaviour
    {
        public event Action OnAnimationEnterEvent;
        public event Action OnAnimationExitEvent;
        public event Action<string> OnAnimationEvent;

        public void FireAnimationEnterEvent() => OnAnimationEnterEvent?.Invoke();
        public void FireAnimationExitEvent() => OnAnimationExitEvent?.Invoke();
        public void FireAnimationEvent(string e) => OnAnimationEvent?.Invoke(e);
    }
}