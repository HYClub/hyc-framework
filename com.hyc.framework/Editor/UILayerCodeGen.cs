using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HYC.Framework.UI;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// UI 层表 → 宿主工程生成代码（HYC Settings → UI/层级 的「保存并生成」按钮）。
    ///
    /// <para><b>产出什么</b>：单个文件 <c>Assets/GeneratedUI/UILayer.g.cs</c>，内含两个静态类：</para>
    /// <list type="bullet">
    /// <item><c>UILayer</c>：每层一个 <c>const int</c> 常量 + <c>Count</c>。<b>刻意不用 enum</b>——
    /// 框架 API（<c>UIManager.Open&lt;T&gt;(layer, …)</c>）一旦引用宿主生成的类型就会形成程序集环；
    /// int 常量零耦合，且拼错名字照样编译报错，类型安全性与 enum 等价。</item>
    /// <item>每层一个 <c>Open{层名}&lt;T&gt;(…)</c>：转发到 <c>UIManager.Open&lt;T&gt;(UILayer.xxx, …)</c>，
    /// 与框架原有的角色 Open 方法等价；层表改了重新生成一次，调用点不必手改。</item>
    /// </list>
    ///
    /// <para>写盘统一走 <see cref="GeneratedCodeFile.WriteAndImport"/>：套自动生成壳、内容未变不重写不重导入。</para>
    /// <para>层名不合法 / 重名 / 为空时拒绝生成并说明原因——宁可不写，也不往宿主吐编译不过的代码。</para>
    /// </summary>
    public static class UILayerCodeGen
    {
        /// <summary>生成目录（工程相对），统一收口到 <see cref="GeneratedPaths.UI"/>（生成代码总根下）。</summary>
        public const string OutputDir = GeneratedPaths.UI;

        /// <summary>生成文件名（单文件，层名变化只覆盖它，不会留过时常量文件）。</summary>
        public const string FileName = "UILayer.g.cs";

        /// <summary>生成产物完整路径（工程相对）。</summary>
        public static string OutputPath => $"{OutputDir}/{FileName}";

        /// <summary>
        /// 按当前层表生成宿主侧常量与 Open 方法。成功返回 true；层表有问题时返回 false 且
        /// <paramref name="error"/> 说明原因（此时不写任何文件）。
        /// </summary>
        public static bool Generate(out string error)
        {
            var ns = HYCSettings.UICodeNamespace;
            var code = GenerateCode(ns, out error);
            if (code == null)
                return false;

            // 生成目录首次不存在（全新工程），写盘前先建——File.WriteAllText 不会自建目录
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), OutputDir));

            GeneratedCodeFile.WriteAndImport(OutputPath, code);
            error = null;
            return true;
        }

        /// <summary>拼生成内容（不含自动生成壳，由 <see cref="GeneratedCodeFile"/> 统一套）。</summary>
        public static string GenerateCode(string ns, out string error)
        {
            var defs = UILayerTable.All;
            var problem = Validate(defs);
            if (problem != null)
            {
                error = problem;
                return null;
            }

            var sb = new StringBuilder(1024);
            sb.Append("using HYC.Framework.UI;\n\n");
            sb.Append("namespace ").Append(ns).Append("\n{\n");

            // ---- 常量 ----
            sb.Append("    /// <summary>UI 层常量。由 HYC Settings → UI/层级 生成；改层请改那里后重新生成。</summary>\n");
            sb.Append("    public static class UILayer\n    {\n");

            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                sb.Append("        /// <summary>");
                sb.Append(string.IsNullOrEmpty(def.desc) ? "第 " + i + " 层。" : EscapeDoc(def.desc));
                sb.Append("</summary>\n");
                sb.Append("        public const int ").Append(EscapeIdentifier(def.name)).Append(" = ").Append(i).Append(";\n");
            }

            sb.Append("\n        /// <summary>层数（= 层表长度）。</summary>\n");
            sb.Append("        public const int Count = ").Append(defs.Count).Append(";\n");

            // ---- 每层一个 Open 方法 ----
            foreach (var def in defs)
            {
                var method = "Open" + MethodNamePart(def.name);
                sb.Append("\n        /// <summary>打开 ").Append(EscapeDoc(def.desc)).Append("（层 ").Append(def.name).Append("）。</summary>\n");
                sb.Append("        public static T ").Append(method)
                  .Append("<T>(WindowCanvasType canvas = WindowCanvasType.Normal, UISystemType system = UISystemType.Normal, params object[] args) where T : AbsUISystem\n");
                sb.Append("        {\n");
                sb.Append("            return UIManager.Open<T>(").Append(EscapeIdentifier(def.name)).Append(", canvas, system, args);\n");
                sb.Append("        }\n");
            }

            sb.Append("    }\n}\n");

            error = null;
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 校验

        /// <summary>层表是否可生成；有问题返回原因（中文，直接面向用户），没问题返回 null。</summary>
        private static string Validate(List<UILayerDef> defs)
        {
            if (defs == null || defs.Count == 0)
                return "层表为空：至少需要一个层。";

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var seenMethods = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null)
                    return $"第 {i} 层是空条目，请先删除。";

                if (string.IsNullOrWhiteSpace(def.name))
                    return $"第 {i} 层没有名字：层名会变成生成常量名，必须填。";

                if (!IsIdentifierShape(def.name))
                    return $"层名 “{def.name}” 不是合法的 C# 标识符：只能是字母 / 数字 / 下划线且不能以数字开头。" +
                           "中文描述可以照常写，层名才需要是标识符。";

                if (def.name == "Count")
                    return $"层名 “Count” 是生成类的保留名（层数常量），请改名。";

                if (!seen.Add(def.name))
                    return $"层名 “{def.name}” 重复：生成常量会重名导致宿主编译失败。";

                if (!seenMethods.Add(MethodNamePart(def.name)))
                    return $"层名 “{def.name}” 生成的 Open 方法名 Open{MethodNamePart(def.name)} 与前面的层重复：" +
                           "请避免仅大小写不同的层名，否则宿主编译失败。";
            }

            return null;
        }

        private static bool IsIdentifierShape(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var c = name[0];
            if (c != '_' && !char.IsLetter(c))
                return false;
            for (var i = 1; i < name.Length; i++)
            {
                c = name[i];
                if (c != '_' && !char.IsLetterOrDigit(c))
                    return false;
            }
            return true;
        }

        /// <summary>C# 关键字表。生成的是宿主代码，命中时用 @ 逃逸，不让用户的层名被拒。</summary>
        private static bool IsKeyword(string name)
        {
            switch (name)
            {
                case "abstract": case "as": case "base": case "bool": case "break": case "byte": case "case":
                case "catch": case "char": case "checked": case "class": case "const": case "continue": case "decimal":
                case "default": case "delegate": case "do": case "double": case "else": case "enum": case "event":
                case "explicit": case "extern": case "false": case "finally": case "fixed": case "float": case "for":
                case "foreach": case "goto": case "if": case "implicit": case "in": case "int": case "interface":
                case "internal": case "is": case "lock": case "long": case "namespace": case "new": case "null":
                case "object": case "operator": case "out": case "override": case "params": case "private":
                case "protected": case "public": case "readonly": case "ref": case "return": case "sbyte":
                case "sealed": case "short": case "sizeof": case "stackalloc": case "static": case "string":
                case "struct": case "switch": case "this": case "throw": case "true": case "try": case "typeof":
                case "uint": case "ulong": case "unchecked": case "unsafe": case "ushort": case "using": case "var":
                case "virtual": case "void": case "volatile": case "while":
                    return true;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- 命名

        /// <summary>C# 标识符逃逸：关键字加 @ 前缀（不改用户输入的形状）。</summary>
        private static string EscapeIdentifier(string name) => IsKeyword(name) ? "@" + name : name;

        /// <summary>层名 → 方法名片段：全小写名首字母大写（hud → Hud），其余保持原样（HUD / hudLayer / Hud2D）。</summary>
        private static string MethodNamePart(string name)
        {
            var hasUpper = false;
            foreach (var c in name)
            {
                if (char.IsUpper(c))
                {
                    hasUpper = true;
                    break;
                }
            }

            return hasUpper ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>XML 文档注释内容逃逸（注释只允许一行，撞上 -- 或 --&gt; 会破壳）。</summary>
        private static string EscapeDoc(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Replace("--", "- -").Replace(">", "&gt;").Replace("<", "&lt;").Replace("\n", " ").Replace("\r", " ");
        }
    }
}
