using HYC.Framework.Config.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <see cref="ConfigAssetLayout"/>（配置资产「类型文件夹」布局规则）的契约。
    ///
    /// <para><b>为什么这个契约要紧</b>：配置资产按「根目录/类型短名」自动归置（创建时没有就建），
    /// 数据树里 <c>[角色]</c> 那种前缀与文件夹名必须同源——一旦 FolderNameFor 的回退行为变了
    /// （比如丢了 <see cref="ConfigTemplate"/>/<see cref="ConfigEnumDefinition"/> 的固定名），
    /// 新建资产会散落到根目录，而 <see cref="ConfigAssetMoveGuard"/> 的守护判定也会跟着失效。</para>
    ///
    /// <para><b>只测纯函数</b>：不调 <see cref="ConfigAssetLayout.EnsureTypeFolder"/>（会在磁盘建目录）、
    /// 不碰 <c>AssetDatabase</c>。移出守护依赖资产路径查询，留给 Unity 内手工验证。</para>
    /// </summary>
    public class ConfigAssetLayoutTests
    {
        [Test]
        public void FolderNameFor_模板与枚举_用固定文件夹名()
        {
            Assert.AreEqual(ConfigAssetLayout.TemplateFolderName, ConfigAssetLayout.FolderNameFor(typeof(ConfigTemplate)),
                "配置模板的类型文件夹名必须是常量「配置模板」");
            Assert.AreEqual(ConfigAssetLayout.EnumFolderName, ConfigAssetLayout.FolderNameFor(typeof(ConfigEnumDefinition)),
                "枚举定义的类型文件夹名必须是常量「枚举」");
        }

        [Test]
        public void FolderNameFor_非配置类型与空_返回null不归置()
        {
            Assert.IsNull(ConfigAssetLayout.FolderNameFor(null), "null 类型不归置");
            Assert.IsNull(ConfigAssetLayout.FolderNameFor(typeof(ScriptableObject)),
                "非 ConfigBase 派生的 ScriptableObject 不归置（如天赋树走自己的布局）");
        }

        [Test]
        public void TypeFolderPath_一律拼在配置根目录下一层()
        {
            var root = ConfigDataSettings.RootFolder;

            Assert.AreEqual(root + "/" + ConfigAssetLayout.TemplateFolderName,
                ConfigAssetLayout.TypeFolderPath(typeof(ConfigTemplate)),
                "模板类型文件夹 = 根目录/配置模板");
            Assert.AreEqual(root + "/" + ConfigAssetLayout.EnumFolderName,
                ConfigAssetLayout.TypeFolderPath(typeof(ConfigEnumDefinition)),
                "枚举类型文件夹 = 根目录/枚举");
            Assert.IsNull(ConfigAssetLayout.TypeFolderPath(typeof(ScriptableObject)),
                "非配置类型没有类型文件夹");
        }

        [Test]
        public void 未归置判定_空路径安全返回false()
        {
            // 守护只在「资产已在类型文件夹内」时生效；空/ null 路径必须安全返回 false（不能抛）
            Assert.IsFalse(ConfigAssetLayout.IsInItsTypeFolder(null));
            Assert.IsFalse(ConfigAssetLayout.IsInItsTypeFolder(string.Empty));
            Assert.IsFalse(ConfigAssetLayout.TryGetItsTypeFolder(string.Empty, out var folder));
            Assert.IsNull(folder);
        }
    }
}
