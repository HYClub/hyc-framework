using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>字段末尾追加空白。</summary>
    public class SpaceAtEndAttribute : PropertyAttribute
    {
        public int Space;
        public SpaceAtEndAttribute(int space) => Space = space;
    }
}
