using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>条件可见性：根据其他字段/属性/方法的 bool 值决定是否显示。</summary>
    public class VisibleAttribute : PropertyAttribute
    {
        public enum LogicType
        {
            And,
            Or
        }

        public LogicType Logic;
        public string[] Methods;

        public VisibleAttribute(string method)
        {
            Logic = LogicType.And;
            Methods = new[] { method };
        }

        public VisibleAttribute(params string[] names)
        {
            Logic = LogicType.And;
            Methods = names;
        }

        public VisibleAttribute(LogicType logic, params string[] names)
        {
            Logic = logic;
            Methods = names;
        }
    }
}
