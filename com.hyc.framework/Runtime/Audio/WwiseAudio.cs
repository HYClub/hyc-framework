// ============================================================
// HYC Framework - Wwise 音频后端
// 文件: Runtime/Audio/WwiseAudio.cs
// 说明: 提前写好的 Wwise 实现，整个类体用 HYC_WWISE 宏包裹。
//       工程没装 Wwise 时宏未定义 → 本文件编译出空壳，不引用任何 Wwise 类型，
//       因此框架包本身不需要 Wwise 依赖就能编译通过。
//
// 宏怎么来（两条路，先到先得）：
//   1. asmdef versionDefines：装了 UPM 形式的 com.audiokinetic.wwise 时自动定义。
//   2. 手动：Project Settings → Player → Scripting Define Symbols 添加 HYC_WWISE。
//      新版 Wwise Unity Integration 是 .unitypackage 导入到 Assets/Wwise/（无 package.json），
//      versionDefines 侦测不到，必须走这条手动路。
//      编辑器的音频管理窗口会在检测到 Assets/Wwise 时提示你加这个宏。
// ============================================================

// using 必须放在 #if 外面：#else 分支（未装 Wwise 时）也要用 UnityEngine.Debug。
// 放在宏内会导致未定义宏时 Debug 找不到（CS0103）。
using UnityEngine;
#if HYC_WWISE
using System.Collections.Generic;
#endif

namespace HYC.Framework.Audio
{
    /// <summary>
    /// Wwise 中间件后端。与 <see cref="DefaultAudio"/> 实现同一个 <see cref="IAudio"/> 契约，
    /// 因此 <see cref="Audio"/> 门面切后端时业务代码零改动。
    ///
    /// 未定义 HYC_WWISE 时本类仍存在（保证 <see cref="Audio.SetBackend"/> 可编译），
    /// 但所有方法都会明确报错而不是静默失败——切换后端被 UI 置灰，
    /// 走到这里说明是代码直接调了，属于要暴露的编程错误。
    /// </summary>
    public sealed class WwiseAudio : IAudio
    {
#if HYC_WWISE
        // Wwise 的 PostEvent 返回 playingID，用它做停止/暂停的句柄。
        private readonly Dictionary<int, int> _playing = new Dictionary<int, int>(64);
        private readonly Dictionary<int, float> _channelVolume = new Dictionary<int, float>();
        private bool _muted;
        private bool _disposed;

        public WwiseAudio()
        {
        }

        private float EffectiveVolume(AudioChannelId channel, float voiceVolume)
        {
            if (_muted) return 0f;
            var v = _channelVolume.TryGetValue(0, out var m) ? m : 1f;
            if (channel.Value != 0) v *= _channelVolume.TryGetValue(channel.Value, out var g) ? g : 1f;
            return v * voiceVolume;
        }

        public AudioHandle Play(string eventName, float volume, bool loop, AudioChannelId channel)
        {
            if (_disposed || string.IsNullOrEmpty(eventName)) return AudioHandle.Invalid;
            // Wwise 的循环由 Authoring 侧的 Event 定义决定，代码里的 loop 参数不生效；
            // 传入 loop 仅用于让门面 API 在各后端间保持一致。
            var playingId = AkSoundEngine.PostEvent(eventName);
            if (playingId == AkSoundEngine.INVALID_PLAYING_ID) return AudioHandle.Invalid;

            _playing[playingId] = channel.Value;
            AkSoundEngine.SetRTPCValue(AudioRtcpId(channel), EffectiveVolume(channel, volume));
            return new AudioHandle(playingId);
        }

        /// <summary>分组音量通过 RTPC 下发给 Wwise，名字与 AudioManagerWindow 中展示的一致。</summary>
        private static string AudioRtcpId(AudioChannelId channel) => "HYC_Volume_" + channel.Value;

        public void Stop(AudioHandle handle)
        {
            if (!_playing.ContainsKey(handle.Id)) return;
            AkSoundEngine.StopEvent(handle.Id);
            _playing.Remove(handle.Id);
        }

        public void StopChannel(AudioChannelId channel)
        {
            if (channel == AudioChannelId.Master) { StopAll(); return; }

            // Wwise 没有"按组停"的原生 API，遍历本后端记录自己发的事件。
            var toRemove = new List<int>();
            foreach (var kv in _playing)
            {
                if (kv.Value != channel.Value) continue;
                AkSoundEngine.StopEvent(kv.Key);
                toRemove.Add(kv.Key);
            }
            for (int i = 0; i < toRemove.Count; i++) _playing.Remove(toRemove[i]);
        }

        public void StopAll()
        {
            foreach (var kv in _playing) AkSoundEngine.StopEvent(kv.Key);
            _playing.Clear();
        }

        public void Pause(AudioHandle handle, bool paused)
        {
            if (!_playing.ContainsKey(handle.Id)) return;
            AkSoundEngine.PauseEvent(handle.Id, paused);
        }

        public void SetChannelVolume(AudioChannelId channel, float volume)
        {
            _channelVolume[channel.Value] = Mathf.Clamp01(volume);
            if (channel == AudioChannelId.Master)
            {
                // Master 变化要重算所有分组。
                foreach (var kv in _playing)
                    AkSoundEngine.SetRTPCValue(AudioRtcpId(kv.Value), EffectiveVolume(kv.Value, 1f));
                return;
            }
            AkSoundEngine.SetRTPCValue(AudioRtcpId(channel), EffectiveVolume(channel, 1f));
        }

        public void SetVolume(AudioHandle handle, float volume)
        {
            if (!_playing.TryGetValue(handle.Id, out var channel)) return;
            AkSoundEngine.SetRTPCValue(AudioRtcpId(channel), EffectiveVolume(channel, volume));
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            AkSoundEngine.PostEvent("HYC_ApplyMute");
            SetChannelVolume(AudioChannelId.Master, _channelVolume.TryGetValue(0, out var m) ? m : 1f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAll();
        }
#else
        private const string NotAvailable =
            "[HYC.Audio] Wwise 后端未启用：编译宏 HYC_WWISE 未定义。" +
            "请在 Project Settings → Player → Scripting Define Symbols 添加 HYC_WWISE" +
            "（并确认工程已导入 Wwise Unity Integration）。";

        public AudioHandle Play(string eventName, float volume, bool loop, AudioChannelId channel)
        {
            Debug.LogError(NotAvailable);
            return AudioHandle.Invalid;
        }

        public void Stop(AudioHandle handle) => Debug.LogError(NotAvailable);
        public void StopChannel(AudioChannelId channel) => Debug.LogError(NotAvailable);
        public void StopAll() => Debug.LogError(NotAvailable);
        public void Pause(AudioHandle handle, bool paused) => Debug.LogError(NotAvailable);
        public void SetChannelVolume(AudioChannelId channel, float volume) => Debug.LogError(NotAvailable);
        public void SetVolume(AudioHandle handle, float volume) => Debug.LogError(NotAvailable);
        public void SetMuted(bool muted) => Debug.LogError(NotAvailable);
        public void Dispose() { }
#endif
    }
}
