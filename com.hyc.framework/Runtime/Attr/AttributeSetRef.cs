using Unity.Entities;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 实体的「属性预设引用」组件（托管组件）。给实体挂上它，
    /// <see cref="AttributeInitSystem"/> 会在缺属性袋时自动把对应预设的 values 灌进去。
    /// 用 class 实现 IComponentData = 托管组件（可持有 UnityEngine.Object 引用）。
    /// 在手动驱动的 World 里创建实体后 AddComponentObject(entity, new AttributeSetRef { asset = mySet }) 即可。
    /// </summary>
    public class AttributeSetRef : IComponentData
    {
        public AttributeSetAsset asset;
    }
}
