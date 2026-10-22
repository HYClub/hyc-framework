using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HYC.Framework.Loc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <see cref="LocalizationManager"/> 的「原子提交」契约。
    ///
    /// <para>根因是旧实现「<b>先清空所有静态字段、再读盘</b>」：
    /// 一旦读盘失败（最常见就是路径写错），静态表已经被清空了。
    /// 现在改成「先读进局部变量，全部成功才一次性替换静态状态」。</para>
    ///
    /// <para>本类钉的就是这一条：<b>Reload 失败 ⇒ 现有数据一字不动</b>。
    /// 这里不构造真实 blob（那要走 BlobAssetReference 写入，成本高），
    /// 而是用反射直接摆一份"已加载数据"进静态字段，然后拿三种坏输入去砸 Reload。</para>
    ///
    /// <para><b>为什么必须反射</b>：LocalizationManager 没有 Reset/注入 API，
    /// 数据只由 <c>Reload</c> 从磁盘灌入，而构造合法 blob 的成本远高于本用例的价值。
    /// 代价是要手工跟踪新增的静态字段——新增字段若忘了加进 <see cref="TrackedFields"/>，
    /// 用例之间会互相污染。这条约束已写在下方字段清单的注释里。</para>
    /// </summary>
    public class LocalizationManagerAtomicCommitTests
    {
        /// <summary>
        /// 需要在用例之间保存/还原的静态字段。
        /// <b>新增 LocalizationManager 静态字段时必须补进这里</b>，否则会跨用例污染。
        /// 注意 <c>mTexts</c> 是 public、其余是 private，所以查找必须同时给 Public|NonPublic。
        /// </summary>
        private static readonly FieldInfo[] TrackedFields =
        {
            F("mIDs"), F("mLangs"), F("mTexts"), F("mLang2Index"), F("mKey2Index"),
            F("mConfigLanguage"), F("mSelectedLanguage"), F("mDefaultLanguage"),
            F("mSensitiveWords"),
        };

        private object[] _backup;
        private string _badDir;

        private static FieldInfo F(string name) =>
            typeof(LocalizationManager).GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static void Set(string name, object value) => F(name).SetValue(null, value);

        [SetUp]
        public void SetUp()
        {
            _backup = new object[TrackedFields.Length];
            for (int i = 0; i < TrackedFields.Length; i++)
                _backup[i] = TrackedFields[i].GetValue(null);

            // 摆一份"宿主已加载"的数据：2 个语言 × 3 个 key
            Set("mIDs", new[] { "ui/start", "ui/quit", "ui/score" });
            Set("mLangs", new[] { "en", "zh" });
            Set("mTexts", new[]
            {
                new[] { "Start", "Quit", "Score" },
                new[] { "开始", "退出", "分数" },
            });
            Set("mLang2Index", null);   // 走内部惰性重建，确保测的是真实查询路径
            Set("mKey2Index", null);

            // 语言状态必须钉死：SelectedLanguage 的解析顺序是
            // 「已选 → 默认 → 配置」，宿主若留了 mSelectedLanguage="fr"，
            // 因为 "fr" 不在本表 Langs 里会回落到 ConfigLanguage("en")——
            // 断言就会随宿主状态飘。固定成"未选择 + 配置为 en"，结果唯一。
            Set("mSelectedLanguage", null);
            Set("mDefaultLanguage", null);
            Set("mConfigLanguage", "en");

            _badDir = Path.Combine("Temp", "LocTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < TrackedFields.Length; i++)
                TrackedFields[i].SetValue(null, _backup[i]);

            try
            {
                if (Directory.Exists(_badDir)) Directory.Delete(_badDir, true);
            }
            catch
            {
                // Temp 残留不影响结论
            }
        }

        private void AssertDataIntact(string because)
        {
            CollectionAssert.AreEqual(new[] { "ui/start", "ui/quit", "ui/score" },
                LocalizationManager.IDs, $"IDs 被破坏：{because}");
            CollectionAssert.AreEqual(new[] { "en", "zh" },
                LocalizationManager.Langs, $"Langs 被破坏：{because}");
            Assert.AreEqual(2, LocalizationManager.Texts.Length, $"Texts 被破坏：{because}");
            Assert.AreEqual("Start", LocalizationManager.GetText("ui/start"), $"英文取不到：{because}");
            Assert.AreEqual("开始", LocalizationManager.GetTextByLang("ui/start", "zh"), $"中文取不到：{because}");
        }

        // ────────────────────── 三种坏输入，都必须"原数据一字不动" ──────────────────────

        [Test]
        public void Reload_目录不存在时_保留原数据()
        {
            LogAssert.Expect(LogType.Error, new Regex("Reload 失败"));
            LocalizationManager.Reload(Path.Combine(_badDir, "根本不存在"));

            AssertDataIntact("目录不存在");
        }

        [Test]
        public void Reload_传null时_保留原数据()
        {
            // 调用方拼路径拼出 null 是很常见的（配置项没填）
            LogAssert.Expect(LogType.Error, new Regex("Reload 失败"));
            LocalizationManager.Reload(null);

            AssertDataIntact("传 null");
        }

        [Test]
        public void Reload_空目录无id文件时_保留原数据()
        {
            Directory.CreateDirectory(_badDir);
            LogAssert.Expect(LogType.Error, new Regex("没有可用的"));
            LocalizationManager.Reload(_badDir);

            AssertDataIntact("空目录");
        }

        [Test]
        public void Reload_任意失败路径_都不抛异常()
        {
            Directory.CreateDirectory(_badDir);
            LogAssert.Expect(LogType.Error, new Regex("没有可用的"));

            // Reload 是启动链路上的调用点，抛异常会让整个引导链断掉
            Assert.DoesNotThrow(() => LocalizationManager.Reload(_badDir));
        }

        [Test]
        public void Reload_失败时不触发语言变更回调()
        {
            // Reload 成功末尾会调 RefreshAll 广播 onLanguageChanged。
            // 失败路径若也广播，会让所有 LocalizedBase 重刷一次——白刷且可能触发副作用。
            int fired = 0;
            Action handler = () => fired++;
            LocalizationManager.onLanguageChanged += handler;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("Reload 失败"));
                LocalizationManager.Reload(Path.Combine(_badDir, "不存在"));

                Assert.AreEqual(0, fired, "Reload 失败不应广播语言变更");
            }
            finally
            {
                LocalizationManager.onLanguageChanged -= handler;
            }
        }

        // ────────────────────── 失败后索引也应保持可重建 ──────────────────────

        [Test]
        public void Reload失败后_查询路径依然自愈()
        {
            LogAssert.Expect(LogType.Error, new Regex("Reload 失败"));
            LocalizationManager.Reload(Path.Combine(_badDir, "不存在"));

            // 额外验证：即使索引处于 null，惰性重建也能正常工作
            //（不能因为一次失败就永久失能）
            Set("mLang2Index", null);
            Set("mKey2Index", null);

            Assert.IsTrue(LocalizationManager.HasKey("ui/quit"));
            Assert.AreEqual("Quit", LocalizationManager.GetText("ui/quit"));
        }

        // ────────────────────── 查询层的既有契约 ──────────────────────

        [Test]
        public void 缺失key_返回可辨识的占位而非空串()
        {
            // 占位串带 key 本身，是排查"文案没出来"时唯一的线索
            var r = LocalizationManager.GetText("ui/根本没这个key");
            StringAssert.Contains("ui/根本没这个key", r);
            Assert.IsNotEmpty(r);
        }

        [Test]
        public void 空条目_回退到key末段而非返回空串()
        {
            // 语言文件里该 key 的译文为空时，用 "[" + key 末段 + "]" 占位
            Set("mTexts", new[]
            {
                new[] { "Start", string.Empty, "Score" },
                new[] { "开始", "退出", "分数" },
            });

            Assert.AreEqual("[quit]", LocalizationManager.GetTextByLang("ui/quit", "en"));
        }

        [Test]
        public void GetText_空key_返回空串而不抛异常()
        {
            // LocalizedBase 在 Key 为空时会跳过刷新，但空 Key 也可能从别处传进来
            Assert.AreEqual(string.Empty, LocalizationManager.GetText(null));
            Assert.AreEqual(string.Empty, LocalizationManager.GetText(""));
        }

        [Test]
        public void 未加载任何数据时_访问器不返回null()
        {
            // 这批访问器是 UI 的第一道防线，返回 null 会在渲染时才炸
            Set("mIDs", null);
            Set("mLangs", null);
            Set("mTexts", null);
            Set("mSensitiveWords", null);
            Set("mKey2Index", null);
            Set("mLang2Index", null);

            Assert.IsNotNull(LocalizationManager.IDs);
            Assert.IsNotNull(LocalizationManager.Langs);
            Assert.IsNotNull(LocalizationManager.Texts);
            Assert.IsNotNull(LocalizationManager.SensitiveWords);
            Assert.IsEmpty(LocalizationManager.IDs);
            Assert.IsEmpty(LocalizationManager.Texts);
            Assert.IsFalse(LocalizationManager.HasKey("any"));
        }
    }
}