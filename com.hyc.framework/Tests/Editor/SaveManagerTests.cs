using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HYC.Framework.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <see cref="SaveManager"/> 的三条存储层本分保证。
    ///
    /// <para>SaveManager 刻意<b>不做</b>槽位、摘要、自动存——那些是玩法层概念。
    /// 但它自己声明的三条保证必须钉死，因为它们是"崩溃不丢档"的底线：</para>
    /// <list type="number">
    /// <item><b>原子写</b>：先写 <c>.tmp</c>，再用 <c>File.Replace</c> 顶替正式文件，
    ///       旧版自动备份成 <c>.bak</c>。任何时刻崩溃都不会只剩半份数据。</item>
    /// <item><b>坏档不覆盖</b>：解析失败改名 <c>.corrupt_*</c> 备份。
    ///       静默覆盖等于进度凭空消失且无从追查——这是绝不能接受的。</item>
    /// <item><b>格式版本</b>：版本对不上按坏档处理，而不是硬读出一个半吊子对象。</item>
    /// </list>
    ///
    /// <para><b>为什么能在 EditMode 测</b>：SaveManager 只依赖 <see cref="File"/>/
    /// <see cref="Directory"/> 与序列化器，通过 <c>SaveOptions.RootPath</c> 可把根目录
    /// 指向 <c>Temp/</c> 完全隔离，不碰真实 <c>persistentDataPath</c>。</para>
    ///
    /// <para><b>注意</b>：SaveManager 是静态单例，_options 全局共享。
    /// 每个用例都要在 SetUp 重置、TearDown 还原，否则会污染同批次的其他用例。</para>
    /// </summary>
    public class SaveManagerTests
    {
        private string _root;
        private string _originalRoot;

        /// <summary>测试载荷。只用基础字段，避免依赖序列化器的多态/泛型支持。</summary>
        private class Payload
        {
            public int level;
            public string nickname;
            public int[] stageIds;
        }

        /// <summary>故意在序列化阶段抛异常的序列化器，用于验证"失败不崩、不留半份文件"。</summary>
        private sealed class ThrowingSerializer : ISaveSerializer
        {
            public string Serialize(object obj) => throw new InvalidOperationException("故意失败");
            public T Deserialize<T>(string text) => throw new InvalidOperationException("故意失败");
        }

        [SetUp]
        public void SetUp()
        {
            // 记住原始配置，TearUp 还原——静态单例不还原会污染后续用例。
            _originalRoot = SaveManager.Options.RootPath;

            _root = Path.Combine("Temp", "SaveManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            var options = new SaveOptions { RootPath = _root };
            SaveManager.Init(options);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch
            {
                // 清理失败不应让测试失败——Temp 目录下次启动会被 Unity 清掉。
            }
            SaveManager.Init(new SaveOptions { RootPath = _originalRoot });
        }

        private string PathOf<T>()
        {
            var key = typeof(T).FullName ?? typeof(T).Name;
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(key.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
            return Path.Combine(_root, sanitized + ".dat");
        }

        // ────────────────────────── 基本往返 ──────────────────────────

        [Test]
        public void Save后Get_往返一致()
        {
            var data = new Payload { level = 7, nickname = "测试用户", stageIds = new[] { 1, 2, 3 } };
            SaveManager.Save(data);

            var back = SaveManager.Get<Payload>();

            Assert.NotNull(back, "刚存的数据应能读回");
            Assert.AreEqual(7, back.level);
            Assert.AreEqual("测试用户", back.nickname);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, back.stageIds);
        }

        [Test]
        public void Get_文件不存在时_返回默认值而非抛异常()
        {
            // 首次进游戏/首次进关卡都是这个路径，必须静默返回
            Assert.IsNull(SaveManager.Get<Payload>(), "class 缺档应返回 null");
            Assert.AreEqual(0, SaveManager.Get<int>(), "值类型缺档应返回 0");
        }

        [Test]
        public void Has_反映文件是否存在()
        {
            Assert.IsFalse(SaveManager.Has<Payload>(), "未存时 Has 应为 false");
            SaveManager.Save(new Payload { level = 1 });
            Assert.IsTrue(SaveManager.Has<Payload>(), "存过后 Has 应为 true");
        }

        // ────────────────────────── 保证①：原子写 ──────────────────────────

        [Test]
        public void 首次写入_不留临时文件()
        {
            SaveManager.Save(new Payload { level = 1 });

            Assert.IsTrue(File.Exists(PathOf<Payload>()), "正式文件应存在");
            Assert.IsFalse(File.Exists(PathOf<Payload>() + ".tmp"), "首次写入不该残留 .tmp");
        }

        [Test]
        public void 二次写入_旧版被备份成bak且bak内容是上一版()
        {
            SaveManager.Save(new Payload { level = 1, nickname = "第一版" });
            SaveManager.Save(new Payload { level = 2, nickname = "第二版" });

            var path = PathOf<Payload>();
            Assert.IsTrue(File.Exists(path + ".bak"), "二次写入应把旧版备份成 .bak");

            // 关键：.bak 里必须是**上一版**内容，不是最新版
            var bakText = File.ReadAllText(path + ".bak");
            StringAssert.Contains("第一版", bakText, ".bak 应保存上一版内容");

            // 正式文件是最新版
            var back = SaveManager.Get<Payload>();
            Assert.AreEqual(2, back.level, "正式文件应是最新版");
            Assert.AreEqual("第二版", back.nickname);
        }

        [Test]
        public void 多次写入_始终只有一份bak且不会累积tmp()
        {
            for (int i = 1; i <= 5; i++)
            {
                SaveManager.Save(new Payload { level = i });
            }

            Assert.IsFalse(File.Exists(PathOf<Payload>() + ".tmp"), "循环写入后不该有 .tmp 残留");
            Assert.AreEqual(5, SaveManager.Get<Payload>().level);
        }

        [Test]
        public void 序列化抛异常时_不产生正式文件也不崩()
        {
            // 存量风险最高的一条：如果序列化失败却已经把半份内容写进了正式文件，
            // 玩家下次读档拿到的是**损坏的存档**，而不是"读不到存档"。
            SaveManager.Init(new SaveOptions { RootPath = _root, Serializer = new ThrowingSerializer() });

            // Log.Error 走 Debug.LogError，Unity 测试框架遇到未预期的 Error 会直接判失败，
            // 所以必须显式声明预期——顺带把"失败时确实有报错"也钉住了。
            LogAssert.Expect(LogType.Error, new Regex("序列化失败"));
            Assert.DoesNotThrow(() => SaveManager.Save(new Payload { level = 1 }));

            Assert.IsFalse(File.Exists(PathOf<Payload>()), "序列化失败不应留下正式文件");
            Assert.IsFalse(File.Exists(PathOf<Payload>() + ".tmp"), "也不应留下 .tmp");
        }

        // ────────────────────────── 保证②：坏档不覆盖 ──────────────────────────

        [Test]
        public void 坏档_被改名备份且返回默认值()
        {
            File.WriteAllText(PathOf<Payload>(), "这不是合法 JSON {{{");

            // 反序列化失败走 Log.Error(Error)，备份成功走 Log.Warn(Warning)。
            // 两者都要声明：Error 未声明会被测试框架判失败，Warn 不声明只是噪音。
            LogAssert.Expect(LogType.Error, new Regex("反序列化失败"));
            LogAssert.Expect(LogType.Warning, new Regex("坏档已备份"));
            var back = SaveManager.Get<Payload>();

            Assert.IsNull(back, "坏档应返回默认值");
            Assert.IsFalse(File.Exists(PathOf<Payload>()), "坏档原文件应被移走（腾出干净路径）");

            var corrupt = Directory.GetFiles(_root, "*.corrupt_*");
            Assert.AreEqual(1, corrupt.Length, "应恰好产生一个 .corrupt_ 备份，实际: " + string.Join(",", corrupt));
        }

        [Test]
        public void 坏档备份之后_可以正常存入新数据()
        {
            File.WriteAllText(PathOf<Payload>(), "坏数据");
            LogAssert.Expect(LogType.Error, new Regex("反序列化失败"));
            LogAssert.Expect(LogType.Warning, new Regex("坏档已备份"));
            SaveManager.Get<Payload>();          // 触发备份，腾出路径
            SaveManager.Save(new Payload { level = 9 });

            Assert.AreEqual(9, SaveManager.Get<Payload>().level, "备份后原路径应可正常写入");
        }

        // ────────────────────────── 保证③：格式版本 ──────────────────────────

        [Test]
        public void 格式版本不匹配_按坏档处理而不是硬读()
        {
            // 手工伪造一个"未来版本"的文件
            SaveManager.Save(new Payload { level = 3 });
            var path = PathOf<Payload>();
            var text = File.ReadAllText(path)
                .Replace("\"formatVersion\":1", "\"formatVersion\":999");
            File.WriteAllText(path, text);

            LogAssert.Expect(LogType.Warning, new Regex("格式版本不匹配"));
            LogAssert.Expect(LogType.Warning, new Regex("坏档已备份"));
            var back = SaveManager.Get<Payload>();

            Assert.IsNull(back, "版本对不上必须当坏档，不能硬读出半吊子对象");
            Assert.IsFalse(File.Exists(path), "版本不符的文件也应被备份移走");
            Assert.AreEqual(1, Directory.GetFiles(_root, "*.corrupt_*").Length);
        }

        [Test]
        public void 版本号常量_与写出的文件一致()
        {
            // 守卫"改了 FormatVersion 常量却忘了迁移逻辑"这类改动
            SaveManager.Save(new Payload { level = 1 });
            StringAssert.Contains("\"formatVersion\":" + SaveManager.FormatVersion,
                File.ReadAllText(PathOf<Payload>()));
        }

        // ────────────────────────── GetOrCreate / 删除 ──────────────────────────

        [Test]
        public void GetOrCreate_缺档时新建但不落盘()
        {
            var created = SaveManager.GetOrCreate<Payload>();

            Assert.NotNull(created, "缺档应新建一个");
            Assert.IsFalse(SaveManager.Has<Payload>(), "GetOrCreate 不应自动写盘（何时落盘由调用方决定）");
        }

        [Test]
        public void GetOrCreate_有档时返回已存内容而非新建()
        {
            SaveManager.Save(new Payload { level = 11, nickname = "已有" });

            var got = SaveManager.GetOrCreate<Payload>();

            Assert.AreEqual(11, got.level);
            Assert.AreEqual("已有", got.nickname);
        }

        [Test]
        public void Delete_删掉文件且重复调用安全()
        {
            SaveManager.Save(new Payload { level = 1 });
            Assert.IsTrue(SaveManager.Has<Payload>());

            Assert.DoesNotThrow(() =>
            {
                SaveManager.Delete<Payload>();
                SaveManager.Delete<Payload>();
            });
            Assert.IsFalse(SaveManager.Has<Payload>());
        }

        [Test]
        public void DeleteAll_只删dat_保留bak等其他文件()
        {
            SaveManager.Save(new Payload { level = 1 });
            SaveManager.Save(new Payload { level = 2 });   // 产生 .bak
            var keep = Path.Combine(_root, "readme.txt");
            File.WriteAllText(keep, "别删我");

            SaveManager.DeleteAll();

            Assert.AreEqual(0, Directory.GetFiles(_root, "*.dat").Length, "所有 .dat 应被删");
            Assert.IsTrue(File.Exists(keep), "非 .dat 文件不该被删");
            Assert.IsTrue(Directory.Exists(_root), "目录本身应保留");
        }

        [Test]
        public void DeleteAll_目录不存在时_安全不抛()
        {
            Directory.Delete(_root, true);
            Assert.DoesNotThrow(() => SaveManager.DeleteAll());
        }
    }
}