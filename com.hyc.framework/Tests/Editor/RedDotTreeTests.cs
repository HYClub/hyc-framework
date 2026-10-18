using HYC.Framework.RedDot;
using NUnit.Framework;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// 红点树聚合/清除/通知的契约测试。
    /// 全部为纯静态逻辑（不碰 Unity 对象），可在无 Unity 渲染的环境下跑。
    /// </summary>
    public class RedDotTreeTests
    {
        private static readonly RedDotKey Root = new RedDotKey(1000);
        private static readonly RedDotKey ChildA = new RedDotKey(1001);
        private static readonly RedDotKey ChildB = new RedDotKey(1002);
        private static readonly RedDotKey GrandChild = new RedDotKey(1003);

        [SetUp]
        public void SetUp()
        {
            RedDotManager.Clear();
            RedDotManager.Register(Root, default, "root");
            RedDotManager.Register(ChildA, Root, "childA");
            RedDotManager.Register(ChildB, Root, "childB");
            RedDotManager.Register(GrandChild, ChildA, "grand");
        }

        [TearDown]
        public void TearDown() => RedDotManager.Clear();

        [Test]
        public void 子节点亮_父与祖父链自动亮()
        {
            RedDotManager.Mark(GrandChild);

            Assert.IsTrue(RedDotManager.IsActive(GrandChild));
            Assert.IsTrue(RedDotManager.IsActive(ChildA), "祖父（直接父）应亮");
            Assert.IsTrue(RedDotManager.IsActive(Root), "根应亮");
            Assert.IsFalse(RedDotManager.IsActive(ChildB), "兄弟不应亮");
        }

        [Test]
        public void 计数向上累加()
        {
            RedDotManager.SetCount(GrandChild, 2);
            RedDotManager.SetCount(ChildB, 3);

            Assert.AreEqual(2, RedDotManager.GetCount(GrandChild));
            Assert.AreEqual(2, RedDotManager.GetCount(ChildA), "父 = 自身 + 子");
            Assert.AreEqual(5, RedDotManager.GetCount(Root), "根 = 子树之和");
            Assert.IsTrue(RedDotManager.IsActive(Root), "计数 > 0 视为亮");
        }

        [Test]
        public void Consume只清自身不动子()
        {
            RedDotManager.Mark(ChildA);
            RedDotManager.Mark(GrandChild);

            RedDotManager.Consume(ChildA);

            Assert.IsFalse(RedDotManager.GetOwnFlag(ChildA), "自身已清");
            Assert.IsTrue(RedDotManager.IsActive(ChildA), "子仍亮 ⇒ 父聚合仍亮");
            Assert.IsTrue(RedDotManager.IsActive(GrandChild), "子未被清");
        }

        [Test]
        public void ConsumeWithChildren清整棵子树()
        {
            RedDotManager.Mark(GrandChild);
            RedDotManager.SetCount(ChildB, 4);

            RedDotManager.ConsumeWithChildren(Root);

            Assert.IsFalse(RedDotManager.IsActive(Root));
            Assert.IsFalse(RedDotManager.IsActive(ChildA));
            Assert.IsFalse(RedDotManager.IsActive(GrandChild));
            Assert.AreEqual(0, RedDotManager.GetCount(Root));
        }

        [Test]
        public void 未注册键安全返回()
        {
            var missing = new RedDotKey(9999);

            Assert.IsFalse(RedDotManager.IsActive(missing));
            Assert.AreEqual(0, RedDotManager.GetCount(missing));
            Assert.IsFalse(RedDotManager.TryGet(missing, out var active, out var count));
            Assert.IsFalse(active);
            Assert.AreEqual(0, count);

            // 写入未注册键不应抛异常
            Assert.DoesNotThrow(() => RedDotManager.Mark(missing));
            Assert.DoesNotThrow(() => RedDotManager.SetCount(missing, 5));
            Assert.DoesNotThrow(() => RedDotManager.Consume(missing));
        }

        [Test]
        public void 先注册子后注册父_父能接住子()
        {
            RedDotManager.Clear();

            var child = new RedDotKey(2002);
            var parent = new RedDotKey(2001);

            RedDotManager.Register(child, parent, "child");   // 父还没注册
            RedDotManager.Register(parent, default, "parent"); // 父后注册

            RedDotManager.Mark(child);

            Assert.IsTrue(RedDotManager.IsActive(parent), "晚注册的父应回收先注册的子");
        }

        [Test]
        public void 变化通知只在该键聚合值变化时触发()
        {
            var hits = 0;
            var lastKey = default(RedDotKey);
            void Handler(RedDotKey k)
            {
                hits++;
                lastKey = k;
            }

            RedDotManager.OnChanged += Handler;
            try
            {
                RedDotManager.Mark(GrandChild);
                Assert.AreEqual(3, hits, "叶子 + 父 + 根 各一次");
                Assert.AreEqual(Root, lastKey, "最后一次应冒泡到根");

                hits = 0;
                RedDotManager.Mark(GrandChild);   // 重复置位，聚合无变化
                Assert.AreEqual(0, hits, "值未变不应通知");
            }
            finally
            {
                RedDotManager.OnChanged -= Handler;
            }
        }

        [Test]
        public void 状态导出与恢复()
        {
            RedDotManager.SetCount(ChildB, 7);
            RedDotManager.Mark(GrandChild);

            var states = new System.Collections.Generic.List<RedDotState>();
            RedDotManager.ExportState(states);
            Assert.AreEqual(2, states.Count, "只有非空自有值的节点被导出");

            RedDotManager.Clear();
            RedDotManager.Register(Root, default, "root");
            RedDotManager.Register(ChildA, Root, "childA");
            RedDotManager.Register(ChildB, Root, "childB");
            RedDotManager.Register(GrandChild, ChildA, "grand");

            RedDotManager.ImportState(states);

            Assert.AreEqual(7, RedDotManager.GetCount(ChildB));
            Assert.AreEqual(7, RedDotManager.GetCount(Root), "恢复后应重算聚合");
            Assert.IsTrue(RedDotManager.IsActive(GrandChild));
        }
    }
}
