// ============================================================
// HYC Framework - 音频字段特性
// 文件: Runtime/Audio/AudioFieldAttribute.cs
// 说明: 标记 string 字段为「框架/音频」引用。
//       数据编辑器识别后绘制下拉框，列出音频总览里的条目。
// ============================================================

using System;
using UnityEngine;

namespace HYC.Framework.Audio
{
    /// <summary>
    /// 标记 string 字段为框架音频引用。生成配置类时由 <c>AudioEvent</c>
    /// 字段类型自动附加；数据编辑器绘制下拉选择。
    ///
    /// 字段存的是带前缀的字符串：<c>clip:&lt;Addressables地址&gt;</c> 或
    /// <c>wwise:&lt;事件名&gt;</c>，前缀决定路由到哪个后端。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AudioFieldAttribute : PropertyAttribute
    {
    }
}
