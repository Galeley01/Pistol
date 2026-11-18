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

        [BoxGroup("Оружие"), Tooltip("Бесконечные патроны: каморы не пустеют, перезаряжать не нужно")]
        public bool infiniteAmmo = false;

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
        [BoxGroup("Сравнение вида"), Tooltip("Авто-экспозиция (адаптация глаз): картинка плавно светлеет, когда в кадре темно, и темнеет на ярком. " +
                                              "Нет в WebGL (нужны вычислительные шейдеры). Выкл — экспозиция постоянная, как было")]
        public bool autoExposure = true;
        [BoxGroup("Сравнение вида"), Tooltip("Новый звук окружения: хвост выстрела, эхо от стен и холмов, реверберация. Выкл — выстрел как записан")]
        public bool newAcoustics = true;
        [BoxGroup("Сравнение вида"), Tooltip("Проверка: звучать как в закрытом тире, где бы ни стоял (иначе — по зонам, по умолчанию улица)")]
        public bool forceIndoor = false;
        [BoxGroup("Сравнение вида"), Tooltip("Объёмный звук Steam Audio (HRTF): в наушниках слышно, откуда звук — слева, справа, сзади, сверху. " +
                                              "Выкл — обычная стереопанорама Unity")]
        public bool steamAudioHrtf = true;
        [BoxGroup("Сравнение вида"), Tooltip("Новые следы от пуль (воронки в бетоне). Выкл — простые тёмные точки. Действует на новые выстрелы")]
        public bool newBulletHoles = true;
        public enum HoleStyle { AsInSurfaceTable, Photo, ScanFlatEntry, ScanFlatExit, ScanCraterEntry, ScanCraterExit, RandomOfAll }
        [BoxGroup("Сравнение вида"), Tooltip("Какой след в бетоне: фото, наклейка из скана (вход / выход пули), объёмная воронка из скана, или каждый раз случайный. " +
                                              "Действует на новые выстрелы")]
        public HoleStyle bulletHoleStyle = HoleStyle.AsInSurfaceTable;
        // Baked light is baked or removed only by hand: Pistol > Bake Lighting / Remove Baked Lighting.
        // (It used to follow a checkbox here, but OnValidate also fires on undo and on reimport after a branch switch,
        // which could strip the bake from the open scene unasked.)
        [ShowNativeProperty]
        string BakedLight => LightmapSettings.lightmaps.Length > 0 ? "есть (Pistol → Remove Baked Lighting)" : "нет (Pistol → Bake Lighting)";

        // Editor and Development builds only
        public static bool Allowed => Debug.isDebugBuild;

        public bool WeaponEvents => Allowed && weaponEvents;
        public bool ItemIntents => Allowed && itemIntents;
        public bool SoundPlays => Allowed && soundPlays;
        public bool BulletHits => Allowed && bulletHits;
        public bool BulletPaths => Allowed && bulletPaths;
        public bool TrajectoryPreview => Allowed && trajectoryPreview;
        public bool AimPoint => Allowed && aimPoint;
        public bool InfiniteAmmo => Allowed && infiniteAmmo;
        // In a normal build the new look is always on
        public bool NewPostFx => !Allowed || newPostFx;
        public bool NewSky => !Allowed || newSky;
        public bool NewCamera => !Allowed || newCamera;
        public bool AutoExposure => !Allowed || autoExposure;
        public bool NewAcoustics => !Allowed || (newAcoustics && !AllOld);
        public bool ForceIndoor => Allowed && forceIndoor;
        public bool SteamAudioHrtf => !Allowed || (steamAudioHrtf && !AllOld);
        public bool NewBulletHoles => !Allowed || (newBulletHoles && !AllOld);
        // -1 = as in the surface table, -2 = random of all, else the index in BulletSystem's style list
        public int BulletHoleStyleIndex => !Allowed ? -1 :
            bulletHoleStyle == HoleStyle.AsInSurfaceTable ? -1 : bulletHoleStyle == HoleStyle.RandomOfAll ? -2 : (int)bulletHoleStyle - 1;

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
