using HYC.Framework.Attr;
using NUnit.Framework;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <see cref="AttributeKey"/> 的哈希契约。
    ///
    /// 为什么值得钉：属性 key 哈希会<b>烘进生成代码</b>（<c>AttrKeyMap.Hash</c> 数组），
    /// 也用于运行期按名查找。一旦算法或偏移改了，历史上用旧哈希烘出来的
    /// 数据资产会静默查不到值——没有异常、没有报错，只是数值变成默认值。
    /// 所以这里的断言是「锁定具体数值」，不是「锁定性质」。
    /// </summary>
    public class AttributeKeyHashTests
    {
        [Test]
        public void Hash_空字符串与null_返回FNV偏移基()
        {
            // FNV-1a 64 的 offset basis
            const ulong FnvOffsetBasis = 14695981039346656037UL;

            Assert.AreEqual(FnvOffsetBasis, AttributeKey.Hash(null));
            Assert.AreEqual(FnvOffsetBasis, AttributeKey.Hash(""),
                "空串不进入循环，结果应等于 offset basis");
        }

        [Test]
        public void Hash_空串与null_结果相同()
        {
            Assert.AreEqual(AttributeKey.Hash(null), AttributeKey.Hash(""));
        }

        [Test]
        public void Hash_不同key_结果不同()
        {
            Assert.AreNotEqual(AttributeKey.Hash("ATK"), AttributeKey.Hash("DEF"));
            Assert.AreNotEqual(AttributeKey.Hash("HP"), AttributeKey.Hash("HP "),
                "末尾空格是不同 key，不应碰撞");
        }

        [Test]
        public void Hash_相同key_结果稳定()
        {
            // 稳定性：同名必须恒等，否则运行期袋数据会错位。
            var a = AttributeKey.Hash("Hero/ATK");
            for (var i = 0; i < 100; i++)
                Assert.AreEqual(a, AttributeKey.Hash("Hero/ATK"), "第 {0} 次", i);
        }

        [Test]
        public void Hash_锁定FNV1a数值_防止算法被无声改动()
        {
            // 标准 FNV-1a 64 手算值。这些数字一旦变化，说明算法/编码被改，
            // 生成的 AttrKeyMap.Hash 与历史数据资产将不再兼容。
            Assert.AreEqual(0xcbf29ce484222325UL, AttributeKey.Hash(""),
                "空串 = offset basis（已锁定）");

            // "a" 的 FNV-1a 64：basis ^ 0x61，再乘 FNV prime
            const ulong FnvPrime = 1099511628211UL;
            var expectedA = unchecked((14695981039346656037UL ^ (byte)'a') * FnvPrime);
            Assert.AreEqual(expectedA, AttributeKey.Hash("a"));

            // "a" 与 "b" 应相差 prime 的差值（异或同一位置后的乘性差异）
            Assert.AreNotEqual(AttributeKey.Hash("a"), AttributeKey.Hash("b"));
        }
    }
}