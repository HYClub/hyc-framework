using Unity.Collections;
using Unity.Entities;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 自动初始化系统（优化方向 C）：扫描带 <see cref="AttributeSetRef"/> 但还没有
    /// <see cref="AttributeValue"/> 属性袋的实体，把预设的 values 复制进去。
    /// 挂到手动驱动的 World 里即可（框架目前是编辑器中手动驱动 World）。
    /// 加 <c>[UpdateInGroup(...)]</c> 把它放进合适的更新阶梯（默认组也行，由 World 装配决定）。
    /// </summary>
    public partial class AttributeInitSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var query = EntityManager.CreateEntityQuery(
                typeof(AttributeSetRef),
                ComponentType.Exclude<AttributeValue>());
            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var setRef = EntityManager.GetComponentObject<AttributeSetRef>(e);
                if (setRef == null || setRef.asset == null) continue;
                var buf = EntityManager.AddBuffer<AttributeValue>(e);
                var vals = setRef.asset.values;
                for (int j = 0; j < vals.Count; j++) buf.Add(vals[j]);
            }
            entities.Dispose();
        }
    }
}
