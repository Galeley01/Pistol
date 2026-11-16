using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Cameras
{
    // Camera motion tuning for CameraMotion: head bob, steps, shake, jolts, breathing, leaning, body while running.
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Profiles/Camera Motion", fileName = "CameraMotionProfile")]
    public class CameraMotionProfile : ScriptableObject
    {
        // ===================== Camera =====================
        [BoxGroup("Камера"), MinValue(30), MaxValue(120)] public float fieldOfView = 90f;

        // ===================== Steps =====================
        [Foldout("Темп шагов"), Tooltip("Шагов в секунду при обычной ходьбе")]
        public float walkStepRate = 1.9f;
        [Foldout("Темп шагов"), Tooltip("Шагов в секунду при беге (меньше = шире шаг)")]
        public float runStepRate = 2.6f;
        [Foldout("Темп шагов"), Tooltip("Длина шага при беге в усталости")]
        public float tiredStepLength = 0.7f;

        [Foldout("Покачивание: ходьба")] public float walkBobVertical = 0.022f;
        [Foldout("Покачивание: ходьба")] public float walkBobHorizontal = 0.008f;
        [Foldout("Покачивание: ходьба")] public float walkBobRoll = 0.4f;
        [Foldout("Покачивание: ходьба")] public float walkBobPitch = 0.5f;
        [Foldout("Покачивание: ходьба"), Range(0f, 1f)] public float crouchBobMultiplier = 0.6f;

        [Foldout("Покачивание: бег")] public float runBobVertical = 0.055f;
        [Foldout("Покачивание: бег")] public float runBobHorizontal = 0.02f;
        [Foldout("Покачивание: бег")] public float runBobRoll = 1f;
        [Foldout("Покачивание: бег")] public float runBobPitch = 1.3f;

        [Foldout("Покачивание: бег в усталости")] public float tiredBobVertical = 0.081f;
        [Foldout("Покачивание: бег в усталости")] public float tiredBobHorizontal = 0.054f;
        [Foldout("Покачивание: бег в усталости")] public float tiredBobRoll = 2.7f;
        [Foldout("Покачивание: бег в усталости")] public float tiredBobPitch = 1.8f;

        // ===================== Fatigue build-up =====================
        [Foldout("Нарастание усталости (перед 8 с)"), InfoBox("Работает с секунды «Fatigue Build Start» (PlayerMovement) до наступления усталости. Сама усталость не меняется.")]
        [Tooltip("Доп. тряска «в руках» к моменту усталости, градусы")]
        public float preFatigueShake = 0.8f;
        [Foldout("Нарастание усталости (перед 8 с)"), Tooltip("Доп. мелкая дрожь к моменту усталости")]
        public float preFatigueJitter = 0.35f;
        [Foldout("Нарастание усталости (перед 8 с)"), Tooltip("Доп. сила толчка шагов к моменту усталости")]
        public float preFatigueStepImpact = 0.1f;
        [Foldout("Нарастание усталости (перед 8 с)"), Range(0f, 1f), Tooltip("Насколько покачивание и темп шагов приближаются к усталому бегу")]
        public float preFatigueBobBlend = 0.4f;

        // ===================== Shake =====================
        [Foldout("Удары шагов")] public float stepImpactWalk = 0.05f;
        [Foldout("Удары шагов")] public float stepImpactRun = 0.18f;
        [Foldout("Удары шагов")] public float stepImpactAngle = 3f;
        [Foldout("Удары шагов"), Tooltip("Меньше = плавнее толчок шага")] public float stepImpactSpeed = 8f;
        [Foldout("Удары шагов")] public float stepImpactDecay = 3f;

        [Foldout("Тряска «в руках»"), Tooltip("Градусы")] public float shakeIdle = 0.35f;
        [Foldout("Тряска «в руках»")] public float shakeMoving = 0.9f;
        [Foldout("Тряска «в руках»")] public float shakeRunning = 1.4f;
        [Foldout("Тряска «в руках»")] public float shakeSpeed = 1.4f;
        [Foldout("Тряска «в руках»"), Tooltip("Насколько быстрее тряска при беге")] public float runShakeSpeedBoost = 0.3f;
        [Foldout("Тряска «в руках»"), Tooltip("Мелкая дрожь при беге")] public float runJitter = 0.25f;
        [Foldout("Тряска «в руках»")] public float runJitterSpeed = 5f;
        [Foldout("Тряска «в руках»"), Tooltip("Меньше = плавнее. Пока бежишь и ещё не устал")] public float runShakeSmoothing = 7f;

        [Foldout("Толчки: прыжок и приземление")] public float impactMaxAngle = 5f;
        [Foldout("Толчки: прыжок и приземление")] public float impactMaxOffset = 0.03f;
        [Foldout("Толчки: прыжок и приземление")] public float impactSpeed = 22f;
        [Foldout("Толчки: прыжок и приземление")] public float impactDecay = 2.2f;
        [Foldout("Толчки: прыжок и приземление")] public float jumpImpact = 0.45f;
        [Foldout("Толчки: прыжок и приземление")] public float jumpKickAngle = 2.5f;
        [Foldout("Толчки: прыжок и приземление")] public float landingStrength = 0.03f;
        [Foldout("Толчки: прыжок и приземление")] public float landingMax = 0.3f;
        [Foldout("Толчки: прыжок и приземление")] public float landingRecover = 6f;
        [Foldout("Толчки: прыжок и приземление"), Tooltip("Сила толчка на каждый м/с скорости падения")] public float landingImpact = 0.07f;

        // ===================== Breathing =====================
        [Foldout("Дыхание и отдышка")] public float breathSpeed = 1.3f;
        [Foldout("Дыхание и отдышка")] public float breathAmount = 0.006f;
        [Foldout("Дыхание и отдышка")] public float breathPitch = 0.3f;
        [Foldout("Дыхание и отдышка")] public float heavyBreathSpeed = 6f;
        [Foldout("Дыхание и отдышка")] public float heavyBreathAmount = 0.025f;
        [Foldout("Дыхание и отдышка")] public float heavyBreathPitch = 1.5f;
        [Foldout("Дыхание и отдышка")] public float breathlessShake = 1.4f;

        // ===================== Mouse and strafe =====================
        [Foldout("Инерция мыши и стрейф")] public float swayAmount = 0.12f;
        [Foldout("Инерция мыши и стрейф")] public float swayMaxAngle = 5f;
        [Foldout("Инерция мыши и стрейф")] public float swaySmooth = 8f;
        [Foldout("Инерция мыши и стрейф")] public float strafeTilt = 2.5f;

        // ===================== Recoil and leaning =====================
        [Foldout("Отдача камеры"), Range(0.01f, 0.15f), Tooltip("За сколько секунд голову подбрасывает до верхней точки")]
        public float recoilRiseTime = 0.04f;
        [Foldout("Отдача камеры"), Range(0.05f, 0.6f), Tooltip("За сколько секунд голова почти вернулась (на 90%)")]
        public float recoilReturnTime = 0.18f;

        [Foldout("Выглядывание Q / E")] public float leanAngle = 14f;
        [Foldout("Выглядывание Q / E")] public float leanOffset = 0.4f;
        [Foldout("Выглядывание Q / E"), Tooltip("Меньше = медленнее")] public float leanSpeed = 4f;
        [Foldout("Выглядывание Q / E"), Range(0f, 1f)] public float crouchLeanMultiplier = 0.7f;
        [Foldout("Выглядывание Q / E")] public LayerMask leanCollisionMask = ~0;

        // ===================== Body while running =====================
        [Foldout("Тело на бегу"), Range(0f, 10f), Tooltip("Наклон камеры вперёд на бегу, градусы")]
        public float runForwardLean = 3f;
        [Foldout("Тело на бегу"), Range(0f, 3f), Tooltip("Поворот корпуса от шага к шагу при ходьбе, градусы")]
        public float walkTorsoTwist = 0.6f;
        [Foldout("Тело на бегу"), Range(0f, 8f), Tooltip("Поворот корпуса от шага к шагу на бегу, градусы")]
        public float runTorsoTwist = 2.4f;
        [Foldout("Тело на бегу"), Range(0f, 0.06f), Tooltip("Смещение камеры вбок за поворотом корпуса на бегу, м")]
        public float runTorsoShift = 0.02f;
        [Foldout("Тело на бегу"), Range(0f, 2f), Tooltip("Кивок камеры при торможении / откидывание при разгоне, градусов на м/с²")]
        public float accelerationNod = 0.55f;
        [Foldout("Тело на бегу"), Range(0f, 1f), Tooltip("Крен при резком смещении вбок, градусов на м/с²")]
        public float accelerationRoll = 0.25f;
        [Foldout("Тело на бегу"), Range(0f, 15f)] public float maxNod = 6f;
        [Foldout("Тело на бегу"), Tooltip("Применять наклон и поворот корпуса и к бегу в усталости (по умолчанию нет — он и так хорош)")]
        public bool bodyEffectsWhenTired = false;

        [BoxGroup("В прицеле"), Range(0f, 1f), Tooltip("Сколько тряски и покачивания камеры остаётся при прицеливании (оружие качается само)")]
        public float aimCameraShake = 0.35f;

        [BoxGroup("Общая плавность")] public float smoothing = 14f;
    }
}
