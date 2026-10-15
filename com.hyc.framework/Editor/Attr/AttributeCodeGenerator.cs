// ============================================================
// HYC Framework - 属性模块(Editor)
// 文件: Editor/Attr/AttributeCodeGenerator.cs
// 说明: 扫属性目录 → 一键生成三件套（优化方向 A/B）：
//       · <前缀>AttrKey.cs      —— enum AttrKey（序号 = 目录顺序），拼错编译期报错
//       · <前缀>AttrKeyMap.cs   —— 枚举序号 → FNV-1a 哈希 / 类型的映射表（生成期算好）
//       · <前缀>AttrBag.cs      —— 类型安全 AttrBag（实现 IAttrBag，暴露 GetInt(AttrKey.ATK) 等）
//       重跑会覆盖这三件，enum/映射/袋子均幂等（无 #region USER，因为不需要手写区）。
// ============================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using HYC.Framework.Attr;
using HYC.Framework.Editor;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Attr.Editor
{
    public static class AttributeCodeGenerator
    {
        /// <summary>扫目录生成三件套。</summary>
        /// <returns>null = 成功；否则返回错误信息。</returns>
        public static string Generate(AttributeCatalog cat, string folder, string prefix, string ns)
        {
            if (cat == null) return "属性目录为空。";
            if (string.IsNullOrEmpty(folder)) return "输出目录不能为空。";

            var errs = AttributeValidator.Validate(cat).FindAll(i => i.IsError);
            if (errs.Count > 0)
                return "属性目录有错误，先修正：\n" + string.Join("\n", errs.ConvertAll(i => i.Message));

            prefix = SanitizePrefix(prefix);
            if (string.IsNullOrEmpty(prefix)) return "类名前缀清洗后为空，请换一个。";

            // 清洗每个 key 成合法枚举名，处理重复
            var names = new List<string>();
            var used = new HashSet<string>();
            for (int i = 0; i < cat.entries.Count; i++)
            {
                var n = SanitizeIdent(cat.entries[i].key);
                if (string.IsNullOrEmpty(n)) n = "Attr" + i;
                while (!used.Add(n)) n += "_";
                names.Add(n);
            }

            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            // 走统一写盘入口：内容未变不重写 + 自动套「自动生成」壳
            // （原 Build* 里的 ad-hoc 头已移除，禁改声明统一由壳承担）
            GeneratedCodeFile.WriteIfChanged(Path.Combine(folder, prefix + "AttrKey.cs"),
                GeneratedCodeFile.WithHeader(BuildEnum(cat, names, prefix, ns)));
            GeneratedCodeFile.WriteIfChanged(Path.Combine(folder, prefix + "AttrKeyMap.cs"),
                GeneratedCodeFile.WithHeader(BuildMap(cat, names, prefix, ns)));
            GeneratedCodeFile.WriteIfChanged(Path.Combine(folder, prefix + "AttrBag.cs"),
                GeneratedCodeFile.WithHeader(BuildBag(cat, names, prefix, ns)));

            AssetDatabase.Refresh();
            return null;
        }

        private static string BuildEnum(AttributeCatalog cat, List<string> names, string prefix, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using HYC.Framework.Attr;");
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>属性键枚举（序号 = 目录顺序）。拼错会在编译期报错，零手写字符串 key。</summary>");
            sb.AppendLine("    public enum " + prefix + "AttrKey");
            sb.AppendLine("    {");
            for (int i = 0; i < names.Count; i++)
                sb.AppendLine("        " + names[i] + " = " + i + ",");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildMap(AttributeCatalog cat, List<string> names, string prefix, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using HYC.Framework.Attr;");
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>枚举序号 → FNV-1a 哈希 / 类型的映射表（生成期算好，运行期直接查）。</summary>");
            sb.AppendLine("    public static class " + prefix + "AttrKeyMap");
            sb.AppendLine("    {");
            sb.AppendLine("        public const int Count = " + names.Count + ";");
            sb.AppendLine("        public static readonly ulong[] Hash = new ulong[]");
            sb.AppendLine("        {");
            for (int i = 0; i < names.Count; i++)
                sb.AppendLine("            " + AttributeKey.Hash(cat.entries[i].key) + "UL,");
            sb.AppendLine("        };");
            sb.AppendLine("        public static readonly AttributeType[] Type = new AttributeType[]");
            sb.AppendLine("        {");
            for (int i = 0; i < names.Count; i++)
                sb.AppendLine("            AttributeType." + cat.entries[i].type + ",");
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildBag(AttributeCatalog cat, List<string> names, string prefix, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using HYC.Framework.Attr;");
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>类型安全属性袋（枚举版）：包 (EntityManager, Entity)，转调框架 AttributeStore。</summary>");
            sb.AppendLine("    public class " + prefix + "AttrBag : IAttrBag");
            sb.AppendLine("    {");
            sb.AppendLine("        readonly EntityManager em;");
            sb.AppendLine("        readonly Entity e;");
            sb.AppendLine("        public " + prefix + "AttrBag(EntityManager em, Entity e) { this.em = em; this.e = e; }");
            sb.AppendLine();
            // IAttrBag 契约是 ulong key 版（见 IAttrBag），必须实现这 8 个成员，否则 CS0535。
            sb.AppendLine("        // —— IAttrBag 契约（ulong key 版）：调用方直接传名哈希 ——");
            sb.AppendLine("        public int GetInt(ulong key) => AttributeStore.GetInt(em, e, key);");
            sb.AppendLine("        public void SetInt(ulong key, int v) => AttributeStore.SetInt(em, e, key, v);");
            sb.AppendLine("        public float GetFloat(ulong key) => AttributeStore.GetFloat(em, e, key);");
            sb.AppendLine("        public void SetFloat(ulong key, float v) => AttributeStore.SetFloat(em, e, key, v);");
            sb.AppendLine("        public long GetLong(ulong key) => AttributeStore.GetLong(em, e, key);");
            sb.AppendLine("        public void SetLong(ulong key, long v) => AttributeStore.SetLong(em, e, key, v);");
            sb.AppendLine("        public bool GetBool(ulong key) => AttributeStore.GetBool(em, e, key);");
            sb.AppendLine("        public void SetBool(ulong key, bool v) => AttributeStore.SetBool(em, e, key, v);");
            sb.AppendLine();
            // 枚举友好重载：经 AttrKeyMap 把枚举映射成哈希后转调 AttributeStore。
            sb.AppendLine("        // —— 枚举友好重载：经 " + prefix + "AttrKeyMap 映射到名哈希 ——");
            sb.AppendLine("        public int GetInt(" + prefix + "AttrKey k) => AttributeStore.GetInt(em, e, " + prefix + "AttrKeyMap.Hash[(int)k]);");
            sb.AppendLine("        public void SetInt(" + prefix + "AttrKey k, int v) => AttributeStore.SetInt(em, e, " + prefix + "AttrKeyMap.Hash[(int)k], v);");
            sb.AppendLine("        public float GetFloat(" + prefix + "AttrKey k) => AttributeStore.GetFloat(em, e, " + prefix + "AttrKeyMap.Hash[(int)k]);");
            sb.AppendLine("        public void SetFloat(" + prefix + "AttrKey k, float v) => AttributeStore.SetFloat(em, e, " + prefix + "AttrKeyMap.Hash[(int)k], v);");
            sb.AppendLine("        public long GetLong(" + prefix + "AttrKey k) => AttributeStore.GetLong(em, e, " + prefix + "AttrKeyMap.Hash[(int)k]);");
            sb.AppendLine("        public void SetLong(" + prefix + "AttrKey k, long v) => AttributeStore.SetLong(em, e, " + prefix + "AttrKeyMap.Hash[(int)k], v);");
            sb.AppendLine("        public bool GetBool(" + prefix + "AttrKey k) => AttributeStore.GetBool(em, e, " + prefix + "AttrKeyMap.Hash[(int)k]);");
            sb.AppendLine("        public void SetBool(" + prefix + "AttrKey k, bool v) => AttributeStore.SetBool(em, e, " + prefix + "AttrKeyMap.Hash[(int)k], v);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>把任意字符串洗成合法的 C# 类型名前缀（首字符为字母，其余字母/数字/下划线）。</summary>
        public static string SanitizePrefix(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder();
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-' || c == '/' || c == '\\' || c == '.') sb.Append('_');
            }
            var s = sb.ToString().Trim('_');
            if (s.Length == 0) return "";
            if (!char.IsLetter(s[0]) && s[0] != '_') s = "_" + s;
            return s;
        }

        private static string SanitizeIdent(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder();
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            var s = sb.ToString().Trim('_');
            if (s.Length == 0) return "";
            if (!char.IsLetter(s[0]) && s[0] != '_') s = "_" + s;
            return s;
        }
    }
}
