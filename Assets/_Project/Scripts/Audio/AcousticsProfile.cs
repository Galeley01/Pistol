using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Audio
{
    // How a place answers a gunshot (an open range, a closed hall...): the reverb of the space, the long tail
    // rolling away, echoes from big surfaces near the shooter and from the distant terrain.
    // Played by RangeAcoustics. Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Audio/Acoustics Profile", fileName = "AcousticsProfile")]
    public class AcousticsProfile : ScriptableObject
    {
        [BoxGroup("Реверберация"), Tooltip("Гул пространства (Audio Reverb Zone). Plain — открытое поле, Stoneroom — каменный зал. " +
                                            "Работает на Mac и ПК; в WebGL реверберации нет, там звучат только хвост и эхо")]
        public AudioReverbPreset reverb = AudioReverbPreset.Plain;

        [BoxGroup("Хвост выстрела"), Tooltip("Далёкий раскат после выстрела. Пусто — без хвоста")]
        public SoundEvent tail;
        [BoxGroup("Хвост выстрела"), Range(0f, 1.5f), Tooltip("Громкость хвоста")]
        public float tailVolume = 0.6f;
        [BoxGroup("Хвост выстрела"), Range(0f, 0.5f), Tooltip("Через сколько секунд после хлопка вступает хвост")]
        public float tailDelay = 0.04f;

        [BoxGroup("Ранние отражения"), Tooltip("Отражение самого выстрела от близких стен и пола (1–12 м): приходит через доли секунды " +
                                                  "из точки на стене — по нему слышно, с какой стороны стена. Пусто — без ранних отражений")]
        public SoundEvent reflection;
        [BoxGroup("Ранние отражения"), Range(0.3f, 5f), Tooltip("Ближе этого отражение не отделяется от выстрела, м")]
        public float reflectionMin = 1f;
        [BoxGroup("Ранние отражения"), Range(0f, 1.5f), Tooltip("Громкость отражения от стены в 1.5 м (дальше — тише)")]
        public float reflectionVolume = 0.9f;
        [BoxGroup("Ранние отражения"), Range(1f, 4f), Tooltip("Преувеличение задержки отражения. 1 — как в жизни (но ухо сливает его с выстрелом и не слышит, " +
                                                            "откуда стена), 2 — отчётливый ответ с той стороны")]
        public float reflectionDelayScale = 2f;
        [BoxGroup("Ранние отражения"), Range(1, 8), Tooltip("Сколько отражений максимум (самые близкие)")]
        public int maxReflections = 4;
        [BoxGroup("Ранние отражения"), Range(500f, 12000f), Tooltip("Насколько приглушено отражение: слышно только ниже этой частоты, Гц")]
        public float reflectionLowPass = 4000f;
        [BoxGroup("Ранние отражения"), Range(0f, 1f), Tooltip("Отражение от пола (оно есть всегда; громче, когда стреляешь вниз)")]
        public float floorVolume = 0.35f;

        [BoxGroup("Эхо от стен"), Tooltip("Звук эха (приглушённый выстрел). Пусто — без эха")]
        public SoundEvent echo;
        [BoxGroup("Эхо от стен"), Range(0, 32), Tooltip("Сколько лучей искать вокруг стрелка")]
        public int rays = 12;
        [BoxGroup("Эхо от стен"), Range(1, 8), Tooltip("Сколько эх максимум на один выстрел (самые близкие)")]
        public int maxEchoes = 3;
        [BoxGroup("Эхо от стен"), Range(2f, 40f), Tooltip("Ближе этого поверхность даёт не эхо, а гул (реверберацию), м")]
        public float minDistance = 8f;
        [BoxGroup("Эхо от стен"), Range(20f, 400f), Tooltip("Дальше этого эха от стен не ищем, м")]
        public float maxDistance = 120f;
        [BoxGroup("Эхо от стен"), Range(0.5f, 20f), Tooltip("Поверхность меньше этого (по большей стороне) эха не даёт, м")]
        public float minSurfaceSize = 2f;
        [BoxGroup("Эхо от стен"), Range(0f, 1.5f), Tooltip("Громкость эха от поверхности на минимальном расстоянии")]
        public float echoVolume = 0.18f;
        [BoxGroup("Эхо от стен"), Range(0.5f, 1.2f), Tooltip("Высота звука эха (ниже 1 — глуше и ниже)")]
        public float echoPitch = 0.8f;
        [BoxGroup("Эхо от стен"), Range(200f, 8000f), Tooltip("Насколько глухое эхо от близкой поверхности: слышно только ниже этой частоты, Гц")]
        public float lowPassNear = 1200f;
        [BoxGroup("Эхо от стен"), Range(100f, 4000f), Tooltip("То же для самой далёкой поверхности: воздух съедает высокие частоты")]
        public float lowPassFar = 450f;
        [BoxGroup("Эхо от стен"), Range(0f, 0.3f), Tooltip("Эха ближе друг к другу по времени сливаются в одно (без «чух-чух»), сек")]
        public float mergeTime = 0.12f;
        [BoxGroup("Эхо от стен"), Tooltip("Искать поверхности и вверх (потолок, навес), не только вокруг")]
        public bool verticalRays = true;
        [BoxGroup("Эхо от стен"), Range(0f, 1f), Tooltip("Выстрел громче в сторону ствола: 0 — одинаково во все стороны, 1 — назад почти тихо")]
        public float directivity = 0.5f;

        [BoxGroup("Эхо от рельефа"), Tooltip("Эхо от далёких холмов вокруг (только на улице)")]
        public bool terrainEcho = true;
        [BoxGroup("Эхо от рельефа"), ShowIf("terrainEcho"), Range(50f, 1000f), Tooltip("Расстояние до холмов, м (эхо придёт через 2 × расстояние / 343 с)")]
        public float terrainDistance = 260f;
        [BoxGroup("Эхо от рельефа"), ShowIf("terrainEcho"), Range(0f, 1f), Tooltip("Громкость эха от холмов")]
        public float terrainVolume = 0.15f;
        [BoxGroup("Эхо от рельефа"), ShowIf("terrainEcho"), Range(100f, 4000f), Tooltip("Насколько глухое эхо от холмов, Гц")]
        public float terrainLowPass = 350f;

        [BoxGroup("Хвост выстрела"), Range(0f, 1f), Tooltip("Насколько меняется хвост от направления: в небо — суше и тише, в землю — плотнее")]
        public float tailAimInfluence = 0.35f;
        [BoxGroup("Хвост выстрела"), Range(0f, 60f), Tooltip("Хвост уходит в открытую сторону: на сколько метров в сторону, где нет стен (0 — из точки выстрела)")]
        public float tailOpenSide = 25f;

        public const float SpeedOfSound = 343f;
    }
}
