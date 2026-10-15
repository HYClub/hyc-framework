using System;
using System.IO;
using HYC.Framework.Config.Editor;
using NUnit.Framework;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <see cref="ConfigTemplateCodeGen"/> 的 <c>WriteIfChanged</c> 契约。
    ///
    /// <para><b>为什么这个契约要紧</b>：代码生成一次要产出六七个文件，重复生成时内容通常
    /// 没变。无条件重写会白白触发一串 <c>AssetDatabase.ImportAsset</c>，
    /// Unity 侧表现为 .meta 时间戳刷新、编译抖动、版本库里产生无意义 diff。
    /// 这个方法把"内容没变就别碰磁盘"这件事做成了硬约束，值得钉住。</para>
    ///
    /// <para><b>注意</b>：这些用例会在工程内创建临时文件，位于 <c>Temp/</c> 下，
    /// TearDown 会清理。</para>
    /// </summary>
    public class ConfigCodeGenWriteTests
    {
        private const string TempDir = "Temp/ConfigCodeGenWriteTests";
        private string _dirAbs;

        [SetUp]
        public void SetUp()
        {
            _dirAbs = Path.Combine(Directory.GetCurrentDirectory(), TempDir);
            if (Directory.Exists(_dirAbs)) Directory.Delete(_dirAbs, true);
            Directory.CreateDirectory(_dirAbs);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_dirAbs)) Directory.Delete(_dirAbs, true);
            }
            catch (IOException)
            {
                // 文件被 Unity 占用等；临时目录残留不致命，不让测试因此失败
            }
        }

        private string Rel(string name) => TempDir + "/" + name;

        [Test]
        public void 文件不存在时_写入并返回true()
        {
            var ok = ConfigTemplateCodeGen.WriteIfChanged(Rel("New.cs"), "// a");
            Assert.IsTrue(ok, "首次写入应当返回 true");
            Assert.IsTrue(File.Exists(Path.Combine(_dirAbs, "New.cs")), "文件应被创建");
        }

        [Test]
        public void 内容相同_返回false且不改修改时间()
        {
            var path = Rel("Same.cs");
            Assert.IsTrue(ConfigTemplateCodeGen.WriteIfChanged(path, "// content"), "首次写入");

            var abs = Path.Combine(_dirAbs, "Same.cs");
            var firstWrite = File.GetLastWriteTimeUtc(abs);

            // 第二次传完全相同的内容
            var second = ConfigTemplateCodeGen.WriteIfChanged(path, "// content");

            Assert.IsFalse(second, "内容一致时应返回 false（跳过写入）");
            Assert.AreEqual(firstWrite, File.GetLastWriteTimeUtc(abs),
                "内容一致时文件不应被重写，修改时间应保持不变");
        }

        [Test]
        public void 内容不同_返回true且内容被更新()
        {
            var path = Rel("Diff.cs");
            ConfigTemplateCodeGen.WriteIfChanged(path, "// v1");
            Assert.AreEqual("// v1", File.ReadAllText(Path.Combine(_dirAbs, "Diff.cs")));

            var second = ConfigTemplateCodeGen.WriteIfChanged(path, "// v2 更长的内容");

            Assert.IsTrue(second, "内容不同应返回 true");
            Assert.AreEqual("// v2 更长的内容", File.ReadAllText(Path.Combine(_dirAbs, "Diff.cs")),
                "内容应被更新为新值");
        }

        [Test]
        public void 反复生成同一内容_始终返回false()
        {
            // 模拟"连续点几次生成"这一真实场景
            var path = Rel("Repeat.cs");
            var first = ConfigTemplateCodeGen.WriteIfChanged(path, "// stable");
            var second = ConfigTemplateCodeGen.WriteIfChanged(path, "// stable");
            var third = ConfigTemplateCodeGen.WriteIfChanged(path, "// stable");

            Assert.IsTrue(first, "首次应写");
            Assert.IsFalse(second, "第二次内容相同应跳过");
            Assert.IsFalse(third, "第三次内容相同应跳过");
        }

        [Test]
        public void 绝对路径_与相对路径都能工作()
        {
            var relPath = Rel("Both.cs");
            var absPath = Path.Combine(_dirAbs, "Absolute.cs");

            var a = ConfigTemplateCodeGen.WriteIfChanged(relPath, "// relative");
            var b = ConfigTemplateCodeGen.WriteIfChanged(absPath, "// absolute");

            Assert.IsTrue(a, "相对路径应可写");
            Assert.IsTrue(b, "绝对路径应可写");
            Assert.IsTrue(File.Exists(Path.Combine(_dirAbs, "Both.cs")), "相对路径文件应存在");
            Assert.IsTrue(File.Exists(absPath), "绝对路径文件应存在");
        }

        [Test]
        public void 空内容_也能正常写入()
        {
            var path = Rel("Empty.cs");
            var ok = ConfigTemplateCodeGen.WriteIfChanged(path, string.Empty);

            Assert.IsTrue(ok, "空内容首次应写");
            Assert.AreEqual(string.Empty, File.ReadAllText(Path.Combine(_dirAbs, "Empty.cs")));

            // 空内容再写一次同样应跳过
            Assert.IsFalse(ConfigTemplateCodeGen.WriteIfChanged(path, string.Empty),
                "空内容重复写入应跳过");
        }

        [Test]
        public void 换行差异_视为内容不同()
        {
            // Windows 下WriteAllText 不改换行，故 \n 与 \r\n 确实不同。
            // 这条用例钉住"比较是逐字符的，不是行数/语义比较"——
            // 生成代码时缩进变化必须能触发重写，否则会留下过期产物。
            var path = Rel("Eol.cs");
            ConfigTemplateCodeGen.WriteIfChanged(path, "line1\nline2");
            var changed = ConfigTemplateCodeGen.WriteIfChanged(path, "line1\r\nline2");

            Assert.IsTrue(changed, "换行符差异应被视为内容变化");
        }
    }
}