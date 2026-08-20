using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>bool 字段双按钮切换（True/False 标签）。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class BoolToggleButtonAttribute : PropertyAttribute
    {
        public string TrueLabel { get; }
        public string FalseLabel { get; }

        public BoolToggleButtonAttribute(string trueLabel, string falseLabel)
        {
            TrueLabel = trueLabel;
            FalseLabel = falseLabel;
        }
    }
}
