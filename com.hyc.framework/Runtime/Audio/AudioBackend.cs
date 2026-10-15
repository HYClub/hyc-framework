// ============================================================
// HYC Framework - 音频后端枚举与分组
// 文件: Runtime/Audio/AudioBackend.cs
// 说明: 音频后端选择 + 音量分组。三种后端共用一套 Audio.xxx 门面，
//       切换后端只换门面背后的实现，业务代码与配置表零改动。
// ============================================================

using System;

namespace HYC.Framework.Audio
{
    /// <summary>音频后端类型。Unity 与 Addressable 已合并为 <see cref="Default"/>（都走 AA）。</summary>
    public enum AudioBackend
    {
        /// <summary>框架默认：Addressables 载 AudioClip + 池化 AudioSource。Unity 原生模式也走这里。</summary>
        Default = 0,

        /// <summary>Wwise 中间件。仅在工程装了 Wwise 时可用（宏 HYC_WWISE）。</summary>
        Wwise = 1,
    }

    /// <summary>
    /// 音量分组标识。原本是写死的枚举，现改为<b>配置驱动</b>：分组集合存于
    /// <see cref="HYC.Framework.Runtime.FrameworkSettings.audioGroups"/>（HYC Settings 的 Audio/Groups 里可增删改名），
    /// 每个分组有一个稳定 int id。<c>Master = id 0</c> 固定为全局总音量（作用于所有组，不可删），
    /// 其余分组由配置决定。
    ///
    /// 用 readonly struct 包一层 int 保类型安全（不能把任意 int 当通道传），同时保留隐式 int 转换方便与旧数据兼容。
    /// 旧 catalog / Blob 里存的 0~4 整数仍对应默认 5 个分组（id 不变）。
    /// </summary>
    public readonly struct AudioChannelId : System.IEquatable<AudioChannelId>
    {
        /// <summary>分组 id（Master = 0）。</summary>
        public readonly int Value;

        public AudioChannelId(int value) { Value = value; }

        /// <summary>主音量（全局倍率，作用于所有组）。</summary>
        public static readonly AudioChannelId Master = new AudioChannelId(0);
        /// <summary>背景音乐（默认 id 1）。</summary>
        public static readonly AudioChannelId Music = new AudioChannelId(1);
        /// <summary>音效（默认 id 2）。</summary>
        public static readonly AudioChannelId Sfx = new AudioChannelId(2);
        /// <summary>语音 / 对话（默认 id 3）。</summary>
        public static readonly AudioChannelId Voice = new AudioChannelId(3);
        /// <summary>界面反馈音（默认 id 4）。</summary>
        public static readonly AudioChannelId Ui = new AudioChannelId(4);

        public static implicit operator int(AudioChannelId id) => id.Value;
        public static implicit operator AudioChannelId(int v) => new AudioChannelId(v);

        public bool Equals(AudioChannelId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AudioChannelId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    /// <summary>一个音量分组的配置定义（存于 <see cref="HYC.Framework.Runtime.FrameworkSettings.audioGroups"/>）。</summary>
    [System.Serializable]
    public sealed class AudioGroupDef
    {
        /// <summary>稳定 id（Master 固定为 0，不可改）。</summary>
        public int id;
        /// <summary>显示名（可改名，如本地化）。</summary>
        public string displayName = "";
        /// <summary>项目级默认音量（0~1），出包由 Bootstrap 应用。</summary>
        public float defaultVolume = 1f;
    }

    /// <summary>音频引用前缀。配置表字段存 "前缀:值"，一个字段同时表达两路后端。</summary>
    public static class AudioRefPrefix
    {
        /// <summary>Addressables 地址前缀：<c>clip:Assets/Audio/Foo.wav</c>。</summary>
        public const string Clip = "clip:";

        /// <summary>Wwise 事件前缀：<c>wwise:Play_Foo</c>。</summary>
        public const string Wwise = "wwise:";

        /// <summary>
        /// 解析配置字段里的音频引用，返回前缀类型与剩余值。
        /// 无前缀时按 <see cref="Clip"/> 处理（默认后端）。
        /// </summary>
        /// <param name="value">配置字段里的原始字符串，允许为空。</param>
        /// <param name="target">输出：去掉前缀后的地址或事件名。</param>
        /// <returns>true = clip（默认后端）；false = wwise；value 为空时返回 false 且 target 为空。</returns>
        public static bool TryParse(string value, out string target)
        {
            target = string.Empty;
            if (string.IsNullOrEmpty(value)) return false;

            var v = value.Trim();
            if (v.StartsWith(Wwise, StringComparison.Ordinal))
            {
                target = v.Substring(Wwise.Length).Trim();
                return false;
            }

            if (v.StartsWith(Clip, StringComparison.Ordinal))
                v = v.Substring(Clip.Length).Trim();

            target = v;
            return true;
        }

        /// <summary>把地址或事件名包装成带前缀的引用字符串。</summary>
        /// <param name="target">Addressables 地址或 Wwise 事件名。</param>
        /// <param name="wwise">true = 包装成 wwise 引用；false = 包装成 clip 引用。</param>
        public static string Wrap(string target, bool wwise)
            => (wwise ? Wwise : Clip) + (target ?? string.Empty);
    }
}
