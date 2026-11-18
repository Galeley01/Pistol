using UnityEngine;
using UnityEngine.Audio;
using NaughtyAttributes;

namespace Pistol.Audio
{
    // One sound of the game (a shot, a dry click, a step...): which clips, how loud, how it varies
    // and how it sounds in space. Played through GameAudio; nothing keeps its own AudioSource.
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Audio/Sound Event", fileName = "SoundEvent")]
    public class SoundEvent : ScriptableObject
    {
        [BoxGroup("Звук"), Tooltip("Клипы. Каждый раз играет случайный, по возможности не тот же, что в прошлый раз")]
        public AudioClip[] clips;
        [BoxGroup("Звук"), Range(0f, 1f), Tooltip("Громкость")]
        public float volume = 1f;
        [BoxGroup("Звук"), Range(0f, 0.5f), Tooltip("Случайный разброс громкости (0.1 = до −10%)")]
        public float volumeVariation = 0f;
        [BoxGroup("Звук"), Range(0f, 0.15f), Tooltip("Случайный сдвиг высоты звука, чтобы звуки не были одинаковыми (0.05 = ±5%)")]
        public float pitchVariation = 0.05f;
        [BoxGroup("Звук"), Tooltip("Группа микшера (Weapons, Footsteps...). Пусто — звук идёт мимо микшера")]
        public AudioMixerGroup output;
        [BoxGroup("Звук"), Min(0f), Tooltip("Пропустить начало клипа, сек: если в файле перед звуком тишина")]
        public float startTime = 0f;
        [BoxGroup("Звук"), Min(0f), Tooltip("Обрезать клип на этой секунде (с затуханием ниже). 0 — играть до конца")]
        public float endTime = 0f;
        [BoxGroup("Звук"), Range(0f, 2f), Tooltip("За сколько секунд до обрезки звук плавно затихает")]
        public float endFade = 0.2f;
        [BoxGroup("Звук"), Range(0, 256), Tooltip("Важность, если одновременно звучит слишком много: 0 — самый важный, 256 — наименее")]
        public int priority = 128;

        [BoxGroup("В пространстве"), Range(0f, 1f), Tooltip("0 — звук «в голове» (2D), 1 — из точки в мире (3D). Для своего оружия 0.6–0.8: слышно, что справа, но не режет одно ухо")]
        public float spatialBlend = 0.7f;
        [BoxGroup("В пространстве"), Range(0f, 360f), Tooltip("Насколько широко звук расходится по колонкам/ушам, градусы. 0 — точка, больше — шире и объёмнее")]
        public float spread = 0f;
        [BoxGroup("В пространстве"), Range(0.1f, 10f), Tooltip("Ближе этого расстояния звук на полной громкости, м")]
        public float minDistance = 1f;
        [BoxGroup("В пространстве"), Range(1f, 1000f), Tooltip("Дальше этого расстояния звук больше не затихает, м")]
        public float maxDistance = 300f;
        [BoxGroup("В пространстве"), Tooltip("Как звук затихает с расстоянием. Logarithmic — как в жизни")]
        public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
        [BoxGroup("В пространстве"), Range(0f, 5f), Tooltip("Эффект Доплера. Для своего оружия 0: иначе звук «плывёт» при поворотах")]
        public float dopplerLevel = 0f;

        [System.NonSerialized] int lastClip = -1;

        public bool HasClips => clips != null && clips.Length > 0;

        public AudioClip PickClip()
        {
            if (!HasClips) return null;
            int i = Random.Range(0, clips.Length);
            if (clips.Length > 1 && i == lastClip) i = (i + 1) % clips.Length;
            lastClip = i;
            return clips[i];
        }

        // Puts all settings of this event onto the source (before Play); pitchScale multiplies the pitch
        public void ApplyTo(AudioSource source, float volumeScale = 1f, float pitchScale = 1f)
        {
            source.outputAudioMixerGroup = output;
            source.priority = priority;
            source.volume = volume * volumeScale * (1f - Random.Range(0f, volumeVariation));
            source.pitch = (1f + Random.Range(-pitchVariation, pitchVariation)) * pitchScale;
            source.spatialBlend = spatialBlend;
            source.spread = spread;
            source.minDistance = minDistance;
            source.maxDistance = Mathf.Max(maxDistance, minDistance + 0.1f);
            source.rolloffMode = rolloff;
            source.dopplerLevel = dopplerLevel;
        }
    }
}
