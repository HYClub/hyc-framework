using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>
    /// 条件可见性（模板字段「关联参数」生成）：仅当 <see cref="FieldName"/> 指向的字段
    /// 满足条件时，本字段才在数据编辑器的配置面板上绘制。
    /// <para>
    /// 可叠加多个特性（AND，全部满足才显示）；目标字段缺失或类型不符时<b>视为满足</b>
    /// （保持显示）——配置面板不能因为目标字段被改名/删除就把字段吞掉。
    /// 仅影响数据编辑器面板展示，不影响反射赋值与导出。
    /// 由 <see cref="ConfigTemplateField.visibility"/> 在代码生成时自动附加，无需手写。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class CfgVisibleWhenAttribute : PropertyAttribute
    {
        /// <summary>目标字段名（同一配置类内的 bool/enum 标量字段）。</summary>
        public string FieldName { get; }

        /// <summary>目标字段为 bool 时要求的值（enum 构造时为 true，无意义）。</summary>
        public bool BoolValue { get; }

        /// <summary>目标字段为 enum 时允许的值名列表（命中任一即满足；空列表 = 不满足）。</summary>
        public string[] EnumValues { get; }

        /// <summary>bool 目标字段条件。</summary>
        public CfgVisibleWhenAttribute(string fieldName, bool boolValue)
        {
            FieldName = fieldName;
            BoolValue = boolValue;
            EnumValues = Array.Empty<string>();
        }

        /// <summary>enum 目标字段条件：当前值命中 <paramref name="enumValues"/> 任一即满足。</summary>
        public CfgVisibleWhenAttribute(string fieldName, params string[] enumValues)
        {
            FieldName = fieldName;
            BoolValue = true;
            EnumValues = enumValues ?? Array.Empty<string>();
        }

        /// <summary>目标字段为 enum 时求值（与 <see cref="ConfigVisibilityCondition.MatchesEnum"/> 同语义）。</summary>
        public bool MatchesEnum(string actualValueName)
        {
            if (EnumValues == null || EnumValues.Length == 0)
                return false;
            for (var i = 0; i < EnumValues.Length; i++)
            {
                if (string.Equals(EnumValues[i], actualValueName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
