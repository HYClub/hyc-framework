using System;
using System.Collections.Generic;
using System.IO;
using HYC.Framework.Audio;
using HYC.Framework.Config;
using HYC.Framework.Config.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// 命名空间 root 切换安全的契约（历史「生成即报 CS0246」事故的回归守护）：
    /// <list type="bullet">
    /// <item>模板资产里存储的引用全名（含 pick 时的旧命名空间）绝不泄漏进生成代码；</item>
    /// <item>生成类引用按「短名 + 当前命名空间」确定性解析、零反射——编译失败期间（旧程序集
    /// 未退出）也能正确生成，破「反射旧类型 → 失效 using → 编译失败 → 旧程序集不退」死循环；</item>
    /// <item>手写 <c>[CfgAsset]</c> 类（无对应模板）仍按真实命名空间引用，不误伤。</item>
    /// </list>
    /// <para>涉模板资产的用例在配置根目录下的临时文件夹里创建/删除，TearDown 保证清理。</para>
    /// </summary>
    public class ConfigNamespaceRuleTests
    {
        private const string TempDirName = "Temp/ConfigNamespaceRuleTests";
        private string _tempFolder;

        [SetUp]
        public void SetUp()
        {
            ConfigDataSettings.EnsureRootFolder();
            _tempFolder = $"{ConfigDataSettings.RootFolder}/{TempDirName}";
            if (AssetDatabase.IsValidFolder(_tempFolder))
                AssetDatabase.DeleteAsset(_tempFolder);
            EnsureFolder(_tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(_tempFolder))
                    AssetDatabase.DeleteAsset(_tempFolder);
            }
            catch (Exception)
            {
                // 清理失败不掩盖测试结果；残留在配置根下的 Temp/... 目录肉眼可辨
            }
        }

        // ---------- 纯函数：短名剥离 / 全名重建 ----------

        [TestCase("StaleOldRoot.config.DropConfig, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", ExpectedResult = "DropConfig")]
        [TestCase("DropConfig, Assembly-CSharp", ExpectedResult = "DropConfig")]
        [TestCase("A.B.C", ExpectedResult = "C")]
        [TestCase("  Spaced.Config  ", ExpectedResult = "Config")]
        public string RefTypeShortName_剥出短名_丢弃命名空间与程序集限定(string storedFullName)
            => ConfigTemplateCodeGen.RefTypeShortName(storedFullName);

        [Test]
        public void RefTypeShortName_空值与纯程序集限定_返回null()
        {
            Assert.IsNull(ConfigTemplateCodeGen.RefTypeShortName(null));
            Assert.IsNull(ConfigTemplateCodeGen.RefTypeShortName(""));
            Assert.IsNull(ConfigTemplateCodeGen.RefTypeShortName("   "));
            Assert.IsNull(ConfigTemplateCodeGen.RefTypeShortName(", Assembly-CSharp"));
        }

        [Test]
        public void 重建全名_换成当前命名空间_保留程序集限定()
        {
            var rebuilt = ConfigTemplateCodeGen.RebuildRefTypeFullName(
                "StaleOldRoot.config.DropConfig, Assembly-CSharp, Version=1.2.3.4");
            Assert.AreEqual(
                $"{ConfigDataSettings.Namespace}.DropConfig, Assembly-CSharp, Version=1.2.3.4",
                rebuilt);
        }

        [Test]
        public void 重建全名_无程序集限定时只换命名空间()
        {
            var rebuilt = ConfigTemplateCodeGen.RebuildRefTypeFullName("StaleOldRoot.config.DropConfig");
            Assert.AreEqual($"{ConfigDataSettings.Namespace}.DropConfig", rebuilt);
        }

        // ---------- 生成类引用：旧命名空间不泄漏（事故核心契约） ----------

        [Test]
        public void 生成类引用_存储全名里的旧命名空间_不泄漏进生成代码()
        {
            // 目标模板：让引用短名命中「生成类」清单（generateCode=true 才会被计入）
            var target = ScriptableObject.CreateInstance<ConfigTemplate>();
            target.className = "NsRuleTargetConfig";
            target.displayName = "测试/目标";
            var targetPath = $"{_tempFolder}/NsRuleTargetConfig.asset";
            AssetDatabase.CreateAsset(target, targetPath);
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);

            // 宿主模板：引用字段存的是 root 切换前的旧全名（旧命名空间 + 旧程序集限定）
            var owner = ScriptableObject.CreateInstance<ConfigTemplate>();
            owner.className = "NsRuleOwnerConfig";
            owner.displayName = "测试/宿主";
            owner.fields = new List<ConfigTemplateField>
            {
                new ConfigTemplateField
                {
                    name = "Drop",
                    type = ConfigFieldType.Reference,
                    refTypeFullName = "StaleOldRoot.config.NsRuleTargetConfig, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
                    // 只导服务器：Reference 整对象不可 blittable，客户端侧选「不导出」绕开 Blob 可行性拦截
                    clientExport = new ConfigFieldExportSetting { mode = ConfigFieldExportMode.None },
                    serverExport = new ConfigFieldExportSetting { mode = ConfigFieldExportMode.Direct },
                },
            };

            var code = ConfigTemplateCodeGen.GenerateCode(owner, out var error);
            Assert.IsNotNull(code, error ?? "GenerateCode 失败");
            StringAssert.Contains("public NsRuleTargetConfig Drop;", code);
            StringAssert.DoesNotContain("StaleOldRoot", code);
            StringAssert.Contains($"namespace {ConfigDataSettings.Namespace}", code);
            // using 只允许出现当前 root 派生的命名空间或框架命名空间，不得夹带旧 root
            foreach (var line in code.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("using ", StringComparison.Ordinal))
                    StringAssert.DoesNotContain("StaleOldRoot", t);
            }
        }

        // ---------- 手写 [CfgAsset] 类：仍按真实命名空间（不误伤回归） ----------

        [Test]
        public void 手写CfgAsset类引用_仍按真实命名空间生成using()
        {
            var owner = ScriptableObject.CreateInstance<ConfigTemplate>();
            owner.className = "NsRuleForeignRefConfig";
            owner.displayName = "测试/外部引用";
            owner.fields = new List<ConfigTemplateField>
            {
                new ConfigTemplateField
                {
                    name = "Catalog",
                    type = ConfigFieldType.Reference,
                    // 框架自带 [CfgAsset] 类，命名空间 HYC.Framework.Audio，无对应模板
                    refTypeFullName = typeof(AudioCatalogAsset).AssemblyQualifiedName,
                    clientExport = new ConfigFieldExportSetting { mode = ConfigFieldExportMode.None },
                    serverExport = new ConfigFieldExportSetting { mode = ConfigFieldExportMode.Direct },
                },
            };

            var code = ConfigTemplateCodeGen.GenerateCode(owner, out var error);
            Assert.IsNotNull(code, error ?? "GenerateCode 失败");
            StringAssert.Contains("public AudioCatalogAsset Catalog;", code);
            StringAssert.Contains("using HYC.Framework.Audio;", code);
        }

        // ---------- 引用目标缺失：生成期清晰报错（替代旧「反射不到才报错」路径） ----------

        [Test]
        public void 引用目标不存在_生成期报错而非产出坏代码()
        {
            var owner = ScriptableObject.CreateInstance<ConfigTemplate>();
            owner.className = "NsRuleGhostRefConfig";
            owner.displayName = "测试/幽灵引用";
            owner.fields = new List<ConfigTemplateField>
            {
                new ConfigTemplateField
                {
                    name = "Ghost",
                    type = ConfigFieldType.Reference,
                    refTypeFullName = "StaleOldRoot.config.NsRuleGhostConfig, Assembly-CSharp",
                    clientExport = new ConfigFieldExportSetting { mode = ConfigFieldExportMode.None },
                    serverExport = new ConfigFieldExportSetting { mode = ConfigFieldExportMode.Direct },
                },
            };

            var code = ConfigTemplateCodeGen.GenerateCode(owner, out var error);
            Assert.IsNull(code);
            StringAssert.Contains("不存在", error ?? "");
        }

        // ---------- 枚举命名空间：平级 {root}.Enum（避免被配置资产过滤器误伤） ----------

        [Test]
        public void 枚举命名空间_由root派生且与配置类命名空间平级()
        {
            Assert.IsTrue(ConfigDataSettings.EnumNamespace.EndsWith(".Enum"), "枚举命名空间须以 .Enum 结尾");
            Assert.AreNotEqual(ConfigDataSettings.Namespace, ConfigDataSettings.EnumNamespace, "枚举与配置类命名空间须不同");
            Assert.IsFalse(ConfigDataSettings.EnumNamespace.StartsWith(ConfigDataSettings.Namespace),
                "枚举命名空间不能是配置命名空间的子命名空间，否则会被 StartWith 过滤器误当成配置类去导出/收集");
        }

        // ---------- 工具 ----------

        /// <summary>逐级创建资产目录（AssetDatabase.CreateFolder 只建一层）。</summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder.Replace('\\', '/')).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
