// ============================================================
// HYC Framework - 框架默认音频后端
// 文件: Runtime/Audio/DefaultAudio.cs
// 说明: Addressables 载 AudioClip + 池化 AudioSource。Unity 原生模式与框架模式
//       合并到这一份实现（都走 AA），区别只在配置字段里存的是 clip: 还是 wwise:。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HYC.Framework.Audio
{
    /// <summary>
    /// 框架默认音频后端。全部资源经 Addressables 加载，AudioSource 走对象池复用。
    ///
    /// 句柄生命周期约定（与 UI 的 <c>Addressables.Release</c> 语义一致）：
    /// 播放句柄只是"池位 + 播放实例"，<b>不拥有</b> AudioClip 的引用计数。
    /// AudioClip 一旦加载就常驻缓存，直到 <see cref="Dispose"/> 才统一 Release，
    /// 避免"第二次播放同一音效要重新加载"的卡顿。
    /// </summary>
    public sealed class DefaultAudio : IAudio
    {
        private sealed class ClipEntry
        {
            public AsyncOperationHandle<AudioClip> Handle;
            public AudioClip Clip;
            public bool Loading;
        }

        private sealed class Voice
        {
            public AudioSource Source;
            public string Address;
            public int Channel;
            public float Volume = 1f;
            public bool Paused;
            public bool Playing;
            public int Generation;
        }

        /// <summary>池初始容量，按通道各分一份。</summary>
        private const int PoolPerChannel = 8;

        private readonly Dictionary<string, ClipEntry> _clips = new Dictionary<string, ClipEntry>(64);
        private readonly List<Voice> _voices = new List<Voice>(32);
        private readonly Dictionary<int, float> _channelVolume = new Dictionary<int, float>();
        private GameObject _root;
        private bool _disposed;
        private bool _muted;
        private int _generation;

        public DefaultAudio()
        {
            EnsureRoot();
        }

        /// <summary>根节点名，便于在 Hierarchy 里定位与排查。</summary>
        private const string RootName = "[HYC.Audio]";

        private void EnsureRoot()
        {
            if (_root != null) return;

            // 编辑期**不建**常驻 GO：音频只在运行期播；编辑期一旦建了就会污染场景 ——
            // 每 new 一个 DefaultAudio（切后端时必然发生）就在 Hierarchy 里留一个 [HYC.Audio]，切几次堆几个。
            // 运行期（含 Play 模式切后端）照常创建，Dispose 时销毁，始终最多一个。
            if (!Application.isPlaying) return;

            // 用 HideFlags 避免污染用户场景；不用 Find 搜索，避免误命中同名对象。
            _root = new GameObject(RootName);
            _root.hideFlags = HideFlags.DontSave;
            UnityEngine.Object.DontDestroyOnLoad(_root);
        }

        private Voice AllocVoice(AudioChannelId channel)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (!_voices[i].Playing) return _voices[i];
            }

            // 运行期兜底重建根节点；编辑期无宿主 → 返回 null（Play 侧按 Invalid 处理，不 NRE）。
            EnsureRoot();
            if (_root == null) return null;

            var src = _root.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            var v = new Voice { Source = src };
            _voices.Add(v);
            return v;
        }

        private float EffectiveVolume(AudioChannelId channel, float voiceVolume)
        {
            if (_muted) return 0f;
            var v = _channelVolume.TryGetValue(0, out var m) ? m : 1f;
            if (channel.Value != 0) v *= _channelVolume.TryGetValue(channel.Value, out var g) ? g : 1f;
            return v * voiceVolume;
        }

        public AudioHandle Play(string address, float volume, bool loop, AudioChannelId channel)
        {
            if (_disposed || string.IsNullOrEmpty(address)) return AudioHandle.Invalid;

            var clip = ResolveClip(address);
            if (clip == null) return AudioHandle.Invalid; // 还在加载，本次静默跳过

            var voice = AllocVoice(channel);
            if (voice == null || voice.Source == null) return AudioHandle.Invalid;

            voice.Address = address;
            voice.Channel = channel.Value;
            voice.Volume = volume;
            voice.Paused = false;
            voice.Playing = true;
            voice.Generation = ++_generation;

            var src = voice.Source;
            src.clip = clip;
            src.loop = loop;
            src.volume = EffectiveVolume(channel, volume);
            src.Play();

            return new AudioHandle(voice.Generation);
        }

        /// <summary>
        /// 解析地址对应的 AudioClip。首次调用发起异步加载并立即返回 null，
        /// 加载完成后同一地址的后续播放即可命中缓存。
        /// </summary>
        private AudioClip ResolveClip(string address)
        {
            if (_clips.TryGetValue(address, out var entry))
            {
                if (entry.Loading) return null;
                if (entry.Clip == null)
                {
                    // 加载失败（地址错/资源缺失），清掉条目让下次可以重试。
                    ReleaseEntry(entry);
                    _clips.Remove(address);
                }
                return entry.Clip;
            }

            entry = new ClipEntry { Loading = true };
            _clips.Add(address, entry);
            var op = Addressables.LoadAssetAsync<AudioClip>(address);
            entry.Handle = op;
            op.Completed += h =>
            {
                // 后端已 Dispose：立刻还引用计数，避免泄漏。
                if (_disposed)
                {
                    if (h.Status == AsyncOperationStatus.Succeeded && h.Result != null)
                        Addressables.Release(h);
                    return;
                }
                entry.Loading = false;
                if (h.Status == AsyncOperationStatus.Succeeded)
                    entry.Clip = h.Result;
                else
                    Debug.LogWarning($"[HYC.Audio] 加载失败: {address}");
            };
            return null;
        }

        private void ReleaseEntry(ClipEntry entry)
        {
            if (entry.Loading) return;
            if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
            entry.Clip = null;
            entry.Handle = default;
        }

        private Voice Find(int generation)
        {
            if (generation == 0) return null;
            for (int i = 0; i < _voices.Count; i++)
                if (_voices[i].Generation == generation && _voices[i].Playing) return _voices[i];
            return null;
        }

        public void Stop(AudioHandle handle)
        {
            var v = Find(handle.Id);
            if (v == null) return;
            v.Source.Stop();
            v.Source.clip = null;
            v.Playing = false;
            v.Address = null;
        }

        public void StopChannel(AudioChannelId channel)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (!v.Playing || v.Channel != channel.Value) continue;
                v.Source.Stop();
                v.Source.clip = null;
                v.Playing = false;
                v.Address = null;
            }
        }

        public void StopAll() => StopChannel(AudioChannelId.Master);

        public void Pause(AudioHandle handle, bool paused)
        {
            var v = Find(handle.Id);
            if (v == null || v.Paused == paused) return;
            v.Paused = paused;
            if (paused) v.Source.Pause();
            else v.Source.UnPause();
        }

        public void SetChannelVolume(AudioChannelId channel, float volume)
        {
            _channelVolume[channel.Value] = Mathf.Clamp01(volume);
            // Master 变化要刷新所有实例；分组变化只刷本组。
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (!v.Playing) continue;
                if (channel.Value != 0 && v.Channel != channel.Value) continue;
                v.Source.volume = EffectiveVolume(v.Channel, v.Volume);
            }
        }

        public void SetVolume(AudioHandle handle, float volume)
        {
            var v = Find(handle.Id);
            if (v == null) return;
            v.Volume = volume;
            v.Source.volume = EffectiveVolume(v.Channel, v.Volume);
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (!v.Playing) continue;
                v.Source.volume = EffectiveVolume(v.Channel, v.Volume);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Source != null) _voices[i].Source.Stop();
            }
            _voices.Clear();

            foreach (var kv in _clips) ReleaseEntry(kv.Value);
            _clips.Clear();

            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }
        }
    }
}
