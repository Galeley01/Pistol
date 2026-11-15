using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.Ballistics
{
    // Cartridge type: ballistics and visuals. A new cartridge (.38 Special, hollow point) is just a new asset,
    // no code needed: Create > Weapons > Ammo Type.
    [CreateAssetMenu(menuName = "Weapons/Ammo Type", fileName = "Ammo_357")]
    public class AmmoType : ScriptableObject
    {
        [BoxGroup("Патрон")] public string displayName = ".357 Magnum";

        [BoxGroup("Баллистика"), Range(100f, 1000f), Tooltip("Скорость пули у дула, м/с. .357 Magnum ≈ 440")]
        public float muzzleVelocity = 440f;
        [BoxGroup("Баллистика"), Range(0f, 3f), Tooltip("Множитель гравитации для пули. 1 = как в жизни")]
        public float gravityScale = 1f;
        [BoxGroup("Баллистика"), Range(0.001f, 0.05f), Tooltip("Масса пули, кг (для толчка предметов). 158 гран ≈ 0.0102")]
        public float bulletMass = 0.0102f;
        [BoxGroup("Баллистика"), Range(0.5f, 10f), Tooltip("Сколько секунд пуля живёт в полёте")]
        public float maxLifetime = 3f;

        [BoxGroup("Следы"), Range(0.003f, 0.03f), Tooltip("Диаметр отметины от пули, м")]
        public float holeSize = 0.009f;

        [BoxGroup("Вид в барабане"), Tooltip("Целый патрон (можно прямо FBX Cartridge_357). Если пусто — заглушка-цилиндр")]
        public GameObject livePrefab;
        [BoxGroup("Вид в барабане"), Tooltip("Стреляная гильза. Если пусто — берётся целый патрон без пули (объект Bullet скрывается)")]
        public GameObject spentPrefab;
        [BoxGroup("Вид в барабане"), Tooltip("Оттенок стреляной гильзы (чуть темнее — нагар)")]
        public Color spentTint = new Color(0.72f, 0.68f, 0.62f, 1f);
        [BoxGroup("Вид в барабане"), Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 180f),
         Tooltip("Поворот модели патрона в гнезде. Ось гнезда +Z смотрит от донца к пуле (к дулу). Для Cartridge_357.fbx — 0, 0, 0")]
        public Vector3 modelRotation = Vector3.zero;
        [BoxGroup("Вид в барабане"), Range(0.1f, 5f), Tooltip("0.95 — чтобы патроны не выступали сквозь долы барабана")]
        public float modelScale = 0.95f;

        // Default values when no asset is assigned
        public static AmmoType CreateDefault()
        {
            var a = CreateInstance<AmmoType>();
            a.name = ".357 Magnum (default)";
            return a;
        }
    }
}
