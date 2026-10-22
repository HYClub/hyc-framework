using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 极简 JSON 写入器：给<b>生成的标记组件</b>和手写 marker 用来拼 payload。
    /// <para>
    /// 为什么不用 <c>JsonUtility.ToJson</c>：它无法按字段做「这一侧导不导出 / GO 引用写成路径还是名字」
    /// 这类投影，也无法把 <c>GameObject</c> 转成稳定引用（见 <see cref="SceneContentRefPath"/>）。
    /// 生成代码用本类逐字段写，规则完全可控。
    /// </para>
    /// <para>
    /// 逗号/层级由内部栈自动维护，调用方只按顺序写；数值一律 <see cref="CultureInfo.InvariantCulture"/>
    /// （避免某些区域设置把小数点写成逗号而产出非法 JSON）。不负责缩进——blob 里存紧凑 JSON。
    /// </para>
    /// </summary>
    public sealed class SceneContentJsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(128);
        private readonly List<bool> _hasItem = new List<bool>();
        private bool _afterKey;

        /// <summary>开始一个对象 <c>{</c>。</summary>
        public void BeginObject()
        {
            PreValue();
            _sb.Append('{');
            PostValue();
            _hasItem.Add(false);
            _afterKey = false;
        }

        /// <summary>结束当前对象 <c>}</c>。</summary>
        public void EndObject()
        {
            PopLevel();
            _sb.Append('}');
            _afterKey = false;
        }

        /// <summary>开始一个数组 <c>[</c>。</summary>
        public void BeginArray()
        {
            PreValue();
            _sb.Append('[');
            PostValue();
            _hasItem.Add(false);
            _afterKey = false;
        }

        /// <summary>结束当前数组 <c>]</c>。</summary>
        public void EndArray()
        {
            PopLevel();
            _sb.Append(']');
            _afterKey = false;
        }

        /// <summary>写一个 key（其后必须紧跟一个值或容器）。</summary>
        public void Key(string name)
        {
            PreValue();
            _sb.Append('"').Append(Escape(name)).Append("\":");
            PostValue();
            _afterKey = true;
        }

        /// <summary>写字符串值；null 写成 JSON <c>null</c>。</summary>
        public void Value(string value)
        {
            PreValue();
            if (value == null)
                _sb.Append("null");
            else
                _sb.Append('"').Append(Escape(value)).Append('"');
            PostValue();
        }

        /// <summary>写整型值。</summary>
        public void Value(int value)
        {
            PreValue();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            PostValue();
        }

        /// <summary>写布尔值。</summary>
        public void Value(bool value)
        {
            PreValue();
            _sb.Append(value ? "true" : "false");
            PostValue();
        }

        /// <summary>写浮点值（round-trip 精度，不受区域设置影响）。</summary>
        public void Value(float value)
        {
            PreValue();
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            PostValue();
        }

        /// <summary>取紧凑 JSON 文本。</summary>
        public override string ToString() => _sb.ToString();

        // ---------- 内部 ----------

        private void PreValue()
        {
            // 紧跟 key 的值不加逗号（逗号已经由 key 那次 PostValue 记到层级状态里）
            if (_afterKey)
                return;
            if (_hasItem.Count > 0 && _hasItem[_hasItem.Count - 1])
                _sb.Append(',');
        }

        private void PostValue()
        {
            if (_hasItem.Count > 0)
                _hasItem[_hasItem.Count - 1] = true;
            _afterKey = false;
        }

        private void PopLevel()
        {
            if (_hasItem.Count > 0)
                _hasItem.RemoveAt(_hasItem.Count - 1);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            StringBuilder sb = null;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                string rep = null;
                switch (c)
                {
                    case '"': rep = "\\\""; break;
                    case '\\': rep = "\\\\"; break;
                    case '\n': rep = "\\n"; break;
                    case '\r': rep = "\\r"; break;
                    case '\t': rep = "\\t"; break;
                    case '\b': rep = "\\b"; break;
                    case '\f': rep = "\\f"; break;
                    default:
                        if (c < 0x20)
                            rep = "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
                        break;
                }

                if (rep == null)
                {
                    sb?.Append(c);
                    continue;
                }

                if (sb == null)
                {
                    sb = new StringBuilder(s.Length + 8);
                    sb.Append(s, 0, i);
                }
                sb.Append(rep);
            }

            return sb == null ? s : sb.ToString();
        }
    }
}
