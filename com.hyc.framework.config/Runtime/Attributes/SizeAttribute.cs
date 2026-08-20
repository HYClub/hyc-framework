using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>Vector 尺寸字段分量范围限制。</summary>
    public class SizeAttribute : PropertyAttribute
    {
        public float MinX = 0;
        public float MaxX = float.MaxValue;
        public float MinY = 0;
        public float MaxY = float.MaxValue;
        public float MinZ = 0;
        public float MaxZ = float.MaxValue;
    }
}
