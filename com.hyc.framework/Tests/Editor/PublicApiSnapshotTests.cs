using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <b>公开 API 签名守护（P0-3）</b>：把框架的 public API 签名序列化成一份文本快照
    /// （<c>Tests/Editor/PublicApiSnapshot.txt</c>），测试时比对。
    ///
    /// <b>为什么需要</b>：框架被多个工程以 <c>file:</c> 方式引用、<b>没有内嵌副本</b>，
    /// 改框架即时生效。于是任何"顺手改个签名"都会立刻打断使用方的编译，且因为没有版本锁，
    /// 使用方不会收到任何提示——只能自己撞到编译错误才知道。
    /// 典型事故形态：把 <c>public void Foo(int)</c> 改成 <c>public void Foo(int, bool = false)</c>
    /// 源码级看似兼容，二进制签名已变。
    ///
    /// <b>怎么用</b>：
    /// <list type="bullet">
    /// <item>首次运行 → 测试失败并提示"快照缺失"，把生成的文件提交即可。</item>
    /// <item>有意改动 API → 确认这就是想要的破坏性变更，再更新快照
    /// （菜单 <c>Tools/Tests/Update Public API Snapshot</c>，或删除快照文件后重跑测试自动重建）。</item>
    /// <item>无意改动 → 测试失败说明回归被及早拦下。</item>
    /// </list>
    ///
    /// <b>取舍</b>：这是<b>快照测试</b>而非语义测试——它不判断改动好坏，只报告"变了"。
    /// 这正是目的：公开 API 的任何变化都应当是<b>显式决定</b>，而不是编辑时的意外。
    ///
    /// <b>范围</b>：只扫 <c>HYC.Framework.Runtime</c> 与 <c>HYC.Framework.Editor</c> 两个程序集的
    /// <b>public 类型与其 public 成员签名</b>。不含实现体、不含私有成员——
    /// 私有怎么改都不影响使用方，不该制造噪音。
    /// </summary>
    public static class PublicApiSnapshot
    {
        /// <summary>快照文件路径（相对工程根）。放在 Tests 下，随测试代码一起提交。</summary>
        public const string SnapshotPath = "Tests/Editor/PublicApiSnapshot.txt";

        /// <summary>参与守护的两个程序集名。</summary>
        private static readonly string[] Assemblies =
        {
            "HYC.Framework.Runtime",
            "HYC.Framework.Editor",
        };

        /// <summary>签名规范化 + 排序后的完整快照文本。</summary>
        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# HYC Framework 公开 API 签名快照（由 Tests/Editor/PublicApiSnapshotTests.cs 生成）");
            sb.AppendLine("# 用途：检测 unintentional breaking change。改 API 是有意行为时请同步更新本文件。");
            sb.AppendLine("# 范围：public 类型 + 其 public 成员签名；不含实现体与私有成员。");
            sb.AppendLine("# 格式：每行一条签名，按类型名→成员名排序（保证跨机器稳定）。");
            sb.AppendLine();

            foreach (var asmName in Assemblies)
            {
                var asm = FindAssembly(asmName);
                sb.AppendLine($"===== assembly {asmName} =====");
                if (asm == null)
                {
                    sb.AppendLine("(assembly not found — 检查 asmdef name 是否改动)");
                    sb.AppendLine();
                    continue;
                }

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types
                             .Where(IsPublicApi)
                             .OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    sb.AppendLine(SignatureOf(t));

                    foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance |
                                                    BindingFlags.Static | BindingFlags.DeclaredOnly)
                                 .Select(Describe)
                                 .Where(s => s != null)
                                 .OrderBy(s => s, StringComparer.Ordinal))
                    {
                        sb.AppendLine("    " + m);
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>写快照文件（供菜单与首次生成调用）。返回是否真的写了。</summary>
        public static bool Write()
        {
            var abs = Path.Combine(Directory.GetCurrentDirectory(), SnapshotPath);
            var dir = Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var text = Build();
            if (File.Exists(abs) && string.Equals(File.ReadAllText(abs), text, StringComparison.Ordinal))
                return false;

            File.WriteAllText(abs, text, new UTF8Encoding(false));
            // 让 AssetDatabase 立刻看到，避免 Unity 下次 Refresh 才刷新
            AssetDatabase.ImportAsset(SnapshotPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static Assembly FindAssembly(string name)
        {
            // 直接取已加载的（测试程序集引用了它，通常已在域里）
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == name) return a;
            return null;
        }

        /// <summary>只把"使用方可能引用到"的类型算进公开 API。</summary>
        private static bool IsPublicApi(Type t)
        {
            if (t == null) return false;
            // 嵌套类型要逐层 public；顶层类型 public 即可（internal 的不导出）
            if (!t.IsPublic && !t.IsNestedPublic) return false;
            // 编译器生成的与编辑器内部类型不进快照
            if (t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) return false;
            return true;
        }

        private static string SignatureOf(Type t)
        {
            var kind = t.IsEnum ? "enum"
                : t.IsInterface ? "interface"
                : t.IsValueType ? "struct"
                : "class";
            var mods = (t.IsAbstract && t.IsSealed) ? "static "
                : t.IsAbstract ? "abstract "
                : t.IsSealed && t.IsValueType ? "readonly "
                : "";
            var name = t.IsGenericTypeDefinition
                ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(a => a.Name)) + ">"
                : t.Name;
            return $"{mods}{kind} {t.FullName ?? name}";
        }

        /// <summary>成员 → 单行签名；不适合快照的成员返回 null。</summary>
        private static string Describe(MemberInfo m)
        {
            switch (m)
            {
                case MethodInfo mi:
                    // 编译器生成的（属性 getter/setter、lambda）不收
                    if (mi.IsSpecialName) return null;
                    return $"method {Visibility(mi)}{mi.ReturnType.Name} {mi.Name}{Parameters(mi)}";

                case PropertyInfo pi:
                    var acc = (pi.GetMethod != null ? "get;" : "")
                              + (pi.SetMethod != null ? (pi.SetMethod.IsPublic ? "set;" : "protected set;") : "");
                    return $"property {pi.PropertyType.Name} {pi.Name} {{ {acc} }}";

                case FieldInfo fi:
                    return $"field {Visibility(fi)}{fi.FieldType.Name} {fi.Name}";

                case ConstructorInfo ci:
                    return $"ctor {Visibility(ci)}{ci.DeclaringType?.Name}({string.Join(", ", ci.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";

                case EventInfo ei:
                    return $"event {ei.EventHandlerType?.Name} {ei.Name}";

                default:
                    return null;
            }
        }

        private static string Visibility(MethodBase m) => m.IsPublic ? "public " : "protected ";

        private static string Visibility(FieldInfo f) => f.IsPublic ? "public " : "protected ";

        /// <summary>
        /// 参数列表：类型 + 名字 + 默认值标记。
        /// <b>默认值必须纳入快照</b>——把 <c>Foo(int a = 1)</c> 改成 <c>Foo(int a = 2)</c>
        /// 源码级看不出破坏性，但已编译的调用方行为会静默改变。
        /// </summary>
        private static string Parameters(MethodBase m)
        {
            var ps = m.GetParameters();
            if (ps.Length == 0) return "()";
            return "(" + string.Join(", ", ps.Select(p =>
            {
                // ParameterType 对 ref/out 是 "T&"；TrimEnd 去掉 &，前缀另行标注
                var typeName = p.ParameterType.Name.TrimEnd('&');
                var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "";
                var def = p.HasDefaultValue ? " = " + (p.DefaultValue?.ToString() ?? "null") : "";
                return prefix + typeName + " " + p.Name + def;
            })) + ")";
        }
    }

    /// <summary>
    /// 公开 API 快照比对测试。详见 <see cref="PublicApiSnapshot"/>。
    /// </summary>
    public class PublicApiSnapshotTests
    {
        [Test]
        public void 公开API签名_与快照一致()
        {
            var abs = Path.Combine(Directory.GetCurrentDirectory(), PublicApiSnapshot.SnapshotPath);

            if (!File.Exists(abs))
            {
                // 首次运行：自动生成并给出可操作的提示，而不是让测试莫名失败
                PublicApiSnapshot.Write();
                Assert.Fail(
                    $"快照文件不存在，已自动生成：{PublicApiSnapshot.SnapshotPath}\n" +
                    "请检查生成内容是否符合预期后，把该文件一起提交（它是公开 API 的基线）。\n" +
                    "之后若你有意改动公开 API，删掉该文件重跑本测试即可重建。");
            }

            var expected = File.ReadAllText(abs);
            var actual = PublicApiSnapshot.Build();

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                // 给出可读 diff：逐行对比，只报差异行，避免几百行刷屏
                var expLines = expected.Split('\n');
                var actLines = actual.Split('\n');
                var added = actLines.Where(l => l.Trim().Length > 0 && !expLines.Contains(l)).Take(30).ToList();
                var removed = expLines.Where(l => l.Trim().Length > 0 && !actLines.Contains(l)).Take(30).ToList();

                var sb = new StringBuilder();
                sb.AppendLine("公开 API 签名与快照不一致 —— 这可能是无意的破坏性变更。");
                sb.AppendLine("若确为有意改动：删掉 " + PublicApiSnapshot.SnapshotPath + " 重跑本测试，或执行菜单「Tools/Tests/Update Public API Snapshot」。");
                if (removed.Count > 0) { sb.AppendLine("\n--- 消失的签名（最多 30 条）---"); removed.ForEach(l => sb.AppendLine("  - " + l.Trim())); }
                if (added.Count > 0) { sb.AppendLine("\n--- 新增的签名（最多 30 条）---"); added.ForEach(l => sb.AppendLine("  + " + l.Trim())); }
                Assert.Fail(sb.ToString());
            }
        }
    }
}