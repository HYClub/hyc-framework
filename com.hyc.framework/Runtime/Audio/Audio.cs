// ============================================================
// HYC Framework - 音频门面
// 文件: Runtime/Audio/Audio.cs
// 说明: 运行时唯一的音频入口。三种后端（框架默认 / Wwise）共用这一套 API，
//       切换后端只换门面背后的实现，业务代码与配置表零改动。
//
// 用法：
//   Audio.Play("clip:Audio/Sfx/Click");       // 直接给引用
//   Audio.Play(cfg.BgmRef, AudioChannelId.Music); // 配置表字段（框架/音频 类型）
//   Audio.SetChannelVolume(AudioChannelId.Music, 0.8f);
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Audio
{
    /// <summary>
    /// 音频门面。静态类，进程内单例后端（与框架内其他 Manager 风格一致）。
    ///
    /// 引用格式：配置表「框架/音频」字段存的是带前缀的字符串，
    /// <c>clip:&lt;Addressables地址&gt;</c> 走默认后端，<c>wwise:&lt;事件名&gt;</c> 走 Wwise。
    /// 前缀决定路由，与当前后端无关 —— 这样同一张配置表在两种后端下都能播。
    /// </summary>
    public static class Audio
    {
        private static IAudio _backend;
        private static AudioBackend _current = AudioBackend.Default;
        private static bool _warnedMissingRef;

        /// <summary>当前后端类型。未初始化时读的是 <see cref="AudioBackend.Default"/>。</summary>
        public static AudioBackend CurrentBackend => _current;

        /// <summary>
        /// 切换后端。会先停止当前播放再释放旧后端，切换失败时保持原后端不变。
        /// </summary>
        /// <param name="backend">目标后端。</param>
        /// <returns>切换是否成功。</returns>
        public static bool SetBackend(AudioBackend backend)
        {
            if (_backend != null && _current == backend) return true;

            var created = CreateBackend(backend);
            if (created == null) return false;

            var old = _backend;
            var oldKind = _current;

            _backend = created;
            _current = backend;

            if (old != null)
            {
                try { old.Dispose(); }
                catch (System.Exception e) { Debug.LogWarning($"[HYC.Audio] 释放旧后端失败: {e.Message}"); }
            }

            Debug.Log($"[HYC.Audio] 后端切换: {oldKind} → {backend}");
            return true;
        }

        private static IAudio CreateBackend(AudioBackend backend)
        {
            switch (backend)
            {
                case AudioBackend.Default:
                    return new DefaultAudio();
                case AudioBackend.Wwise:
#if HYC_WWISE
                    return new WwiseAudio();
#else
                    // 没定义宏也允许切到 Wwise 选项卡：返回桩实例（所有方法明确报错而不静默失败），
                    // 这样音频管理窗口能切到 Wwise 页并提示"未启用"，不必先装好宏。
                    return new WwiseAudio();
#endif
                default:
                    Debug.LogError($"[HYC.Audio] 未知后端: {backend}");
                    return null;
            }
        }

        private static IAudio Ensure()
        {
            if (_backend != null) return _backend;
            SetBackend(AudioBackend.Default);
            return _backend;
        }

        /// <summary>
        /// 播放一个音频引用。引用格式 <c>clip:地址</c> 或 <c>wwise:事件名</c>；
        /// 无前缀时按 clip 处理。自动懒初始化默认后端。
        ///
        /// 若该引用已在音频总览（<see cref="AudioCatalog"/>）登记，则<b>以总览为准</b>：
        /// 用总览里的规范地址，并采用总览登记的音量分组（忽略下面的
        /// <paramref name="channel"/> 参数）。没登记过则原样播放引用里的值。
        /// 要强制指定分组用 <see cref="PlayIn"/>。
        /// </summary>
        /// <param name="audioRef">带前缀的音频引用。</param>
        /// <param name="volume">音量倍率，1 = 原始音量。</param>
        /// <param name="loop">是否循环（Wwise 后端由 Authoring 决定，此参数仅对默认后端生效）。</param>
        /// <param name="channel">音量分组，默认音效。仅当该引用<b>未</b>在总览登记时生效。</param>
        // 注意：默认参数必须是编译期常量，而 AudioChannelId.Sfx 是 static readonly（结构体不能是 const），
        // 故用重载表达默认值，不能写成 `AudioChannelId channel = AudioChannelId.Sfx`（CS1736）。
        public static AudioHandle Play(string audioRef, float volume = 1f, bool loop = false)
            => Play(audioRef, volume, loop, AudioChannelId.Sfx);

        /// <summary>显式指定音量分组的 <see cref="Play"/> 重载。</summary>
        public static AudioHandle Play(string audioRef, float volume, bool loop, AudioChannelId channel)
            => PlayInternal(audioRef, volume, loop, channel, useCatalogChannel: true);

        /// <summary>只传引用与分组的便捷重载（其余参数取默认）。</summary>
        public static AudioHandle Play(string audioRef, AudioChannelId channel)
            => Play(audioRef, 1f, false, channel);

        /// <summary>
        /// 播放一个音频引用，并<b>显式指定</b>音量分组（忽略总览里登记的分组）。
        /// 需要临时把某个音效归到别的组时用这个重载。
        /// </summary>
        public static AudioHandle PlayIn(string audioRef, AudioChannelId channel,
            float volume = 1f, bool loop = false)
            => PlayInternal(audioRef, volume, loop, channel, useCatalogChannel: false);

        private static AudioHandle PlayInternal(string audioRef, float volume, bool loop,
            AudioChannelId channel, bool useCatalogChannel)
        {
            if (string.IsNullOrEmpty(audioRef))
            {
                WarnMissingRef(audioRef, "引用为空");
                return AudioHandle.Invalid;
            }

            var isClip = AudioRefPrefix.TryParse(audioRef, out var target);

            // 查总览：命中就让总览说了算（它存的是规范地址），
            // 查不到就用引用里带的值 —— 这样"手填没登记过的地址"也能播，
            // 不至于因为忘了在窗口里登记就完全没声音。
            var useChannel = channel;
            if (AudioCatalog.TryGetByRef(audioRef, out var entry))
            {
                if (!string.IsNullOrEmpty(entry.Ref)) target = entry.Ref;
                if (useCatalogChannel) useChannel = entry.Channel;
            }

            // 前缀声明的去向与当前后端冲突时，明确报错而不是静默播错东西。
            if (isClip && _current == AudioBackend.Wwise)
            {
                Debug.LogWarning($"[HYC.Audio] 当前是 Wwise 后端，但引用 '{audioRef}' 是 clip: 前缀，已按事件名尝试播放。");
            }
            else if (!isClip && !_warnedMissingRef && _current == AudioBackend.Default)
            {
                Debug.LogWarning($"[HYC.Audio] 引用 '{audioRef}' 是 wwise: 前缀，但当前是框架默认后端。" +
                                 "切到 Wwise 后端后才会真正生效。");
            }

            var backend = Ensure();
            if (backend == null) return AudioHandle.Invalid;
            return backend.Play(target, volume, loop, useChannel);
        }

        /// <summary>停止指定播放实例。</summary>
        public static void Stop(AudioHandle handle) => _backend?.Stop(handle);

        /// <summary>停止某分组全部播放（BGM 切场景时用）。</summary>
        public static void StopChannel(AudioChannelId channel) => _backend?.StopChannel(channel);

        /// <summary>停止全部播放。</summary>
        public static void StopAll() => _backend?.StopAll();

        /// <summary>暂停 / 恢复指定实例。</summary>
        public static void Pause(AudioHandle handle, bool paused = true) => _backend?.Pause(handle, paused);

        /// <summary>设置分组音量（0~1）。实际增益 = Master × 组音量。</summary>
        public static void SetChannelVolume(AudioChannelId channel, float volume) =>
            _backend?.SetChannelVolume(channel, volume);

        /// <summary>读回分组音量。</summary>
        public static float GetChannelVolume(AudioChannelId channel)
        {
            if (channel == AudioChannelId.Master) return _masterVolume;
            return _channelVolumes.TryGetValue(channel.Value, out var v) ? v : 1f;
        }

        // 分组音量在门面侧也留一份，这样没初始化后端时设置界面也能存住用户选择。
        private static readonly Dictionary<int, float> _channelVolumes = new Dictionary<int, float>();
        private static float _masterVolume = 1f;
        private static bool _muted;

        /// <summary>
        /// 设置分组音量（0~1）。门面侧先记一份，未初始化后端也不会丢。
        /// </summary>
        public static void SetVolume(AudioChannelId channel, float volume)
        {
            volume = Mathf.Clamp01(volume);
            if (channel == AudioChannelId.Master) _masterVolume = volume;
            else _channelVolumes[channel.Value] = volume;
            _backend?.SetChannelVolume(channel, volume);
        }

        /// <summary>全局静音。各分组设定值保留，取消静音后恢复。</summary>
        public static void SetMuted(bool muted)
        {
            _muted = muted;
            _backend?.SetMuted(muted);
        }

        /// <summary>当前是否静音。</summary>
        public static bool IsMuted => _muted;

        /// <summary>设置某播放实例的音量倍率（叠加在分组音量之上）。</summary>
        public static void SetVolume(AudioHandle handle, float volume) => _backend?.SetVolume(handle, volume);

        /// <summary>释放后端。常驻运行期不需要调用；切换后端时框架内部会处理。</summary>
        public static void Dispose()
        {
            _backend?.Dispose();
            _backend = null;
            _current = AudioBackend.Default;
        }

        private static void WarnMissingRef(string audioRef, string reason)
        {
            // 空引用在配置里很常见（字段留空），只警告一次避免刷屏。
            if (_warnedMissingRef) return;
            _warnedMissingRef = true;
            Debug.LogWarning($"[HYC.Audio] 跳过播放（{reason}）。后续空引用不再重复警告。");
        }

        /// <summary>清掉"空引用只警告一次"的状态。测试与域重载时用。</summary>
        public static void ResetWarnState() => _warnedMissingRef = false;
    }
}
