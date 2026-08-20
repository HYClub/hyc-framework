using System;

namespace HYC.Framework.Config
{
    /// <summary>
    /// Marks a ScriptableObject subclass as a config asset visible in the
    /// QK data editor. <see cref="Name"/> supports "Category/Name" so the
    /// create window can group types; <see cref="Order"/> sorts entries and
    /// <see cref="Unique"/> restricts the project to a single instance.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CfgAssetAttribute : Attribute
    {
        public string Name;
        public int Order;
        public bool Unique;
        public Type ParentAsset;

        public CfgAssetAttribute(string name, int order, bool unique = false)
        {
            Name = name;
            Order = order;
            Unique = unique;
        }

        public CfgAssetAttribute(string name, int order, Type parentAsset)
        {
            Name = name;
            Order = order;
            ParentAsset = parentAsset;
        }
    }
}
