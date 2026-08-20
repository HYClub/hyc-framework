using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>字段最大值限制。</summary>
    public class MaxAttribute : PropertyAttribute
    {
        public readonly float max;
        public MaxAttribute(float max) => this.max = max;
    }
}
