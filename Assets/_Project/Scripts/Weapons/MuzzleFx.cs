using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Weapons
{
    // Shot effects of a weapon: muzzle flash, flame tongue, muzzle smoke, an optional side blast
    // (the revolver's cylinder gap) and a short light. All of them live in a prefab nested under the muzzle,
    // so they are seen and tuned in the editor like any particle system. Play() fires them once.
    // The systems run all the time with zero emission; a shot only emits particles into them.
    //
    // Goes on the root of a muzzle FX prefab.
    [DisallowMultipleComponent]
    public class MuzzleFx : MonoBehaviour
    {
        [BoxGroup("Вспышка"), SerializeField, Tooltip("Огненная «звезда» у дула")] ParticleSystem flash;
        [BoxGroup("Вспышка"), Range(0, 20), Tooltip("Сколько частиц вспышки за выстрел")] public int flashCount = 3;
        [BoxGroup("Вспышка"), SerializeField, Tooltip("Язык пламени вперёд по стволу")] ParticleSystem flame;
        [BoxGroup("Вспышка"), Range(0, 30), Tooltip("Сколько частиц пламени за выстрел")] public int flameCount = 6;

        [BoxGroup("Дым"), SerializeField, Tooltip("Дым из дула (висит в мире, пока оружие двигается)")] ParticleSystem smoke;
        [BoxGroup("Дым"), Range(0, 40), Tooltip("Сколько клубов дыма из дула за выстрел")] public int smokeCount = 5;
        [BoxGroup("Дым"), SerializeField, Tooltip("Боковой выхлоп газов (у револьвера — из зазора между барабаном и стволом). Можно оставить пустым")] ParticleSystem sideSmoke;
        [BoxGroup("Дым"), Range(0, 20), Tooltip("Сколько клубов бокового выхлопа за выстрел")] public int sideSmokeCount = 2;

        [BoxGroup("Свет"), SerializeField, Tooltip("Короткий свет от вспышки. Цвет и дальность — в самом Light")] Light flashLight;
        [BoxGroup("Свет"), Range(0f, 30f), Tooltip("Яркость в первый момент, дальше быстро гаснет")] public float lightIntensity = 8f;
        [BoxGroup("Свет"), Range(0.02f, 0.2f), Tooltip("Сколько секунд горит свет")] public float lightDuration = 0.06f;

        float lightTimer;

        // Emit spawns particles immediately, even if the previous smoke is still around
        public void Play()
        {
            Emit(flash, flashCount);
            Emit(flame, flameCount);
            Emit(smoke, smokeCount);
            Emit(sideSmoke, sideSmokeCount);
            if (flashLight != null && lightIntensity > 0f) lightTimer = lightDuration;
        }

        static void Emit(ParticleSystem ps, int count)
        {
            if (ps != null && count > 0 && ps.gameObject.activeInHierarchy) ps.Emit(count);
        }

        void Awake()
        {
            if (flashLight != null) flashLight.enabled = false;
        }

        void Update()
        {
            if (flashLight == null) return;
            if (lightTimer > 0f)
            {
                lightTimer -= Time.deltaTime;
                float k = Mathf.Clamp01(lightTimer / Mathf.Max(lightDuration, 0.001f));
                flashLight.enabled = true;
                flashLight.intensity = lightIntensity * k * k;
            }
            else flashLight.enabled = false;
        }

        [Button("Тест: выстрел (Play Mode)")]
        void TestPlay()
        {
            if (Application.isPlaying) Play();
        }

        // Editor only: called by the muzzle FX builder
        public void SetParts(ParticleSystem flashPs, ParticleSystem flamePs, ParticleSystem smokePs, ParticleSystem sidePs, Light lightSource)
        {
            flash = flashPs;
            flame = flamePs;
            smoke = smokePs;
            sideSmoke = sidePs;
            flashLight = lightSource;
        }
    }
}
