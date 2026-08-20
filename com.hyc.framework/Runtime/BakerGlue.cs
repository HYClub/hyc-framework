using System.Collections.Generic;
using Unity.Entities;

namespace HYC.Framework.Dots
{
    /// <summary>
    /// Accompanying helpers for authors (<c>Baker&lt;TAuthoring&gt;</c>).
    /// These reduce repetitive "AddComponent + AddBuffer + AddItem" boilerplate
    /// that every authoring→entity pair otherwise duplicates.
    /// </summary>
    public static class BakerGlue
    {
        /// <summary>Adds and populates an <see cref="IBufferElementData"/> buffer from an <see cref="IEnumerable{TBuffer}"/>.</summary>
        public static void AddItems<TBuffer>(IBaker baker, Entity entity, IEnumerable<TBuffer> items)
            where TBuffer : unmanaged, IBufferElementData
        {
            var buffer = baker.AddBuffer<TBuffer>(entity);
            if (items == null) return;
            foreach (var item in items)
            {
                buffer.Add(item);
            }
        }

        /// <summary>Adds a <see cref="IComponentData"/> only when a value is present.</summary>
        public static void AddIfPresent<TComponent>(IBaker baker, Entity entity, in TComponent value, bool present)
            where TComponent : unmanaged, IComponentData
        {
            if (!present) return;
            baker.AddComponent<TComponent>(entity);
            baker.SetComponent(entity, value);
        }
    }
}