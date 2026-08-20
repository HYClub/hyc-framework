using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>结构体列表的紧凑表格绘制（自定义列布局）。</summary>
    public class ListDrawerAttribute : PropertyAttribute
    {
        public string ChildPath;
        public bool ShowHeader = true;
        public float LineHeight = 20;
    }
}
