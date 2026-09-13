using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Timeline
{
    /// <summary>One cutscene/clip instance driven by a config id.</summary>
    public sealed class Cutscene
    {
        public long Id;
        public string Name;
        public string AssetKey;          // Addressable key of the TimelineAsset / prefab
        public bool Loop;
        public int Priority;             // higher wins when interrupting a running clip
        public Action OnFinished;        // per-clip completion callback
    }

    /// <summary>
    /// Base runtime for config-driven cutscenes. Owns a queue of clips, plays
    /// the head, and notifies on completion. Games override <see cref="PlayClip"/>
    /// to drive their actual Timeline/PlayableDirector and call
    /// <see cref="NotifyFinished"/> when the playback ends.
    ///
    /// Extensions over the bare queue:
    /// - <see cref="Pause"/>/<see cref="Resume"/> pause the active clip (the game
    ///   overrides <see cref="OnPause"/> to actually halt its PlayableDirector).
    /// - <see cref="Interrupt"/> drops the active clip for a higher-priority one
    ///   enqueued via <see cref="Play(long, int)"/>.
    /// - <see cref="Cutscene.Loop"/> clips replay instead of dequeuing on finish.
    /// </summary>
    public class CutsceneDirector
    {
        private readonly Queue<Cutscene> _queue = new Queue<Cutscene>();

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public Cutscene Current { get; private set; }

        public event Action<Cutscene> CutsceneStarted;
        public event Action<Cutscene> CutsceneFinished;
        public event Action<bool> PauseStateChanged;

        /// <summary>Queue a cutscene by id with default priority (0).</summary>
        public virtual void Play(long id)
            => Enqueue(new Cutscene { Id = id, Name = "cut_" + id, Priority = 0 });

        /// <summary>Queue a cutscene by id with an explicit priority.</summary>
        public virtual void Play(long id, int priority)
            => Enqueue(new Cutscene { Id = id, Name = "cut_" + id, Priority = priority });

        protected void Enqueue(Cutscene clip)
        {
            // Higher priority preempts a running clip (does not trigger its OnFinished).
            if (IsPlaying && Current != null && clip.Priority > Current.Priority)
                Interrupt();

            _queue.Enqueue(clip);
            if (!IsPlaying) StartNext();
        }

        protected virtual void PlayClip(Cutscene clip)
        {
            // Abstract: the game plays the actual Timeline asset here and must
            // call NotifyFinished() when done.
            Debug.Log("CutsceneDirector playing: " + clip.Name);
        }

        /// <summary>Override to actually halt/resume the underlying PlayableDirector.</summary>
        protected virtual void OnPause(bool paused) { }

        private void StartNext()
        {
            if (_queue.Count == 0) return;
            IsPlaying = true;
            IsPaused = false;
            Current = _queue.Dequeue();
            CutsceneStarted?.Invoke(Current);
            PlayClip(Current);
        }

        /// <summary>Call from the PlayableDirector's "finished" callback.</summary>
        public void NotifyFinished()
        {
            if (Current == null) return;

            // Loop clips replay instead of ending / dequeuing.
            if (Current.Loop)
            {
                PlayClip(Current);
                return;
            }

            var done = Current;
            done.OnFinished?.Invoke();
            Current = null;
            IsPlaying = false;
            IsPaused = false;
            CutsceneFinished?.Invoke(done);
            if (_queue.Count > 0) StartNext();
        }

        public void Pause()
        {
            if (!IsPlaying || IsPaused) return;
            IsPaused = true;
            OnPause(true);
            PauseStateChanged?.Invoke(true);
        }

        public void Resume()
        {
            if (!IsPlaying || !IsPaused) return;
            IsPaused = false;
            OnPause(false);
            PauseStateChanged?.Invoke(false);
        }

        /// <summary>Drop the active clip immediately and start the next queued one.</summary>
        public void Interrupt()
        {
            if (!IsPlaying) return;
            var cur = Current;
            Current = null;
            IsPlaying = false;
            IsPaused = false;
            CutsceneFinished?.Invoke(cur);   // treat as finished, but no OnFinished
            if (_queue.Count > 0) StartNext();
        }

        public void Skip()
        {
            if (IsPlaying) NotifyFinished();
        }

        public void Clear()
        {
            _queue.Clear();
            Current = null;
            IsPlaying = false;
            IsPaused = false;
        }
    }
}
