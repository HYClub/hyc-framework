using System;
using UnityEngine;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Authoring "binder table". Drop on a UI prefab root (or any node), add one
    /// <see cref="Item"/> per field you want to expose. The generator turns this
    /// into a strongly-typed binder class implementing <see cref="IComponentBinder"/>.
    /// This MonoBehaviour itself is harmless at runtime (data-only).
    /// </summary>
    [AddComponentMenu("HYC Framework/UI/UI 元素绑定")]
    public sealed class ComponentBinderTable : MonoBehaviour
    {
        /// <summary>Namespace for the generated binder. Ignored if <see cref="CustomPackageName"/>.</summary>
        public string PackageName;

        /// <summary>Whether to override the project-wide output namespace.</summary>
        public bool CustomPackageName;

        /// <summary>Class name for the generated binder. Ignored if <see cref="CustomClassName"/>.</summary>
        public string ClassName;

        /// <summary>Whether to override the (default GameObject-name) class name.</summary>
        public bool CustomClassName;

        /// <summary>Binds to expose in the generated class.</summary>
        public Item[] Items;

        [Serializable]
        public class Item
        {
            /// <summary>Field name in the generated binder.</summary>
            public string Name;

            /// <summary>Target GameObject that owns <see cref="Component"/>.</summary>
            public GameObject Target;

            /// <summary>The component to expose (must sit on <see cref="Target"/>).</summary>
            public Component Component;

            /// <summary>Optional summary comment.</summary>
            public string Desc;
        }

        /// <summary>Returns the bound component at <paramref name="index"/> (used by generated code).</summary>
        public Component GetComponentAt(int index)
        {
            return Items != null && index >= 0 && index < Items.Length && Items[index] != null
                ? Items[index].Component : null;
        }
    }
}