using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>
    /// 标记 string 字段为多语言 key（本地化 key）。
    /// 生成配置类时由 LocalizedKey 字段类型自动附加；数据编辑器
    /// 右侧面板识别此特性并绘制联想/选择/翻译 tooltip/即时校验。
    /// 本特性属于 config 包，不依赖 loc 包，始终可编译。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LocKeyAttribute : PropertyAttribute
    {
    }
}
