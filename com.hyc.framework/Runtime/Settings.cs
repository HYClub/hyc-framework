using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HYC.Framework.Audio;
using HYC.Framework.UI;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Lightweight global settings container stored as a ScriptableObject.
    /// The framework ships the base record; games add fields for their own
    /// toggles (input remap, V-Sync, target framerate, audio volumes, etc.).
    /// </summary>
    public sealed class FrameworkSettings : ScriptableObject
    {
        public string defaultLanguage = "Default";
        public int targetFramerate = 60;
        public bool vSync = true;
        public int remoteLogLevel = 2;

        /// <summary>项目级音频后端。编辑器在 HYC Settings 里设定并持久化到 Resources 资产，运行时由 bootstrap 应用。</summary>
        public AudioBackend audioBackend = AudioBackend.Default;

        // ---- 音频分组（数据驱动，项目级，出包生效；在 HYC Settings 的 Audio 配置）----
        /// <summary>
        /// 音量分组表。原本写死的 Master/Music/Sfx/Voice/Ui 枚举改为可配置：
        /// 分组集合与每组的默认音量都存这里，HYC Settings 的 Audio/Groups 可增删改名，
        /// Audio/Volumes 改每组默认音量。id 0 = Master 固定为全局总音量（作用于所有组，不可删），
        /// 其余分组 id 由配置决定。旧 catalog / Blob 里存的 0~4 整数仍对应默认 5 组（id 不变）。
        /// </summary>
        public List<AudioGroupDef> audioGroups = new List<AudioGroupDef>
        {
            new AudioGroupDef { id = 0, displayName = "主音量", defaultVolume = 1f },
            new AudioGroupDef { id = 1, displayName = "音乐",   defaultVolume = 1f },
            new AudioGroupDef { id = 2, displayName = "音效",   defaultVolume = 1f },
            new AudioGroupDef { id = 3, displayName = "语音",   defaultVolume = 1f },
            new AudioGroupDef { id = 4, displayName = "界面",   defaultVolume = 1f },
        };
        /// <summary>全局静音（各分组设定保留，取消静音后恢复）。</summary>
        public bool audioMuted = false;

        /// <summary>通道推断规则表：扫描 AudioClip 时按资源路径/名称里的关键词匹配分组。首命中优先，无命中回退 Sfx（id 2）。在 HYC Settings 的 Audio/Inference 编辑。</summary>
        public List<AudioChannelRule> audioChannelRules = new List<AudioChannelRule>
        {
            new AudioChannelRule { keyword = "bgm", channel = AudioChannelId.Music },
            new AudioChannelRule { keyword = "music", channel = AudioChannelId.Music },
            new AudioChannelRule { keyword = "ui", channel = AudioChannelId.Ui },
            new AudioChannelRule { keyword = "voice", channel = AudioChannelId.Voice },
            new AudioChannelRule { keyword = "dialog", channel = AudioChannelId.Voice },
        };

        /// <summary>嵌套可序列化结构：一条「关键词 → 分组」推断规则，编辑器扫描音频时自动归类用。</summary>
        [Serializable]
        public class AudioChannelRule
        {
            /// <summary>资源路径/名称里需包含的关键词（大小写不敏感）。</summary>
            public string keyword = "";
            /// <summary>命中后归到的音量分组 id（见 <see cref="audioGroups"/>，0 = Master）。</summary>
            public int channel = AudioChannelId.Sfx;
        }

        // ---- UI 层级（数据驱动，项目级；在 HYC Settings → UI/层级 增删改名、拖动排序）----

        /// <summary>
        /// UI 层表。<b>顺序即层级</b>（下标 0 在最底，越往后越靠上）：决定 Canvas 的 sortingOrder
        /// 与 Hierarchy 里的兄弟顺序。三枚行为标记（<see cref="UILayerDef.hud"/> /
        /// <see cref="UILayerDef.blockInput"/> / <see cref="UILayerDef.hideOnHideAll"/>）替代了
        /// UIManager 里原先按层名写死的判断，改名 / 插层后群体行为跟随层本身。
        /// 需要编译期常量时，用设置页「保存并生成」产出的 <c>{命名空间root}.ui.UILayer</c> 静态类。
        /// </summary>
        public List<UILayerDef> uiLayers = UILayerTable.DefaultTable();

        private static FrameworkSettings _instance;

        public static FrameworkSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    // 优先读 Resources 里编辑器写出的持久化资产；没有则用代码默认值（框架默认后端）。
                    _instance = Resources.Load<FrameworkSettings>("FrameworkSettings");
                    if (_instance == null)
                        _instance = ScriptableObject.CreateInstance<FrameworkSettings>();
                }
                return _instance;
            }
        }

        /// <summary>Parses command-line switches like <c>--framerate=120</c> / <c>--language=English</c>.</summary>
        public void ApplyCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                var arg = args[i];
                var value = args[i + 1];
                switch (arg.ToLowerInvariant())
                {
                    case "--framerate":
                        if (int.TryParse(value, out var fr)) targetFramerate = fr;
                        break;
                    case "--language":
                        defaultLanguage = value;
                        break;
                    case "--v-sync":
                        if (bool.TryParse(value, out var vs)) vSync = vs;
                        break;
                }
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    /// <summary>Alias for <see cref="FrameworkSettings"/>, kept small for discoverability.</summary>
    public static class GameSettings
    {
        public static FrameworkSettings Current => FrameworkSettings.Instance;
    }
}