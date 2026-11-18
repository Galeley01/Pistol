using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Core
{
    // All debug switches in one place: logs, bullet paths, the future bullet line, the aim point,
    // slow motion and editor pauses for tuning the cylinder close.
    // In a normal (non-Development) build every switch reads as off, whatever is ticked here,
    // and the debug overlays switch themselves off.
    // Given to the scene services (BulletSystem, GameAudio) and, through ItemContext, to held items.
    [CreateAssetMenu(menuName = "Pistol/Debug Settings", fileName = "DebugSettings")]
    public class DebugSettings : ScriptableObject
    {
        [BoxGroup("Логи в Console"), Tooltip("Выстрелы, щелчки, барабан, осмотр, выбор камор")]
        public bool weaponEvents = true;
        [BoxGroup("Логи в Console"), Tooltip("Каждое намерение игрока для предмета в руках (выстрел, перезарядка, осмотр)")]
        public bool itemIntents = false;
        [BoxGroup("Логи в Console"), Tooltip("Каждый проигранный звук")]
        public bool soundPlays = false;
        [BoxGroup("Логи в Console"), Tooltip("Каждое попадание пули: во что, с какого расстояния, с какой скоростью")]
        public bool bulletHits = true;

        [BoxGroup("На экране"), Tooltip("Путь каждой пули жёлтой линией")]
        public bool bulletPaths = true;
        [BoxGroup("На экране"), Tooltip("Линия, куда полетит пуля, если выстрелить сейчас")]
        public bool trajectoryPreview = false;
        [BoxGroup("На экране"), Tooltip("Маленькая точка там, куда смотрит ствол (удобно с Free-Aim)")]
        public bool aimPoint = false;

        [BoxGroup("Закрытие барабана"), Range(0.02f, 1f)]
        [Tooltip("Замедление всей игры, пока рука закрывает барабан: 1 — обычная скорость, 0.1 — в 10 раз медленнее. " +
                 "Звук закрытия при этом играет с обычной скоростью и щелчок слышен раньше")]
        public float closeSlowMotion = 1f;
        [BoxGroup("Закрытие барабана"), Range(-1, 8)]
        [Tooltip("Поставить редактор на паузу в начале этой фазы закрытия (0 — первая фаза). −1 — без паузы. " +
                 "Дальше по кадру — кнопка Step рядом с Play, продолжить — Pause")]
        public int closePauseAtPhase = -1;
        [BoxGroup("Закрытие барабана"), Tooltip("Поставить редактор на паузу в момент защёлкивания крана")]
        public bool closePauseAtLatch = false;

        [BoxGroup("Сравнение вида"), Tooltip("Новая постобработка: ACES, лёгкий bloom, AO. Выкл — как было. Клавиша 4 в игре — то же")]
        public bool newPostFx = true;
        [BoxGroup("Сравнение вида"), Tooltip("Новое небо (HDRI) и солнце по нему. Выкл — стандартное небо Unity и прежнее солнце")]
        public bool newSky = true;
        [BoxGroup("Сравнение вида"), Tooltip("Новая камера: поле зрения 70° и слабое искажение линзы. Выкл — как было (90° и сильное искажение)")]
        public bool newCamera = true;
        [BoxGroup("Сравнение вида"), Tooltip("Новый звук окружения: хвост выстрела, эхо от стен и холмов, реверберация. Выкл — выстрел как записан")]
        public bool newAcoustics = true;
        [BoxGroup("Сравнение вида"), Tooltip("Проверка: звучать как в закрытом тире, где бы ни стоял (иначе — по зонам, по умолчанию улица)")]
        public bool forceIndoor = false;
        [BoxGroup("Сравнение вида"), EnableIf("NotPlaying")]
        [Tooltip("Запечённый свет (карты освещения). Переключается только вне Play: вкл — запекается заново (несколько минут), " +
                 "выкл — запечённое убирается, неподвижное освещено только общим светом неба. Потом сохраните сцену")]
        public bool bakedLight = true;

        bool NotPlaying() => !Application.isPlaying;

        // The editor bakes or removes the baked light when the switch changes outside Play (LightingBakeSetup listens)
        public static event System.Action<bool> BakedLightSwitched;
        [System.NonSerialized] bool lastBakedLight;
        [System.NonSerialized] bool loaded;

        void OnEnable()
        {
            lastBakedLight = bakedLight;
            loaded = true;
        }

        void OnValidate()
        {
            if (!loaded || bakedLight == lastBakedLight) return;
            lastBakedLight = bakedLight;
            if (!Application.isPlaying) BakedLightSwitched?.Invoke(bakedLight);
        }

        // Editor and Development builds only
        public static bool Allowed => Debug.isDebugBuild;

        public bool WeaponEvents => Allowed && weaponEvents;
        public bool ItemIntents => Allowed && itemIntents;
        public bool SoundPlays => Allowed && soundPlays;
        public bool BulletHits => Allowed && bulletHits;
        public bool BulletPaths => Allowed && bulletPaths;
        public bool TrajectoryPreview => Allowed && trajectoryPreview;
        public bool AimPoint => Allowed && aimPoint;
        // In a normal build the new look is always on
        public bool NewPostFx => !Allowed || newPostFx;
        public bool NewSky => !Allowed || newSky;
        public bool NewCamera => !Allowed || newCamera;
        public bool NewAcoustics => !Allowed || (newAcoustics && !AllOld);
        public bool ForceIndoor => Allowed && forceIndoor;

        // Key 4 (LookSwitcher): everything as before this work, for the moment (not saved)
        [System.NonSerialized] bool allOld;
        public bool AllOld => Allowed && allOld;
        public void SetAllOld(bool on) => allOld = on;
        public void SetNewPostFx(bool on) => newPostFx = on;

        public float CloseSlowMotion => Allowed ? Mathf.Clamp(closeSlowMotion, 0.02f, 1f) : 1f;
        public int ClosePauseAtPhase => Allowed ? closePauseAtPhase : -1;
        public bool ClosePauseAtLatch => Allowed && closePauseAtLatch;
    }
}
