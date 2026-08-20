using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>字段分组（右侧编辑器多页签）。</summary>
    public class GroupAttribute : PropertyAttribute
    {
        public string Name;
        public GroupAttribute(string name) => Name = name;
    }
}
