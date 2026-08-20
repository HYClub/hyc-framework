using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>字段上方绘制分隔线。</summary>
    public class LineAttribute : PropertyAttribute
    {
        public uint Space = 0;
        public LineAttribute() { }
        public LineAttribute(uint space) => Space = space;
    }
}
