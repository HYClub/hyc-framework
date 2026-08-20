using UnityEngine;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Protocol implemented by every generated binder. Calling
    /// <see cref="Reset"/> (re)links the binder to a live GameObject — driven by
    /// <see cref="UIManager"/> / generated window systems whenever a window view
    /// is (re)shown, so binders never hold stale references across scene loads.
    /// </summary>
    public interface IComponentBinder
    {
        /// <summary>Binds (or re-binds) this binder to <paramref name="gameObject"/>.</summary>
        void Reset(GameObject gameObject);
    }
}