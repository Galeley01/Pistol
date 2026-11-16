using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.Weapons
{
    // How a weapon moves in the hands (WeaponSway): weight and inertia, roll, bob, drift, run pose,
    // inspect pose and aiming sway. Wrist and rear sight come from ItemRig; recoil timing lives in RecoilProfile.
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Profiles/Weapon Sway", fileName = "SwayProfile")]
    public class SwayProfile : ScriptableObject
    {
        [BoxGroup("Общее"), Range(0f, 2f), Tooltip("Общая сила раскачивания (на Free-Aim не влияет)")]
        public float masterIntensity = 1f;
        [BoxGroup("Общее"), Range(0f, 1f), Tooltip("Сколько раскачивания остаётся в прицеле")]
        public float aimMultiplier = 0.35f;

        [Foldout("Вес и инерция"), Range(0.3f, 4f), Tooltip("Расчётный вес оружия с руками. Больше = медленнее догоняет взгляд")]
        public float weaponWeight = 1.2f;
        [Foldout("Вес и инерция"), Range(50f, 600f), Tooltip("Сила, с которой руки возвращают оружие к взгляду")]
        public float responsiveness = 220f;
        [Foldout("Вес и инерция"), Range(0.2f, 1.2f), Tooltip("Меньше 1 = с перелётом и покачиванием, 1 = без перелёта")]
        public float dampingRatio = 0.6f;
        [Foldout("Вес и инерция"), Range(2f, 30f), Tooltip("Максимальное отставание оружия от взгляда, градусы")]
        public float maxLag = 14f;

        [Foldout("Крен от скорости поворота"), Range(0f, 0.08f), Tooltip("Градусов крена на каждый °/с скорости поворота")]
        public float rollPerTurnSpeed = 0.025f;
        [Foldout("Крен от скорости поворота"), Range(0f, 30f)] public float maxRoll = 14f;
        [Foldout("Крен от скорости поворота"), Range(0f, 0.04f), Tooltip("Ствол «клюёт» вниз при быстром горизонтальном повороте")]
        public float dipPerTurnSpeed = 0.006f;

        [Foldout("Кисти, а не локти"), Range(0f, 0.003f), Tooltip("Насколько кисть смещается вслед за поворотом, м на градус. 0 = только поворот кисти")]
        public float handShift = 0.0006f;

        [Foldout("Инерция при движении"), Range(0f, 0.01f), Tooltip("Отставание оружия на каждый м/с скорости")]
        public float moveLag = 0.0025f;
        [Foldout("Инерция при движении"), Range(0f, 4f), Tooltip("Крен при движении вбок, градусов на м/с")]
        public float strafeRoll = 1.3f;
        [Foldout("Инерция при движении"), Range(0f, 3f), Tooltip("Ствол чуть опускается при движении вперёд, градусов на м/с")]
        public float forwardDip = 0.6f;

        [Foldout("Покачивание при движении (хаотичное)"), Range(0.5f, 4f)] public float walkStepRate = 1.9f;
        [Foldout("Покачивание при движении (хаотичное)"), Range(0.5f, 4f)] public float runStepRate = 2.6f;
        [Foldout("Покачивание при движении (хаотичное)"), Range(0f, 0.5f), Tooltip("Сила толчка каждого шага на бегу (смещение)")]
        public float stepKickPosition = 0.14f;
        [Foldout("Покачивание при движении (хаотичное)"), Range(0f, 80f), Tooltip("Сила толчка каждого шага на бегу (поворот)")]
        public float stepKickRotation = 28f;
        [Foldout("Покачивание при движении (хаотичное)"), Range(0.5f, 3f), Tooltip("Как резко растёт сила с ростом скорости. Больше = спокойная ходьба ещё мягче, бег ещё сильнее")]
        public float speedCurve = 1.6f;
        [Foldout("Покачивание при движении (хаотичное)"), Range(0f, 0.02f), Tooltip("Неровное «плавание» оружия на бегу (смещение)")]
        public float moveNoisePosition = 0.006f;
        [Foldout("Покачивание при движении (хаотичное)"), Range(0f, 5f), Tooltip("Неровное «плавание» оружия на бегу (поворот)")]
        public float moveNoiseRotation = 1.8f;

        [Foldout("Случайный дрейф рук"), Range(0f, 0.02f)] public float driftPosition = 0.002f;
        [Foldout("Случайный дрейф рук"), Range(0f, 5f)] public float driftRotation = 1.2f;
        [Foldout("Случайный дрейф рук"), Range(0.05f, 2f)] public float driftSpeed = 0.35f;
        [Foldout("Случайный дрейф рук"), Range(1f, 6f), Tooltip("Во сколько раз сильнее дрейф при отдышке")]
        public float breathlessDrift = 3f;

        [Foldout("Прыжок и приземление"), Range(0f, 2f)] public float jumpKick = 0.5f;
        [Foldout("Прыжок и приземление"), Range(0f, 0.2f), Tooltip("Сила удара на каждый м/с скорости падения")]
        public float landKick = 0.09f;

        [Foldout("Оружие при беге"), Tooltip("Опускать оружие при беге")] public bool lowerWhileRunning = true;
        [Foldout("Оружие при беге"), ShowIf("lowerWhileRunning"), Tooltip("Оружие прижато к телу, ствол вниз")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.25f)]
        public Vector3 sprintPosition = new Vector3(-0.05f, -0.09f, -0.08f);
        [Foldout("Оружие при беге"), ShowIf("lowerWhileRunning")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 80f)]
        public Vector3 sprintRotation = new Vector3(35f, -22f, 18f);
        [Foldout("Оружие при беге"), ShowIf("lowerWhileRunning"), Range(0.05f, 1.5f), Tooltip("За сколько секунд оружие уходит в позу бега")]
        public float sprintPoseInTime = 0.3f;
        [Foldout("Оружие при беге"), ShowIf("lowerWhileRunning"), Range(0.05f, 2f), Tooltip("За сколько секунд оружие возвращается в стойку после бега")]
        public float sprintPoseOutTime = 0.6f;
        [Foldout("Оружие при беге"), ShowIf("lowerWhileRunning"), Range(0f, 0.5f), Tooltip("Пауза перед подъёмом оружия после остановки, сек")]
        public float sprintPoseHold = 0.12f;

        [Foldout("Осмотр барабана (Alt)"), Tooltip("Насколько сдвинуть оружие в режиме осмотра: ближе к лицу, задним торцом барабана к глазам")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.25f)]
        public Vector3 inspectPosition = new Vector3(-0.04f, 0.06f, -0.06f);
        [Foldout("Осмотр барабана (Alt)"), Tooltip("Поворот оружия в режиме осмотра. X+ — ствол вниз, Z− — откинутый барабан поднимается")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 90f)]
        public Vector3 inspectRotation = new Vector3(20f, 10f, -35f);
        [Foldout("Осмотр барабана (Alt)"), Range(0f, 1f), Tooltip("Сколько покачивания и дрейфа рук остаётся при осмотре")]
        public float inspectSwayMultiplier = 0.3f;
        [Foldout("Прицеливание"), Range(0f, 1.5f), Tooltip("Сколько инерции (заноса) остаётся в прицеле. 1 = как от бедра")]
        public float aimLagMultiplier = 0.85f;
        [Foldout("Прицеливание"), Range(0f, 2f), Tooltip("Покачивание от дыхания в прицеле у отдохнувшего, градусы")]
        public float aimBreathCalm = 0.15f;
        [Foldout("Прицеливание"), Range(0f, 6f), Tooltip("Покачивание от дыхания в прицеле у измотанного, градусы")]
        public float aimBreathTired = 2.2f;
        [Foldout("Прицеливание"), Range(0f, 2f), Tooltip("Хаотичная дрожь в прицеле у отдохнувшего, градусы")]
        public float aimShakeCalm = 0.1f;
        [Foldout("Прицеливание"), Range(0f, 5f), Tooltip("Хаотичная дрожь в прицеле у измотанного, градусы")]
        public float aimShakeTired = 1.6f;
        [Foldout("Прицеливание"), Range(0f, 0.02f), Tooltip("Насколько поворот в прицеле сбивает хват (на каждый °/с)")]
        public float instabilityGain = 0.005f;
        [Foldout("Прицеливание"), Range(0f, 6f), Tooltip("Сила «гуляния» ствола, пока хват не стабилизировался, градусы")]
        public float instabilityAmount = 2.2f;
        [Foldout("Прицеливание"), Range(0.2f, 3f), Tooltip("За сколько секунд стабилизируется хват после поворота (отдохнувший)")]
        public float stabilizeTime = 0.9f;
        [Foldout("Прицеливание"), Range(0.2f, 5f), Tooltip("За сколько секунд стабилизируется хват после поворота (измотанный)")]
        public float tiredStabilizeTime = 2f;
    }
}
