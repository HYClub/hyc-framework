using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>Vector2/Vector3 的 XYZ 分量独立启用/禁用。</summary>
    public class PositionAttribute : PropertyAttribute
    {
        public bool xEnable = true;
        public bool yEnable = true;
        public bool zEnable = true;
    }
}
