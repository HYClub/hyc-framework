// ============================================================
// HYC Framework - Addressable 字段特性
// 文件: Runtime/AddressableFieldAttribute.cs
// 说明: 标记 string 字段为 Addressable 资源地址。
//       数据编辑器识别后绘制"拖资源自动填地址"框。
// ============================================================

using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>标记 string 字段为 Addressable 资源地址。数据编辑器识别并绘制拖放框。</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AddressableFieldAttribute : PropertyAttribute
    {
    }
}
