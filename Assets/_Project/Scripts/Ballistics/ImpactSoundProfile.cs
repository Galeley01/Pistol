using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;

namespace Pistol.Ballistics
{
    // How a bullet hit on one kind of surface (steel, wood...) sounds. BulletSystem plays it at the hit point (3D: the closer, the louder), so the SoundEvents
    // should have Spatial Blend 1. The angle of the hit picks the sound: straight on — the full ring of the plate,
    // the more glancing — quieter, higher and shorter. Far hits are heard a moment later (speed of sound).
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Audio/Impact Sound", fileName = "ImpactSoundProfile")]
    public class ImpactSoundProfile : ScriptableObject
    {
        [BoxGroup("Звуки"), Tooltip("Прямое попадание: полный звон плиты. Spatial Blend 1, чтобы звук шёл из точки попадания")]
        public SoundEvent direct;
        [BoxGroup("Звуки"), Tooltip("Попадание вскользь: короткий высокий звук. Пусто или без клипов — звучит прямой звук, тише и выше")]
        public SoundEvent glancing;

        [BoxGroup("Угол попадания"), Range(0f, 89f), Tooltip("С какого угла (от прямого, градусы) начинает примешиваться звук «вскользь»")]
        public float glancingFrom = 45f;
        [BoxGroup("Угол попадания"), Range(1f, 90f), Tooltip("С какого угла звучит только «вскользь»")]
        public float glancingFull = 75f;
        [BoxGroup("Угол попадания"), Range(0f, 1f), Tooltip("Громкость при попадании почти по касательной (прямое = 1)")]
        public float grazingVolume = 0.45f;
        [BoxGroup("Угол попадания"), Range(0.5f, 2f), Tooltip("Высота звука при попадании почти по касательной (прямое = 1)")]
        public float grazingPitch = 1.25f;

        [BoxGroup("Сила пули"), Range(0f, 1f), Tooltip("Насколько скорость пули влияет на громкость: 0 — не влияет, 1 — пропорционально")]
        public float speedInfluence = 0.5f;
        [BoxGroup("Сила пули"), Range(50f, 1500f), Tooltip("Скорость пули, при которой громкость полная, м/с")]
        public float referenceSpeed = 440f;

        [BoxGroup("Расстояние"), Tooltip("Звук приходит с задержкой по скорости звука: далёкая мишень звенит чуть позже попадания")]
        public bool delayBySpeedOfSound = true;
        [BoxGroup("Расстояние"), ShowIf("delayBySpeedOfSound"), Range(200f, 400f), Tooltip("Скорость звука, м/с")]
        public float speedOfSound = 343f;

        // incidence: 0 = straight on, 90 = along the surface.
        // Weights of both sounds, the pitch multiplier and the overall volume.
        public void Evaluate(float incidence, float bulletSpeed, out float directWeight, out float glancingWeight, out float pitch, out float volume)
        {
            float g = glancing != null && glancing.HasClips
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(glancingFrom, Mathf.Max(glancingFull, glancingFrom + 1f), incidence))
                : 0f;
            directWeight = 1f - g;
            glancingWeight = g;

            float t = Mathf.Clamp01(incidence / 90f);
            t *= t;   // stays close to the straight sound for most angles, changes near the tangent
            pitch = Mathf.Lerp(1f, grazingPitch, t);
            float speed01 = Mathf.Clamp01(bulletSpeed / Mathf.Max(referenceSpeed, 1f));
            volume = Mathf.Lerp(1f, grazingVolume, t) * Mathf.Lerp(1f, speed01, speedInfluence);
        }

        public float Delay(float distance) => delayBySpeedOfSound ? distance / Mathf.Max(speedOfSound, 1f) : 0f;
    }
}
