using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>时间戳字段：编辑器中显示为日期/时间选择器（显示层处理）。</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class TimeStampAttribute : PropertyAttribute
    {
        public bool ShowDate { get; private set; } = true;
        public bool ShowTime { get; private set; } = true;

        public TimeStampAttribute(bool showDate = true, bool showTime = true)
        {
            ShowDate = showDate;
            ShowTime = showTime;
        }
    }
}
