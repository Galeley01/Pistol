using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.Audio
{
    // How the place answers a gunshot. GameAudio.PlayShot calls OnShot for every shot; here:
    //   - the reverb of the space (an Audio Reverb Zone around the listener, preset from the profile; Mac/PC only);
    //   - the tail: a distant roll after the bang;
    //   - echoes from big surfaces: rays around the shooter (and upwards), each wall far enough answers with a quiet,
    //     muffled copy of the shot from where the ray hit it, after the sound went there and back (2 d / 343 s);
    //     echoes close in time merge into one; the bang is louder towards the barrel;
    //   - early reflections: walls closer than the echo distance and the floor answer with a quiet copy of the bang
    //     itself a few milliseconds later, from where they are (this is what tells "a wall on the left");
    //   - aiming up or down changes it: into the sky drier, into the ground the floor answers louder;
    //   - outdoors, echoes from the distant hills.
    // The profile is the default one (outdoor) or that of an AcousticZone (a child box) the listener stands in.
    // DebugSettings: "New Acoustics" off (or key 4) — the shot plays as recorded, with no reverb; "Force Indoor".
    //
    // Goes on its own object in the scene (Pistol > Setup Range Acoustics makes it); GameAudio points to it.
    [RequireComponent(typeof(AudioReverbZone))]
    [DisallowMultipleComponent]
    public class RangeAcoustics : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте GameAudio"), SerializeField, Tooltip("Через него звучат хвост и эхо")]
        GameAudio gameAudio;
        [BoxGroup("Ссылки"), SerializeField, Tooltip("Где уши игрока (камера): по ней выбирается зона и туда ставится реверберация")]
        Transform listener;
        [BoxGroup("Ссылки"), SerializeField, Tooltip("Отладка: переключатели «New Acoustics» и «Force Indoor»")]
        DebugSettings debug;

        [BoxGroup("Профили"), Required("Назначьте профиль улицы"), SerializeField, Tooltip("Как звучит место по умолчанию (улица)")]
        AcousticsProfile outdoor;
        [BoxGroup("Профили"), SerializeField, Tooltip("Закрытый тир: для «Force Indoor» и зон без своего профиля")]
        AcousticsProfile indoor;
        [BoxGroup("Эхо от стен"), SerializeField, Tooltip("Слои, от которых бывает эхо")]
        LayerMask echoMask = ~0;

        AudioReverbZone zone;
        AcousticZone[] zones = System.Array.Empty<AcousticZone>();
        AcousticsProfile current;
        readonly List<(Collider collider, float distance, Vector3 point, Vector3 dir)> found = new List<(Collider, float, Vector3, Vector3)>();
        static RaycastHit[] hits = new RaycastHit[8];
        const int MaxHits = 256;

        // The nearest hit that isn't the shooter (the player's own capsule would stop every ray going backwards)
        bool FirstHit(Vector3 from, Vector3 dir, float distance, out RaycastHit hit)
        {
            hit = default;
            Transform self = listener != null ? listener.root : null;
            // The buffer gets hits in no particular order: when it is full the nearest one may be missing, so grow it and ask again
            int n = Physics.RaycastNonAlloc(from, dir, hits, distance, echoMask, QueryTriggerInteraction.Ignore);
            while (n == hits.Length && hits.Length < MaxHits)
            {
                hits = new RaycastHit[hits.Length * 2];
                n = Physics.RaycastNonAlloc(from, dir, hits, distance, echoMask, QueryTriggerInteraction.Ignore);
            }
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (self != null && hits[i].collider.transform.IsChildOf(self)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; }
            }
            return best < float.MaxValue;
        }

        // Off = everything as recorded (comparison)
        public bool Active => isActiveAndEnabled && outdoor != null && (debug == null || debug.NewAcoustics);

        [ShowNativeProperty] public string Place => current != null ? current.name : "—";

        void Awake()
        {
            zone = GetComponent<AudioReverbZone>();
            zones = GetComponentsInChildren<AcousticZone>(true);
        }

        void Update()
        {
            current = PickProfile();
            if (listener != null) transform.position = listener.position;   // the reverb surrounds the listener
            zone.enabled = Active && current != null;
            if (zone.enabled && zone.reverbPreset != current.reverb) zone.reverbPreset = current.reverb;
        }

        AcousticsProfile PickProfile()
        {
            if (debug != null && debug.ForceIndoor && indoor != null) return indoor;
            if (listener != null)
                foreach (var z in zones)
                    if (z != null && z.isActiveAndEnabled && z.Profile != null && z.Contains(listener.position)) return z.Profile;
            return outdoor;
        }

        // forward: where the barrel points (the bang is louder that way: echoes from ahead are louder)
        public void OnShot(Vector3 position, Vector3 forward, float loudness)
        {
            if (!Active || gameAudio == null) return;
            var p = current != null ? current : PickProfile();
            if (p == null) return;

            // -1 shooting straight down .. +1 straight up
            float aimUp = forward.sqrMagnitude > 0.0001f ? Vector3.Dot(forward.normalized, Vector3.up) : 0f;

            FindSurfaces(position, p);

            if (p.tail != null)
            {
                // Into the sky the roll is drier and thinner, into the ground it is fuller; it rolls away to the open side
                float tail = p.tailVolume * (1f - p.tailAimInfluence * aimUp) * loudness;
                gameAudio.Play(p.tail, position + Vector3.up + openSide * p.tailOpenSide, Mathf.Max(0f, tail), 1f, p.tailDelay);
            }
            PlayReflections(position, forward, loudness, p);

            if (p.echo == null) return;
            float lastDelay = -1f;
            int played = 0;
            foreach (var (_, distance, point, dir) in found)
            {
                if (distance < p.minDistance) continue;   // close walls are early reflections, not echoes
                if (played >= p.maxEchoes) break;
                float delay = 2f * distance / AcousticsProfile.SpeedOfSound;
                if (lastDelay >= 0f && delay - lastDelay < p.mergeTime) continue;   // too close in time: one echo, not "chug-chug"
                lastDelay = delay;
                float far01 = Mathf.InverseLerp(p.minDistance, p.maxDistance, distance);
                float volume = p.echoVolume * loudness * Mathf.Clamp01(p.minDistance / distance) * Facing(dir, forward, p.directivity);
                gameAudio.PlayMuffled(p.echo, point, volume, p.echoPitch, delay, Mathf.Lerp(p.lowPassNear, p.lowPassFar, far01));
                played++;
            }

            if (p.terrainEcho && p == outdoor)
            {
                // The hills all around: one echo from where the shot went (level), a fainter one from the side, later
                Vector3 ahead = Vector3.ProjectOnPlane(forward, Vector3.up);
                if (ahead.sqrMagnitude < 0.01f && listener != null) ahead = Vector3.ProjectOnPlane(listener.forward, Vector3.up);
                ahead = ahead.sqrMagnitude > 0.0001f ? ahead.normalized : Vector3.forward;
                Vector3 side = Quaternion.Euler(0f, Random.value < 0.5f ? -110f : 110f, 0f) * ahead;
                float d = p.terrainDistance;
                // Into the sky or the ground the bang still spreads to the hills, only weaker towards them
                float level = Mathf.Lerp(0.6f, 1f, 1f - Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)));
                gameAudio.PlayMuffled(p.echo, position + ahead * d, p.terrainVolume * level * loudness, p.echoPitch * 0.95f,
                                      2f * d / AcousticsProfile.SpeedOfSound, p.terrainLowPass);
                gameAudio.PlayMuffled(p.echo, position + side * d * 1.3f, p.terrainVolume * 0.6f * level * loudness, p.echoPitch * 0.92f,
                                      2.6f * d / AcousticsProfile.SpeedOfSound, p.terrainLowPass * 0.8f);
            }
        }

        // Early reflections: the shot itself bouncing off walls closer than the echo distance and off the floor.
        // They come 3–70 ms after the bang from the point on the wall: this is how the ear tells "a wall on the left",
        // "between two walls", "next to the floor".
        void PlayReflections(Vector3 position, Vector3 forward, float loudness, AcousticsProfile p)
        {
            if (p.reflection == null) return;
            int played = 0;
            bool log = debug != null && debug.WeaponEvents;
            var sb = log ? new System.Text.StringBuilder("Acoustics: ") : null;
            foreach (var (_, distance, point, dir) in found)
            {
                if (distance >= p.minDistance) break;   // sorted: the rest are echoes
                if (played >= p.maxReflections) break;
                float volume = p.reflectionVolume * loudness * Mathf.Pow(1.5f / Mathf.Max(distance, 0.5f), 0.5f)
                             * Facing(dir, forward, p.directivity);
                gameAudio.PlayMuffled(p.reflection, point, Mathf.Min(volume, 1f), 0.97f,
                                      2f * distance / AcousticsProfile.SpeedOfSound * p.reflectionDelayScale, p.reflectionLowPass);
                played++;
                if (log)
                {
                    float side = listener != null ? Vector3.Dot(dir, listener.right) : 0f;
                    sb.Append($"wall {distance:0.0} m {(side < -0.3f ? "left" : side > 0.3f ? "right" : "ahead/behind")} x{volume:0.00}; ");
                }
            }

            // The floor: always there, right under the shot; much louder when the barrel points down at it
            if (p.floorVolume > 0f && FirstHit(position, Vector3.down, 10f, out var h))
            {
                float volume = p.floorVolume * loudness * Facing(Vector3.down, forward, 0.8f);
                gameAudio.PlayMuffled(p.reflection, h.point, volume, 0.95f,
                                      2f * h.distance / AcousticsProfile.SpeedOfSound, p.reflectionLowPass * 0.7f);
                if (log) sb.Append($"floor {h.distance:0.0} m x{volume:0.00}");
            }
            if (log)
            {
                float side = listener != null ? Vector3.Dot(openSide, listener.right) : 0f;
                sb.Append($"; tail rolls {(openSide.sqrMagnitude < 0.04f ? "all around" : side < -0.3f ? "left" : side > 0.3f ? "right" : "ahead/behind")}");
                Debug.Log(sb.ToString(), this);
            }
        }

        // 1 in the direction of the barrel, down to (1 - directivity) behind it
        static float Facing(Vector3 dir, Vector3 forward, float directivity)
        {
            if (forward.sqrMagnitude < 0.0001f) return 1f;
            float dot = Vector3.Dot(dir, forward.normalized);
            return Mathf.Lerp(1f - directivity, 1f, (dot + 1f) * 0.5f);
        }

        // Big surfaces around the shooter between min and max distance, nearest first, one echo per surface.
        // A ring of level rays, and (if the profile wants) rays upwards for a ceiling or an overhang;
        // the floor is always too close to give an echo (it is part of the bang itself).
        // Where it is open around the shooter (the level directions whose rays found nothing near): the tail rolls that way
        Vector3 openSide;

        void FindSurfaces(Vector3 from, AcousticsProfile p)
        {
            found.Clear();
            float offset = Random.value * 360f;
            Vector3 open = Vector3.zero;
            for (int i = 0; i < p.rays; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, offset + i * 360f / p.rays, 0f) * Vector3.forward;
                float d = FirstHit(from, dir, 30f, out var h) ? h.distance : 30f;
                open += dir * (d / 30f);
                Probe(from, dir, p);
            }
            openSide = open.sqrMagnitude > 0.01f ? open.normalized * Mathf.Clamp01(open.magnitude / Mathf.Max(p.rays * 0.3f, 1f)) : Vector3.zero;
            if (p.verticalRays)
            {
                Probe(from, Vector3.up, p);
                for (int i = 0; i < 4; i++)
                    Probe(from, Quaternion.Euler(-45f, offset + i * 90f, 0f) * Vector3.forward, p);
            }
            found.Sort((a, b) => a.distance.CompareTo(b.distance));
        }

        void Probe(Vector3 from, Vector3 dir, AcousticsProfile p)
        {
            if (!FirstHit(from, dir, p.maxDistance, out var h)) return;
            if (h.distance < p.reflectionMin) return;   // closer than this it is part of the bang
            var size = h.collider.bounds.size;
            if (Mathf.Max(size.x, size.y, size.z) < p.minSurfaceSize) return;
            if (found.Exists(f => f.collider == h.collider)) return;
            found.Add((h.collider, h.distance, h.point, dir));
        }

        // Called by Pistol > Setup Range Acoustics
        public void Configure(GameAudio audio, Transform ears, DebugSettings settings, AcousticsProfile outdoorProfile, AcousticsProfile indoorProfile)
        {
            gameAudio = audio;
            listener = ears;
            debug = settings;
            outdoor = outdoorProfile;
            indoor = indoorProfile;
        }
    }
}
