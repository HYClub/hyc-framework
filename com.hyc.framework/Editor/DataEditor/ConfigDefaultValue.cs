namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 模板字段「默认值」的唯一真源：按值槽读取 → 校验 → 生成 C# 字段初始化器字面量。
    /// 编辑器（折叠区控件 / 顶部错误汇总）与代码生成器共用，保证 UI 可选性与生成条件一致。
    /// 只落主类 ScriptableObject 的字段声明；侧类型与 Blob 是 struct，不允许实例字段初始化器。
    /// </summary>
    public static class ConfigDefaultValue
    {
        /// <summary>该字段能否设置默认值（列表一律不支持）。</summary>
        public static bool IsSupported(HYC.Framework.Config.ConfigTemplateField f)
            => f != null && !f.isList
               && HYC.Framework.Config.ConfigFieldTypes.SupportsDefaultValue(f.type);

        /// <summary>
        /// 枚举默认值按数值反查成员名（数值由 <see cref="HYC.Framework.Config.ConfigEnumDefinition.ValueOf"/> 分配）。
        /// 定义不存在 / 数值无对应成员（含 Flags 组合值）时返回 null。
        /// </summary>
        public static string EnumMemberName(HYC.Framework.Config.ConfigTemplateField f)
        {
            var def = HYC.Framework.Config.Editor.ConfigEnumCodeGen.FindEnum(f.enumRefClassName);
            if (def == null || def.values == null)
                return null;
            for (var i = 0; i < def.values.Count; i++)
            {
                if (def.values[i] != null
                    && HYC.Framework.Config.ConfigEnumDefinition.ValueOf(def, i) == f.defaultEnumValue)
                    return def.values[i].name;
            }
            return null;
        }

        /// <summary>校验默认值能否安全落成字段初始化器；未启用默认值恒通过。</summary>
        public static bool TryValidate(HYC.Framework.Config.ConfigTemplateField f, out string error)
        {
            error = null;
            if (f == null || !f.useDefault)
                return true;

            if (!IsSupported(f))
            {
                error = f.isList ? "列表字段不支持默认值，请取消勾选"
                    : $"类型 {f.type} 不支持默认值，请取消勾选或改用标量类型";
                return false;
            }

            var slot = HYC.Framework.Config.ConfigFieldTypes.DefaultSlotOf(f.type);
            if (slot == HYC.Framework.Config.ConfigDefaultSlot.Number)
            {
                var n = f.defaultNumber;
                var inRange = true;
                switch (f.type)
                {
                    case HYC.Framework.Config.ConfigFieldType.Int:
                        inRange = n >= System.Int32.MinValue && n <= System.Int32.MaxValue; break;
                    case HYC.Framework.Config.ConfigFieldType.Short:
                        inRange = n >= System.Int16.MinValue && n <= System.Int16.MaxValue; break;
                    case HYC.Framework.Config.ConfigFieldType.Byte:
                        inRange = n >= System.Byte.MinValue && n <= System.Byte.MaxValue; break;
                    case HYC.Framework.Config.ConfigFieldType.UInt:
                        inRange = n >= 0 && n <= System.UInt32.MaxValue; break;
                }
                if (!inRange)
                {
                    error = $"默认值 {n} 超出 {f.type} 的取值范围";
                    return false;
                }
            }
            else if (slot == HYC.Framework.Config.ConfigDefaultSlot.Real)
            {
                var d = f.defaultReal;
                if (System.Double.IsNaN(d) || System.Double.IsInfinity(d)
                    || (f.type == HYC.Framework.Config.ConfigFieldType.Float
                        && (d < -System.Single.MaxValue || d > System.Single.MaxValue)))
                {
                    error = $"默认值 {d} 不是 {f.type} 可表示的有限数值";
                    return false;
                }
            }
            else if (slot == HYC.Framework.Config.ConfigDefaultSlot.Enum)
            {
                var member = EnumMemberName(f);
                if (string.IsNullOrEmpty(member))
                {
                    error = $"枚举 {f.enumRefClassName} 中没有数值为 {f.defaultEnumValue} 的成员（Flags 组合值暂不支持）";
                    return false;
                }
                if (!HYC.Framework.Config.Editor.ConfigTemplateCodeGen.IsValidIdentifier(member))
                {
                    error = $"枚举成员名 “{member}” 不是合法的 C# 标识符";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 生成 C# 字面量。前置条件：<see cref="TryValidate"/> 通过。
        /// declaredTypeName 取生成器解析出的字段类型名（枚举为带命名空间的枚举类名）。
        /// </summary>
        public static string FormatLiteral(HYC.Framework.Config.ConfigTemplateField f, string declaredTypeName)
        {
            if (f == null || !f.useDefault)
                return "default";

            var slot = HYC.Framework.Config.ConfigFieldTypes.DefaultSlotOf(f.type);
            switch (slot)
            {
                case HYC.Framework.Config.ConfigDefaultSlot.Number:
                    return NumberLiteral(f);
                case HYC.Framework.Config.ConfigDefaultSlot.Real:
                    return f.type == HYC.Framework.Config.ConfigFieldType.Float
                        ? ((float)f.defaultReal).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f"
                        : f.defaultReal.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d";
                case HYC.Framework.Config.ConfigDefaultSlot.Flag:
                    return f.defaultFlag ? "true" : "false";
                case HYC.Framework.Config.ConfigDefaultSlot.Text:
                    return Quote(f.defaultText);
                case HYC.Framework.Config.ConfigDefaultSlot.Enum:
                    return declaredTypeName + "." + EnumMemberName(f);
                default:
                    return "default";
            }
        }

        private static string NumberLiteral(HYC.Framework.Config.ConfigTemplateField f)
        {
            var text = f.defaultNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (f.type)
            {
                case HYC.Framework.Config.ConfigFieldType.Long:
                    return text + "L";
                case HYC.Framework.Config.ConfigFieldType.UInt:
                    return text + "u";
                default:
                    // int / short / byte：整数字面量在目标类型范围内可隐式常量转换
                    return text;
            }
        }

        /// <summary>转成 C# 字符串字面量（含转义）；控制字符用 \uXXXX，避免生成代码里出现裸换行。</summary>
        private static string Quote(string value)
        {
            var sb = new System.Text.StringBuilder("\"");
            var text = value ?? "";
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\0': sb.Append("\\0"); break;
                    default:
                        if (System.Char.IsControl(c))
                            sb.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append("\"").ToString();
        }
    }
}
