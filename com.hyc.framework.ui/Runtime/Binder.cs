using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Runtime binder engine. Given a window root and a set of
    /// "field → node path" pairs (produced by the UIBinder code generator),
    /// it resolves each path via <see cref="Transform.Find"/> and assigns the
    /// typed reference. Convention used by generated binders:
    /// keys are the C# field names, node names use underscores.
    /// </summary>
    public static class BinderEngine
    {
        /// <summary>Resolves a component of type T on a node resolved by path.</summary>
        public static T Get<T>(GameObject root, string path) where T : Object
        {
            if (root == null) return null;
            var t = path == null || path.Length == 0 ? root.transform : root.transform.Find(path);
            if (t == null) return null;
            return t.GetComponent<T>();
        }

        public static T Require<T>(GameObject root, string path) where T : Object
        {
            var v = Get<T>(root, path);
            return v;
        }

        /// <summary>Convention-style find using underscores→path segments (bind() naming).</summary>
        public static T FindBySeralized<T>(GameObject root, string firstName)
        where T : Object
        {
            if (root == null) return null;
            var path = firstName.Replace('_', '/');
            return Get<T>(root, path);
        }

        /// <summary>
        /// Instantiates a generated binder (implements <see cref="IComponentBinder"/>)
        /// and binds it to <paramref name="root"/> via <c>Reset</c>. Falls back to a
        /// plain instance when the type doesn't implement the protocol.
        /// </summary>
        public static T Bind<T>(GameObject root) where T : class, new()
        {
            var binder = new T();
            if (binder is IComponentBinder componentBinder) componentBinder.Reset(root);
            return binder;
        }
    }
}