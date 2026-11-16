using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Core
{
    // All debug switches in one place: logs, bullet paths, the future bullet line, the aim point.
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

        // Editor and Development builds only
        public static bool Allowed => Debug.isDebugBuild;

        public bool WeaponEvents => Allowed && weaponEvents;
        public bool ItemIntents => Allowed && itemIntents;
        public bool SoundPlays => Allowed && soundPlays;
        public bool BulletHits => Allowed && bulletHits;
        public bool BulletPaths => Allowed && bulletPaths;
        public bool TrajectoryPreview => Allowed && trajectoryPreview;
        public bool AimPoint => Allowed && aimPoint;
    }
}
