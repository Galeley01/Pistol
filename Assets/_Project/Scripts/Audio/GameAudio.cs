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
            public AudioLowPassFilter LowPass;
            public Transform Follow;
            public float StartTime;
            public float End = -1f, Fade, Volume;   // cut at End (clip seconds) after fading out for Fade
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
                // Steam Audio: the voice is heard through a head model (HRTF), so in headphones it comes from its
                // direction — left, right, behind, above. Switched per sound (3D sounds only) and by DebugSettings
                var steam = go.AddComponent<SteamAudio.SteamAudioSource>();
                steam.directBinaural = true;
                steam.distanceAttenuation = false;   // Unity's own rolloff of the SoundEvent stays
                var lowPass = go.AddComponent<AudioLowPassFilter>();
                lowPass.cutoffFrequency = FullRange;
                pool.Add(new Voice { Source = source, LowPass = lowPass });
            }
        }

        // Plays the event at a point in the world
        public void Play(SoundEvent sound, Vector3 position, float volumeScale = 1f) => PlayVoice(sound, position, null, volumeScale, 0f);

        // At a point, with the pitch multiplied and a delay before it starts, sec (sound travelling from far away)
        public void Play(SoundEvent sound, Vector3 position, float volumeScale, float pitchScale, float delay)
            => PlayVoice(sound, position, null, volumeScale, 0f, pitchScale, delay);

        // The same, muffled: only frequencies below lowPass (Hz) are heard (an echo, a sound from far away).
        // Not in WebGL (no audio filters there): the sound plays unmuffled.
        public void PlayMuffled(SoundEvent sound, Vector3 position, float volumeScale, float pitchScale, float delay, float lowPass)
            => PlayVoice(sound, position, null, volumeScale, 0f, pitchScale, delay, true, lowPass);

        const float FullRange = 22000f;

        // A gunshot: the shot itself on the muzzle, and the place answers it (RangeAcoustics: tail, echoes, reverb).
        // Without acoustics (or with them switched off for comparison) the shot plays in full, as recorded.
        [SerializeField, Tooltip("Звук окружения выстрела (хвост, эхо, реверберация). Пусто — выстрел звучит как записан")]
        RangeAcoustics acoustics;

        public void PlayShot(SoundEvent sound, Transform muzzle, float volumeScale = 1f)
        {
            if (muzzle == null) return;
            bool placed = acoustics != null && acoustics.Active;
            PlayVoice(sound, muzzle.position, muzzle, volumeScale, 0f, 1f, 0f, useEnd: placed);
            if (placed) acoustics.OnShot(muzzle.position, muzzle.forward, volumeScale);
        }

        // Plays the event and keeps it on the object while it sounds (a weapon in the hands).
        // startAt > 0 skips the start of the clip, sec (to line a moment inside the clip up with an event)
        public void Play(SoundEvent sound, Transform follow, float volumeScale = 1f, float startAt = 0f)
        {
            if (follow == null) return;
            PlayVoice(sound, follow.position, follow, volumeScale, startAt);
        }

        void PlayVoice(SoundEvent sound, Vector3 position, Transform follow, float volumeScale, float startAt,
                       float pitchScale = 1f, float delay = 0f, bool useEnd = true, float lowPass = FullRange)
        {
            if (sound == null) return;
            var clip = sound.PickClip();
            if (clip == null) return;

            var voice = Take();
            voice.Follow = follow;
            voice.StartTime = Time.unscaledTime;
            voice.Source.transform.position = position;
            sound.ApplyTo(voice.Source, volumeScale, pitchScale);
            voice.Source.spatialize = (debug == null || debug.SteamAudioHrtf) && sound.spatialBlend > 0.05f;
            voice.Source.clip = clip;
            startAt += sound.startTime;   // silence at the start of the file is skipped
            voice.Source.time = startAt > 0f ? Mathf.Min(startAt, Mathf.Max(clip.length - 0.01f, 0f)) : 0f;
            voice.End = useEnd && sound.endTime > 0f ? sound.endTime : -1f;
            voice.Fade = Mathf.Max(sound.endFade, 0.001f);
            voice.Volume = voice.Source.volume;
            if (voice.LowPass != null) voice.LowPass.cutoffFrequency = Mathf.Clamp(lowPass, 10f, FullRange);
            if (delay > 0f) voice.Source.PlayDelayed(delay);
            else voice.Source.Play();
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
                // A clip cut short: fade out and stop at its end time
                if (v.End > 0f && v.Source.isPlaying)
                {
                    float t = v.Source.time;
                    if (t >= v.End) { v.Source.Stop(); v.End = -1f; }
                    else if (t > v.End - v.Fade) v.Source.volume = v.Volume * (v.End - t) / v.Fade;
                }
                if (v.Follow == null) continue;
                if (!v.Source.isPlaying) { v.Follow = null; continue; }
                v.Source.transform.position = v.Follow.position;
            }
        }
    }
}
