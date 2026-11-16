using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.Weapons
{
    // Aiming tuning for WeaponAim: hip and aim positions, raise-to-face timing, aim FOV.
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Profiles/Weapon Aim", fileName = "AimProfile")]
    public class AimProfile : ScriptableObject
    {
        [BoxGroup("Положение оружия"), Tooltip("Вкл: обычным положением считается то, где револьвер стоит в сцене. Ничего настраивать не нужно")]
        public bool useScenePosition = true;

        [BoxGroup("Положение оружия"), HideIf("useScenePosition")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.5f), Tooltip("Обычное положение (от бедра)")]
        public Vector3 hipPosition = new Vector3(0.06f, -0.12f, 0.3f);

        [BoxGroup("Положение оружия")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.5f),
         Tooltip("Положение при прицеливании. X = 0 — ровно по центру. Y — опустите так, чтобы мушка была в центре экрана")]
        public Vector3 aimPosition = new Vector3(0f, -0.055f, 0.35f);

        [BoxGroup("Положение оружия"), Range(0.15f, 0.6f), Tooltip("Расстояние от глаза до целика в прицеле, м. Больше = руки вытянуты, оружие меньше на экране")]
        public float aimDistance = 0.36f;

        [BoxGroup("Подъём к лицу"), Range(0.08f, 1f), Tooltip("За сколько секунд оружие поднимается к лицу у отдохнувшего персонажа")]
        public float aimInTime = 0.24f;
        [BoxGroup("Подъём к лицу"), Range(0.1f, 2f), Tooltip("За сколько секунд — у измотанного (после бега, при отдышке)")]
        public float tiredAimInTime = 0.6f;
        [BoxGroup("Подъём к лицу"), Range(0f, 0.6f), Tooltip("Сколько секунд добавляется, если поднимать оружие прямо с бега")]
        public float fromSprintExtraTime = 0.15f;
        [BoxGroup("Подъём к лицу"), Range(0.05f, 1f), Tooltip("За сколько секунд оружие опускается из прицела")]
        public float aimOutTime = 0.18f;
        [BoxGroup("Подъём к лицу"), Range(0f, 25f), Tooltip("Крен оружия на середине подъёма, градусы")]
        public float transitionRoll = 8f;
        [BoxGroup("Подъём к лицу"), Range(0f, 0.05f), Tooltip("Оружие идёт к лицу по дуге снизу, м")]
        public float transitionDip = 0.015f;
        [BoxGroup("Подъём к лицу"), Range(0f, 40f), Tooltip("Толчок, когда оружие упирается в плечо/встаёт у глаза (у уставшего сильнее)")]
        public float settleKick = 10f;

        [BoxGroup("Камера")] public bool changeFov = true;
        [BoxGroup("Камера"), ShowIf("changeFov"), Range(40f, 100f), Tooltip("Угол обзора при прицеливании")]
        public float aimFov = 75f;
    }
}
