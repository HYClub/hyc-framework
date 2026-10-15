using System;

namespace HYC.Framework.Config
{
    /// <summary>
    /// Marks a ScriptableObject subclass as a config asset visible in the
    /// HYC data editor. <see cref="Name"/> supports "Category/Name" so the
    /// create window can group types; <see cref="Order"/> sorts entries,
    /// <see cref="Unique"/> restricts the project to a single instance and
    /// <see cref="Hide"/> marks framework infrastructure types that the
    /// create window must not offer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CfgAssetAttribute : Attribute
    {
        public string Name;
        public int Order;
        public bool Unique;
        public Type ParentAsset;

        /// <summary>
        /// true = 框架基础设施类型（模板 / 生成配置的基类 / 音频总览等）：
        /// 不出现在「配置创建器」里——它们有专用创建入口（数据树右键 /
        /// 代码生成 / Audio Manager），用户不在此处创建。
        /// ⚠️ 只按类型<b>自身直接标注</b>的特性判定，继承链上的不算——
        /// 否则基类标了 Hide 会把所有派生配置一起藏掉。
        /// </summary>
        public bool Hide;

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

        /// <summary>
        /// 类型自身是否直接标注了 <see cref="Hide"/>。只认自身直接标注的特性，
        /// 继承链上的不算——否则基类标了 Hide 会把所有派生配置一起藏掉
        /// （特性 <see cref="AttributeUsageAttribute.Inherited"/> 为 true）。
        /// 「配置创建器」/「配置引用」选择器等 UI 用它过滤框架基础设施类型。
        /// </summary>
        public static bool IsHidden(Type type)
        {
            foreach (CfgAssetAttribute attr in type.GetCustomAttributes(typeof(CfgAssetAttribute), false))
            {
                if (attr.Hide)
                    return true;
            }
            return false;
        }
    }
}
