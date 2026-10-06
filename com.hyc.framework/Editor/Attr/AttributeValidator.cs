using System.Collections.Generic;
using HYC.Framework.Attr;
using UnityEditor;

namespace HYC.Framework.Attr.Editor
{
    /// <summary>一条校验问题。</summary>
    public class AttributeIssue
    {
        public bool IsError;
        public string Message;
        public AttributeIssue(bool isError, string msg) { IsError = isError; Message = msg; }
    }

    /// <summary>
    /// 属性目录 / 预设的静态校验（优化方向 D 的「作者时拦错」）。
    /// 生成代码前会卡错误；编辑器里也有「校验目录」按钮可手动触发。
    /// </summary>
    public static class AttributeValidator
    {
        public static List<AttributeIssue> Validate(AttributeCatalog cat)
        {
            var issues = new List<AttributeIssue>();
            if (cat == null) { issues.Add(new AttributeIssue(true, "属性目录为空。")); return issues; }

            var seen = new HashSet<string>();
            for (int i = 0; i < cat.entries.Count; i++)
            {
                var e = cat.entries[i];
                var tag = "[" + i + "] ";
                if (string.IsNullOrEmpty(e.key)) { issues.Add(new AttributeIssue(true, tag + "key 为空。")); continue; }
                if (!IsValidIdentifier(e.key))
                    issues.Add(new AttributeIssue(false, tag + "key \"" + e.key + "\" 含非法标识符字符，生成 enum 时会被清洗。"));
                if (!seen.Add(e.key)) issues.Add(new AttributeIssue(true, tag + "key \"" + e.key + "\" 重复。"));

                if (e.type != AttributeType.Float)
                {
                    if (e.minRaw > e.maxRaw)
                        issues.Add(new AttributeIssue(true, tag + "key \"" + e.key + "\": min > max。"));
                    if (e.defaultRaw < e.minRaw || e.defaultRaw > e.maxRaw)
                        issues.Add(new AttributeIssue(true, tag + "key \"" + e.key + "\": default 不在 [min, max] 内。"));
                }
                else
                {
                    if (!IsFiniteBits(e.defaultRaw) || !IsFiniteBits(e.minRaw) || !IsFiniteBits(e.maxRaw))
                        issues.Add(new AttributeIssue(true, tag + "key \"" + e.key + "\": Float 的 raw 不是合法 IEEE754 double bits。"));
                    else
                    {
                        double d = System.BitConverter.Int64BitsToDouble(e.defaultRaw);
                        double mn = System.BitConverter.Int64BitsToDouble(e.minRaw);
                        double mx = System.BitConverter.Int64BitsToDouble(e.maxRaw);
                        if (mn > mx) issues.Add(new AttributeIssue(true, tag + "key \"" + e.key + "\": min > max。"));
                        if (d < mn || d > mx)
                            issues.Add(new AttributeIssue(false, tag + "key \"" + e.key + "\": default 不在 [min, max] 内（生成时会钳制）。"));
                    }
                }
            }
            return issues;
        }

        private static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!char.IsLetter(s[0]) && s[0] != '_') return false;
            for (int i = 1; i < s.Length; i++)
                if (!char.IsLetterOrDigit(s[i]) && s[i] != '_') return false;
            return true;
        }

        private static bool IsFiniteBits(long bits)
        {
            double d = System.BitConverter.Int64BitsToDouble(bits);
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }
    }
}
