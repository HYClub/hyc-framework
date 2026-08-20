using System;
using UnityEngine;

namespace HYC.Framework.Config
{
    public enum InfoBoxLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>字段上方显示信息框，可按条件显示。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class InfoBoxAttribute : PropertyAttribute
    {
        public InfoBoxLevel Level;
        public string Message;
        public string Condition;

        public InfoBoxAttribute(string message)
        {
            Message = message;
            Level = InfoBoxLevel.Info;
            Condition = null;
        }

        public InfoBoxAttribute(string message, string condition)
        {
            Message = message;
            Level = InfoBoxLevel.Info;
            Condition = condition;
        }

        public InfoBoxAttribute(string message, InfoBoxLevel level)
        {
            Message = message;
            Level = level;
            Condition = null;
        }

        public InfoBoxAttribute(string message, InfoBoxLevel level, string condition)
        {
            Message = message;
            Level = level;
            Condition = condition;
        }
    }
}
