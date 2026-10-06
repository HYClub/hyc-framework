using System;
using HYC.Framework.Config;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// 测试用的配置行（blittable 才能进 Blob）。
    /// </summary>
    public struct TestCfgRow
    {
        public long ID;
        public int Value;
    }

    /// <summary>
    /// <see cref="ConfigBlobTable{TRow}"/> 与 <see cref="ConfigManager"/> 的行为约定。
    ///
    /// 这里刻意把「两条构建路径的 id 语义不一致」钉成测试：
    /// <c>Build</c> 用 <c>index.TryAdd(i, i)</c>（行序即 id，<b>id=0 有效</b>），
    /// 而 <c>FromBlob</c> 用 <c>if (key != 0) index.TryAdd(...)</c>（<b>跳过 id=0</b>）。
    /// 二者对同一份数据会给出不同的查询结果，属于真实缺陷而非风格问题。
    /// 本测试记录<b>当前实际行为</b>，使该差异可见、可回归；
    /// 待修复时该用例会失败并提醒同步更新期望值。
    /// </summary>
    public unsafe class ConfigBlobTableTests
    {
        private ConfigBlobTable<TestCfgRow> _table;

        [TearDown]
        public void TearDown()
        {
            if (_table.IsValid || _table.Rows.Length != 0)
                _table.Dispose();
        }

        [Test]
        public void Build_注册后可按行序ID查到全部行()
        {
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 10 },
                new TestCfgRow { ID = 1, Value = 20 },
                new TestCfgRow { ID = 2, Value = 30 },
            });

            Assert.AreEqual(3, _table.Count, "Build 三行 → Count 应为 3");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(_table.TryGet(i, out var row), $"按行序 id={i} 应查得到");
                Assert.AreEqual((i + 1) * 10, row.Value);
            }
        }

        [Test]
        public void Build_ID0_可被查到()
        {
            // 钉住当前行为：Build 路径把 id=0 当有效键收录。
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 99 },
            });

            Assert.IsTrue(_table.TryGet(0, out var row), "Build 路径下 id=0 应可查");
            Assert.AreEqual(99, row.Value);
        }

        [Test]
        public void FromBlob_ID0_查不到_与Build路径不一致()
        {
            // 记录已知的路径间不一致：FromBlob 显式跳过 key==0。
            // 这条用例存在的意义是——若将来统一了语义，这里会失败并提示更新期望值。
            using var blob = BuildBlob(new[]
            {
                new TestCfgRow { ID = 0, Value = 99 },
                new TestCfgRow { ID = 7, Value = 77 },
            });

            var table = ConfigBlobTable<TestCfgRow>.FromBlob(blob, r => r.ID);
            try
            {
                Assert.IsTrue(table.TryGet(7, out var r7), "非零 id 应正常查到");
                Assert.AreEqual(77, r7.Value);

                Assert.IsFalse(table.TryGet(0, out _),
                    "已知不一致：FromBlob 跳过 key==0（源码 if (key != 0)），与 Build 相反。修好后请把本断言改为 IsTrue。");
            }
            finally
            {
                table.Dispose();
            }
        }

        [Test]
        public void TryGet_越界ID_返回false且不崩()
        {
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 1 },
            });

            Assert.IsFalse(_table.TryGet(9999, out var missing));
            Assert.AreEqual(0, missing.Value, "失败时 out 参数应为 default");
        }

        [Test]
        public void TryGetRowAt_越界索引_返回false()
        {
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 5 },
            });

            Assert.IsTrue(_table.TryGetRowAt(0, out var r0));
            Assert.AreEqual(5, r0.Value);
            Assert.IsFalse(_table.TryGetRowAt(1, out _), "越界索引应返回 false");
            Assert.IsFalse(_table.TryGetRowAt(-1, out _), "负索引应返回 false（用无符号比较防越界）");
        }

        [Test]
        public void ConfigManager_未注册时查询安全返回默认值()
        {
            ConfigManager.Clear();
            Assert.IsFalse(ConfigManager.Has<TestCfgRow>());
            Assert.IsFalse(ConfigManager.TryGet(0, out _));
            Assert.AreEqual(0, ConfigManager.GetRowCount<TestCfgRow>());
        }

        [Test]
        public void ConfigManager_注册后可查询且Clear可复位()
        {
            ConfigManager.Clear();
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 11 },
                new TestCfgRow { ID = 1, Value = 22 },
            });
            ConfigManager.Register(_table);

            Assert.IsTrue(ConfigManager.Has<TestCfgRow>());
            Assert.IsTrue(ConfigManager.TryGet(1, out var got));
            Assert.AreEqual(22, got.Value);
            Assert.AreEqual(2, ConfigManager.GetRowCount<TestCfgRow>());

            ConfigManager.Clear();
            Assert.IsFalse(ConfigManager.Has<TestCfgRow>(), "Clear 后不应残留注册");
            ConfigManager.Clear(); // 幂等：重复 Clear 不应抛
        }

        private static BlobAssetReference<BlobRoot<TestCfgRow>> BuildBlob(TestCfgRow[] rows)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BlobRoot<TestCfgRow>>();
            var arr = builder.Allocate(ref root.Rows, rows.Length);
            for (var i = 0; i < rows.Length; i++) arr[i] = rows[i];
            var asset = builder.CreateBlobAssetReference<BlobRoot<TestCfgRow>>(Allocator.Persistent);
            builder.Dispose();
            return asset;
        }
    }
}