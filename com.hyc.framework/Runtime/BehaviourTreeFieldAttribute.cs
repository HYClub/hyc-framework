// ============================================================
// HYC Framework - 行为树字段特性
// 文件: Runtime/BehaviourTreeFieldAttribute.cs
// 说明: 标记 long 字段为行为树引用(存 BTTreeAsset.TreeId)。
//       生成配置类时由 BehaviourTree 字段类型自动附加;
//       数据编辑器识别此特性并绘制"行为树选择下拉"。
// ============================================================

using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>标记 long 字段为行为树引用。数据编辑器识别并绘制树选择器。</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class BehaviourTreeFieldAttribute : PropertyAttribute
    {
    }
}
