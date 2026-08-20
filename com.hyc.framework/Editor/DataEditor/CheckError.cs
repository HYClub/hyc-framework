using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>检查错误级别。</summary>
    public enum CheckErrorLevel
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// 配置检查结果条目。由生成的 XxxConfigCheck 收集，
    /// 由 <see cref="BuildErrorWindow"/> 展示（按资产分组 + 级别过滤）。
    /// </summary>
    public class CheckError
    {
        public Object Asset;
        public string Group;
        public CheckErrorLevel Level;
        public string Message;
        /// <summary>出错的字段名（用于数据编辑器定位高亮），可能为空。</summary>
        public string FieldName;

        public CheckError() { }

        public CheckError(Object asset, string group, CheckErrorLevel level, string message)
            : this(asset, group, level, message, null) { }

        public CheckError(Object asset, string group, CheckErrorLevel level, string message, string fieldName)
        {
            Asset = asset;
            Group = group;
            Level = level;
            Message = message;
            FieldName = fieldName;
        }
    }
}
