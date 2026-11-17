using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.Audio
{
    // Sound of the game: plays SoundEvents from a pool of AudioSources, at a point or following an object.
    // Nobody adds AudioSources for one-shot sounds anymore. When all voices are busy, the oldest one is reused.
    // Later room sound (reflections, reverb zones, occlusion) builds on this.
    //
    // Goes on its own object in the scene. Runs late so followed objects (the weapon on the camera) have moved.
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        [BoxGroup("Пул"), Range(4, 64), SerializeField, Tooltip("Сколько звуков может звучать одновременно")]
        int voices = 24;
        [BoxGroup("Отладка"), SerializeField, Tooltip("Отладочные флаги: лог каждого звука включается там")]
        DebugSettings debug;

        sealed class Voice
        {
            public AudioSource Source;
            public Transform Follow;
            public float StartTime;
        }

        readonly List<Voice> pool = new List<Voice>();

        [ShowNativeProperty]
        public int Playing
        {
            get
            {
                int n = 0;
                foreach (var v in pool) if (v.Source != null && v.Source.isPlaying) n++;
                return n;
            }
        }

        void Awake()
        {
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                pool.Add(new Voice { Source = source });
            }
        }

        // Plays the event at a point in the world
        public void Play(SoundEvent sound, Vector3 position, float volumeScale = 1f) => PlayVoice(sound, position, null, volumeScale, 0f);

        // Plays the event and keeps it on the object while it sounds (a weapon in the hands).
        // startAt > 0 skips the start of the clip, sec (to line a moment inside the clip up with an event)
        public void Play(SoundEvent sound, Transform follow, float volumeScale = 1f, float startAt = 0f)
        {
            if (follow == null) return;
            PlayVoice(sound, follow.position, follow, volumeScale, startAt);
        }

        void PlayVoice(SoundEvent sound, Vector3 position, Transform follow, float volumeScale, float startAt)
        {
            if (sound == null) return;
            var clip = sound.PickClip();
            if (clip == null) return;

            var voice = Take();
            voice.Follow = follow;
            voice.StartTime = Time.unscaledTime;
            voice.Source.transform.position = position;
            sound.ApplyTo(voice.Source, volumeScale);
            voice.Source.clip = clip;
            voice.Source.time = startAt > 0f ? Mathf.Min(startAt, Mathf.Max(clip.length - 0.01f, 0f)) : 0f;
            voice.Source.Play();
            if (debug != null && debug.SoundPlays) Debug.Log($"GameAudio: {sound.name} ({clip.name})", this);
        }

        // A free voice, or the one that has been playing the longest
        Voice Take()
        {
            Voice oldest = null;
            foreach (var v in pool)
            {
                if (!v.Source.isPlaying) return v;
                if (oldest == null || v.StartTime < oldest.StartTime) oldest = v;
            }
            oldest.Source.Stop();
            return oldest;
        }

        void LateUpdate()
        {
            foreach (var v in pool)
            {
                if (v.Follow == null) continue;
                if (!v.Source.isPlaying) { v.Follow = null; continue; }
                v.Source.transform.position = v.Follow.position;
            }
        }
    }
}
