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
    /// <see cref="ConfigBlobTable{TRow}"/> 与 <see cref="ConfigRegistry"/> 的行为约定。
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
        public void ConfigRegistry_未注册时查询安全返回默认值()
        {
            ConfigRegistry.Clear();
            Assert.IsFalse(ConfigRegistry.Has<TestCfgRow>());
            Assert.IsFalse(ConfigRegistry.TryGet(0, out _));
            Assert.AreEqual(0, ConfigRegistry.GetRowCount<TestCfgRow>());
        }

        [Test]
        public void ConfigRegistry_注册后可查询且Clear可复位()
        {
            ConfigRegistry.Clear();
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 11 },
                new TestCfgRow { ID = 1, Value = 22 },
            });
            ConfigRegistry.Register(_table);

            Assert.IsTrue(ConfigRegistry.Has<TestCfgRow>());
            Assert.IsTrue(ConfigRegistry.TryGet(1, out var got));
            Assert.AreEqual(22, got.Value);
            Assert.AreEqual(2, ConfigRegistry.GetRowCount<TestCfgRow>());

            ConfigRegistry.Clear();
            Assert.IsFalse(ConfigRegistry.Has<TestCfgRow>(), "Clear 后不应残留注册");
            ConfigRegistry.Clear(); // 幂等：重复 Clear 不应抛
        }

        [Test]
        public void ConfigRegistry_Contains_注册后命中与未命中()
        {
            ConfigRegistry.Clear();
            // Build 路径下 id == 行序，Contains 走与 GetRef 同款的 TryGetIndex（零拷贝、不取行）
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 0, Value = 11 },
                new TestCfgRow { ID = 1, Value = 22 },
            });
            ConfigRegistry.Register(_table);

            Assert.IsTrue(ConfigRegistry.Contains<TestCfgRow>(1), "已注册 id 应命中");
            Assert.IsTrue(ConfigRegistry.Contains<TestCfgRow>(0), "Build 路径 id=0 应命中");
            Assert.IsFalse(ConfigRegistry.Contains<TestCfgRow>(9999), "不存在的 id 应未命中、不抛");

            ConfigRegistry.Clear();
            Assert.IsFalse(ConfigRegistry.Contains<TestCfgRow>(0), "Clear 后表已注销，应未命中");
        }

        [Test]
        public void ConfigRegistry_GetRowRefAt_按行序取行_越界抛异常()
        {
            ConfigRegistry.Clear();
            // 行序与 id 刻意错开，证明 GetRowRefAt 按 index 而非 id 取行
            _table = ConfigBlobTable<TestCfgRow>.Build(new[]
            {
                new TestCfgRow { ID = 5, Value = 11 },
                new TestCfgRow { ID = 9, Value = 22 },
            });
            ConfigRegistry.Register(_table);

            ref var r0 = ref ConfigRegistry.GetRowRefAt<TestCfgRow>(0);
            Assert.AreEqual(11, r0.Value, "index=0 应为第一行（id=5）");
            ref var r1 = ref ConfigRegistry.GetRowRefAt<TestCfgRow>(1);
            Assert.AreEqual(22, r1.Value, "index=1 应为第二行（id=9）");
            Assert.AreEqual(2, ConfigRegistry.GetRowCount<TestCfgRow>());

            Assert.Throws<KeyNotFoundException>(() => ConfigRegistry.GetRowRefAt<TestCfgRow>(2),
                "越界（== Count）应抛 KeyNotFoundException");
            Assert.Throws<KeyNotFoundException>(() => ConfigRegistry.GetRowRefAt<TestCfgRow>(-1),
                "负索引应抛 KeyNotFoundException（无符号比较防越界）");
        }

        [Test]
        public void ConfigRegistry_GetRowRefAt_未注册表抛异常()
        {
            ConfigRegistry.Clear();
            Assert.Throws<KeyNotFoundException>(() => ConfigRegistry.GetRowRefAt<TestCfgRow>(0),
                "表未注册时应抛 KeyNotFoundException，与 GetRef 失败语义一致");
        }

        [Test]
        public void ConfigRegistry_Contains_未注册表返回false()
        {
            ConfigRegistry.Clear();
            // 表未注册时 Contains 必须返回 false（宽松语义的兜底：不抛、不取行）
            Assert.IsFalse(ConfigRegistry.Contains<TestCfgRow>(0),
                "表未注册时应返回 false，与 GetRef 抛 KeyNotFoundException 互补");
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