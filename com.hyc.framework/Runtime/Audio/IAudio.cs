// ============================================================
// HYC Framework - 音频后端契约
// 文件: Runtime/Audio/IAudio.cs
// 说明: 三种后端实现的统一契约。门面 Audio 只认这个接口，
//       新增后端（FMOD / 腾讯音 / 自研）实现它即可接入，业务代码不改。
// ============================================================

namespace HYC.Framework.Audio
{
    /// <summary>一次播放的句柄。调用方持有它才能停止/调音量；丢弃即视为" fire and forget"。</summary>
    public readonly struct AudioHandle
    {
        /// <summary>后端内部标识（播放实例 id 或池位索引）。0 = 无效。</summary>
        public readonly int Id;

        public AudioHandle(int id) { Id = id; }

        /// <summary>是否是一个有效句柄。</summary>
        public bool IsValid => Id != 0;

        public static readonly AudioHandle Invalid = new AudioHandle(0);
    }

    /// <summary>音频后端契约。实现类会被 <see cref="Audio"/> 门面按当前后端选择实例化。</summary>
    public interface IAudio
    {
        /// <summary>播放一个地址（Addressables key）。</summary>
        /// <param name="address">Addressables 地址。</param>
        /// <param name="volume">音量倍率，1 = 原始音量。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="channel">所属音量分组。</param>
        /// <returns>播放句柄，失败返回 <see cref="AudioHandle.Invalid"/>。</returns>
        AudioHandle Play(string address, float volume, bool loop, AudioChannelId channel);

        /// <summary>停止指定播放实例。</summary>
        void Stop(AudioHandle handle);

        /// <summary>停止某个分组下的全部播放（BGM 切场景时用）。</summary>
        void StopChannel(AudioChannelId channel);

        /// <summary>停止全部播放。</summary>
        void StopAll();

        /// <summary>暂停 / 恢复指定实例。</summary>
        void Pause(AudioHandle handle, bool paused);

        /// <summary>设置分组音量。实际增益 = Master × 组音量。</summary>
        void SetChannelVolume(AudioChannelId channel, float volume);

        /// <summary>设置某实例的音量倍率（叠加在分组音量之上）。</summary>
        void SetVolume(AudioHandle handle, float volume);

        /// <summary>全局静音。静音时所有分组音量置零，但各自设定值保留。</summary>
        void SetMuted(bool muted);

        /// <summary>释放后端持有的所有资源（句柄全部失效）。</summary>
        void Dispose();
    }
}
