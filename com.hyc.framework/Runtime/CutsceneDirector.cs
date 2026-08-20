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
        public Action OnFinished;        // per-clip completion callback
    }

    /// <summary>
    /// Base runtime for config-driven cutscenes. Owns a queue of clips, plays
    /// the head, and notifies on completion. Games override <see cref="PlayClip"/>
    /// to drive their actual Timeline/PlayableDirector and call
    /// <see cref="NotifyFinished"/> when the playback ends.
    /// </summary>
    public class CutsceneDirector
    {
        private readonly Queue<Cutscene> _queue = new Queue<Cutscene>();
        public bool IsPlaying { get; private set; }
        public Cutscene Current { get; private set; }

        public event Action<Cutscene> CutsceneStarted;
        public event Action<Cutscene> CutsceneFinished;

        /// <summary>Queue a cutscene by id. Override in the game to resolve ids from a config table.</summary>
        public virtual void Play(long id)
        {
            Enqueue(new Cutscene { Id = id, Name = "cut_" + id });
        }

        protected void Enqueue(Cutscene clip)
        {
            _queue.Enqueue(clip);
            if (!IsPlaying) StartNext();
        }

        protected virtual void PlayClip(Cutscene clip)
        {
            // Abstract: the game plays the actual Timeline asset here and must
            // call NotifyFinished() when done.
            Debug.Log("CutsceneDirector playing: " + clip.Name);
        }

        private void StartNext()
        {
            if (_queue.Count == 0) return;
            IsPlaying = true;
            Current = _queue.Dequeue();
            CutsceneStarted?.Invoke(Current);
            PlayClip(Current);
        }

        /// <summary>Call from the PlayableDirector's "finished" callback.</summary>
        public void NotifyFinished()
        {
            if (Current == null) return;
            var done = Current;
            Current.OnFinished?.Invoke();
            Current = null;
            IsPlaying = false;
            CutsceneFinished?.Invoke(done);
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
        }
    }
}