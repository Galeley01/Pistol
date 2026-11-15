using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Вешается на Main Camera, которая лежит внутри CameraRoot.
// Эффекты в стиле Bodycam / Ready or Not: покачивание, удары шагов, тряска при беге,
// толчки при прыжке и приземлении, дыхание и отдышка после бега,
// инерция при повороте, наклон при стрейфе, выглядывание Q/E (сидя — меньше).
[RequireComponent(typeof(Camera))]
public class BodycamEffects : MonoBehaviour
{
    // ===================== Ссылки =====================
    [BoxGroup("Ссылки (найдутся сами)")] public CharacterController controller;
    [BoxGroup("Ссылки (найдутся сами)")] public PlayerMovement movement;

    // ===================== Камера =====================
    [BoxGroup("Камера"), MinValue(30), MaxValue(120)] public float fieldOfView = 90f;

    // ===================== Шаги =====================
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

    // ===================== Нарастание усталости =====================
    [Foldout("Нарастание усталости (перед 8 с)"), InfoBox("Работает с секунды «Fatigue Build Start» (PlayerMovement) до наступления усталости. Сама усталость не меняется.")]
    [Tooltip("Доп. тряска «в руках» к моменту усталости, градусы")]
    public float preFatigueShake = 0.8f;
    [Foldout("Нарастание усталости (перед 8 с)"), Tooltip("Доп. мелкая дрожь к моменту усталости")]
    public float preFatigueJitter = 0.35f;
    [Foldout("Нарастание усталости (перед 8 с)"), Tooltip("Доп. сила толчка шагов к моменту усталости")]
    public float preFatigueStepImpact = 0.1f;
    [Foldout("Нарастание усталости (перед 8 с)"), Range(0f, 1f), Tooltip("Насколько покачивание и темп шагов приближаются к усталому бегу")]
    public float preFatigueBobBlend = 0.4f;

    // ===================== Тряска =====================
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

    // ===================== Дыхание =====================
    [Foldout("Дыхание и отдышка")] public float breathSpeed = 1.3f;
    [Foldout("Дыхание и отдышка")] public float breathAmount = 0.006f;
    [Foldout("Дыхание и отдышка")] public float breathPitch = 0.3f;
    [Foldout("Дыхание и отдышка")] public float heavyBreathSpeed = 6f;
    [Foldout("Дыхание и отдышка")] public float heavyBreathAmount = 0.025f;
    [Foldout("Дыхание и отдышка")] public float heavyBreathPitch = 1.5f;
    [Foldout("Дыхание и отдышка")] public float breathlessShake = 1.4f;

    // ===================== Мышь и стрейф =====================
    [Foldout("Инерция мыши и стрейф")] public float swayAmount = 0.12f;
    [Foldout("Инерция мыши и стрейф")] public float swayMaxAngle = 5f;
    [Foldout("Инерция мыши и стрейф")] public float swaySmooth = 8f;
    [Foldout("Инерция мыши и стрейф")] public float strafeTilt = 2.5f;

    // ===================== Выглядывание =====================
    [Foldout("Отдача камеры"), Range(0.01f, 0.15f), Tooltip("За сколько секунд голову подбрасывает до верхней точки")]
    public float recoilRiseTime = 0.04f;
    [Foldout("Отдача камеры"), Range(0.05f, 0.6f), Tooltip("За сколько секунд голова почти вернулась (на 90%)")]
    public float recoilReturnTime = 0.18f;

    [Foldout("Выглядывание Q / E")] public float leanAngle = 14f;
    [Foldout("Выглядывание Q / E")] public float leanOffset = 0.4f;
    [Foldout("Выглядывание Q / E"), Tooltip("Меньше = медленнее")] public float leanSpeed = 4f;
    [Foldout("Выглядывание Q / E"), Range(0f, 1f)] public float crouchLeanMultiplier = 0.7f;
    [Foldout("Выглядывание Q / E")] public LayerMask leanCollisionMask = ~0;

    // ===================== Общее =====================
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 10f), Tooltip("Наклон камеры вперёд на бегу, градусы")]
    public float runForwardLean = 3f;
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 3f), Tooltip("Поворот корпуса от шага к шагу при ходьбе, градусы")]
    public float walkTorsoTwist = 0.6f;
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 8f), Tooltip("Поворот корпуса от шага к шагу на бегу, градусы")]
    public float runTorsoTwist = 2.4f;
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 0.06f), Tooltip("Смещение камеры вбок за поворотом корпуса на бегу, м")]
    public float runTorsoShift = 0.02f;
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 2f), Tooltip("Кивок камеры при торможении / откидывание при разгоне, градусов на м/с²")]
    public float accelerationNod = 0.55f;
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 1f), Tooltip("Крен при резком смещении вбок, градусов на м/с²")]
    public float accelerationRoll = 0.25f;
    [Foldout("Тело на бегу (как в Bodycam)"), Range(0f, 15f)] public float maxNod = 6f;
    [Foldout("Тело на бегу (как в Bodycam)"), Tooltip("Применять наклон и поворот корпуса и к бегу в усталости (по умолчанию нет — он и так хорош)")]
    public bool bodyEffectsWhenTired = false;

    [BoxGroup("В прицеле"), Range(0f, 1f), Tooltip("Сколько тряски и покачивания камеры остаётся при прицеливании (оружие качается само)")]
    public float aimCameraShake = 0.35f;

    [BoxGroup("Общая плавность")] public float smoothing = 14f;

    Camera cam;
    float bobPhase, bobWeight, runWeight, tiredWeight;
    int lastStep;
    float breathPhase, shakeTime, impactTime;
    float impact, jumpKick;
    float stepImpact, stepImpactTime;
    Vector3 shakeFiltered;
    Vector3 sway;
    float strafe, landing, lean;
    float leanLimitLeft, leanLimitRight;
    bool wasGrounded = true;
    float lastAirVelocity;
    Vector3 posCurrent, rotCurrent;
    WeaponAim weaponAim;
    Vector2 nod, nodVel;          // пружина кивка: x — вперёд/назад, y — крен
    Vector3 recoilCam, recoilCamVel;   // отдача: x — вверх, z — крен, градусы
    float bodyRun;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (controller == null) controller = GetComponentInParent<CharacterController>();
        if (movement == null) movement = GetComponentInParent<PlayerMovement>();
        weaponAim = GetComponentInChildren<WeaponAim>(true);

        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.01f;
        leanLimitLeft = leanLimitRight = leanOffset;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || controller == null) return;

        Vector3 localVel = controller.transform.InverseTransformDirection(controller.velocity);
        float hSpeed = new Vector2(localVel.x, localVel.z).magnitude;
        bool grounded = controller.isGrounded;

        float walk      = movement != null ? movement.walkSpeed : 3f;
        float run       = movement != null ? movement.runSpeed : 6f;
        bool running    = movement != null && movement.IsRunning;
        bool exhausted  = movement != null && movement.IsExhausted;
        float crouch    = movement != null ? movement.CrouchAmount : 0f;
        float breathless = movement != null ? movement.Breathless : 0f;
        float preFatigue = movement != null ? movement.PreFatigue : 0f;

        // ---------- Покачивание ----------
        float moveFactor = grounded ? Mathf.Clamp01(hSpeed / walk) : 0f;
        bobWeight = Damp(bobWeight, moveFactor, 8f, dt);
        runWeight = Damp(runWeight, running && grounded ? 1f : 0f, 5f, dt);
        tiredWeight = Damp(tiredWeight, running && exhausted ? 1f : 0f, 3f, dt);

        // Нарастание усталости (с 6-й по 8-ю секунду бега): бег плавно «тяжелеет».
        // pre — сила нарастания, fade — гасит доп. эффекты, когда наступила усталость,
        // чтобы бег в усталости остался ровно таким, как был.
        float pre = preFatigue * runWeight;
        float fade = 1f - tiredWeight;
        float tiredMix = Mathf.Max(tiredWeight, pre * preFatigueBobBlend);

        // Темп шагов: ходьба и бег — реалистичный, широкий шаг; бег в усталости — как раньше
        float normalRate = hSpeed < walk
            ? walkStepRate * Mathf.Lerp(0.75f, 1f, hSpeed / walk)
            : Mathf.Lerp(walkStepRate, runStepRate, Mathf.InverseLerp(walk, run, hSpeed));
        float tiredRate = hSpeed / Mathf.Max(tiredStepLength, 0.1f);
        float stepRate = Mathf.Lerp(normalRate, tiredRate, tiredMix);
        bobPhase += Mathf.PI * stepRate * dt * Mathf.Clamp01(hSpeed / 0.3f);   // π за один шаг

        // Профиль покачивания: ходьба → бег → бег в усталости
        float v  = Mathf.Lerp(Mathf.Lerp(walkBobVertical,   runBobVertical,   runWeight), tiredBobVertical,   tiredMix);
        float h  = Mathf.Lerp(Mathf.Lerp(walkBobHorizontal, runBobHorizontal, runWeight), tiredBobHorizontal, tiredMix);
        float r  = Mathf.Lerp(Mathf.Lerp(walkBobRoll,       runBobRoll,       runWeight), tiredBobRoll,       tiredMix);
        float p  = Mathf.Lerp(Mathf.Lerp(walkBobPitch,      runBobPitch,      runWeight), tiredBobPitch,      tiredMix);

        // Форма движения вверх-вниз: при ходьбе мягкая волна, в усталости — резкое «проседание» как раньше
        float sharpness = Mathf.Max(runWeight * 0.5f, tiredMix);
        float vShape = Mathf.Lerp(Mathf.Cos(bobPhase * 2f), 1f - 2f * Mathf.Abs(Mathf.Sin(bobPhase)), sharpness);

        float aimCalm = Mathf.Lerp(1f, aimCameraShake, weaponAim != null ? weaponAim.AimAmount : 0f);
        float amp = bobWeight * Mathf.Lerp(1f, crouchBobMultiplier, crouch) * Mathf.Lerp(1f, 0.6f, 1f - aimCalm);
        Vector3 bobPos = new Vector3(Mathf.Sin(bobPhase) * h, vShape * v, 0f) * amp;
        Vector3 bobRot = new Vector3(Mathf.Sin(bobPhase * 2f) * p, 0f, Mathf.Sin(bobPhase) * r) * amp;

        // ---------- Тело на бегу: наклон вперёд и поворот корпуса от шага к шагу ----------
        float bodyMask = bodyEffectsWhenTired ? 1f : 1f - tiredWeight;
        bodyRun = Damp(bodyRun, running && grounded ? 1f : 0f, 3f, dt);
        float twist = Mathf.Lerp(walkTorsoTwist, runTorsoTwist, runWeight) * bobWeight * bodyMask
                    * Mathf.Lerp(1f, crouchBobMultiplier, crouch);
        bobRot += new Vector3(runForwardLean * bodyRun * bodyMask, Mathf.Sin(bobPhase) * twist, 0f);
        bobPos += new Vector3(Mathf.Sin(bobPhase) * runTorsoShift * runWeight * bobWeight * bodyMask, 0f, 0f);

        // ---------- Кивок при торможении / откидывание при разгоне (пружина) ----------
        Vector3 acc = movement != null ? movement.LocalAcceleration : Vector3.zero;
        Vector2 nodTarget = new Vector2(Mathf.Clamp(-acc.z * accelerationNod, -maxNod, maxNod),
                                        Mathf.Clamp(-acc.x * accelerationRoll, -maxNod, maxNod));
        nodVel += ((nodTarget - nod) * 90f - nodVel * 11f) * dt;
        nod += nodVel * dt;
        bobRot += new Vector3(nod.x, 0f, nod.y);

        // ---------- Удар каждого шага ----------
        int step = Mathf.FloorToInt(bobPhase / Mathf.PI);
        if (step != lastStep && grounded && bobWeight > 0.4f)
            stepImpact = Mathf.Min(1f, stepImpact
                + (Mathf.Lerp(stepImpactWalk, stepImpactRun, runWeight) + preFatigueStepImpact * pre * fade)
                  * Mathf.Lerp(1f, 0.5f, crouch));
        lastStep = step;

        // ---------- Дыхание и отдышка ----------
        float idle = 1f - bobWeight;
        breathPhase += dt * Mathf.Lerp(breathSpeed, heavyBreathSpeed, breathless);
        float breath = Mathf.Sin(breathPhase);
        Vector3 breathPos = new Vector3(0f, breath * (breathAmount * idle + heavyBreathAmount * breathless), 0f);
        Vector3 breathRot = new Vector3(breath * (breathPitch * idle + heavyBreathPitch * breathless), 0f, 0f);

        // ---------- Инерция при повороте мышью ----------
        // Реальный поворот камеры (с учётом Free-Aim): пока оружие ходит в мёртвой зоне, камера не раскачивается
        Vector2 look = movement != null ? movement.AppliedLookDelta
                     : (Cursor.lockState == CursorLockMode.Locked ? ReadLook() : Vector2.zero);
        Vector3 swayTarget = new Vector3(
            Mathf.Clamp(look.y * swayAmount, -swayMaxAngle, swayMaxAngle),
            Mathf.Clamp(-look.x * swayAmount * 0.5f, -swayMaxAngle, swayMaxAngle),
            Mathf.Clamp(-look.x * swayAmount, -swayMaxAngle, swayMaxAngle));
        sway = Vector3.Lerp(sway, swayTarget, 1f - Mathf.Exp(-swaySmooth * dt));

        // ---------- Наклон при стрейфе ----------
        strafe = Damp(strafe, -Mathf.Clamp(localVel.x / walk, -1f, 1f) * strafeTilt, 6f, dt);

        // ---------- Прыжок ----------
        if (movement != null && movement.JumpedThisFrame)
        {
            AddImpact(jumpImpact);
            jumpKick = 1f;
        }
        jumpKick = Damp(jumpKick, 0f, 5f, dt);

        // ---------- Приземление ----------
        if (!grounded) lastAirVelocity = controller.velocity.y;
        if (grounded && !wasGrounded && lastAirVelocity < -2f)
        {
            landing = Mathf.Min(landingMax, -lastAirVelocity * landingStrength);
            AddImpact(-lastAirVelocity * landingImpact);
        }
        wasGrounded = grounded;
        landing = Damp(landing, 0f, landingRecover, dt);
        Vector3 landPos = new Vector3(0f, -landing, 0f);
        Vector3 landRot = new Vector3(landing * 20f - jumpKick * jumpKickAngle, 0f, 0f);

        // ---------- Выглядывание Q / E (сидя — меньше) ----------
        float leanMul = Mathf.Lerp(1f, crouchLeanMultiplier, crouch);
        float maxLean = leanOffset * leanMul;
        float leanInput = movement != null
            ? movement.LeanInput
            : (ReadLeanRight() ? 1f : 0f) - (ReadLeanLeft() ? 1f : 0f);
        UpdateLeanLimits(maxLean);
        lean = Damp(lean, leanInput, leanSpeed, dt);
        float limit = lean > 0f ? leanLimitRight : leanLimitLeft;
        float limitRatio = maxLean > 0.001f ? limit / maxLean : 0f;
        float leanDist = lean * limit;
        Vector3 leanPos = new Vector3(leanDist, -Mathf.Abs(leanDist) * 0.15f, 0f);
        Vector3 leanRot = new Vector3(0f, 0f, -lean * leanAngle * leanMul * limitRatio);

        // ---------- Плавная часть ----------
        Vector3 targetPos = bobPos + breathPos + landPos + leanPos;
        Vector3 targetRot = bobRot + breathRot + sway + landRot + leanRot + new Vector3(0f, 0f, strafe);
        posCurrent = Vector3.Lerp(posCurrent, targetPos, 1f - Mathf.Exp(-smoothing * dt));
        rotCurrent = Vector3.Lerp(rotCurrent, targetRot, 1f - Mathf.Exp(-smoothing * dt));

        // ---------- Тряска (добавляется без сглаживания, чтобы была резкой) ----------
        // 1) «в руках»: сильнее при ходьбе, ещё сильнее при беге и отдышке
        float shakeAmt = shakeIdle
                       + (shakeMoving - shakeIdle) * bobWeight
                       + (shakeRunning - shakeMoving) * runWeight * bobWeight
                       + breathlessShake * breathless
                       + preFatigueShake * pre * fade;
        shakeTime += dt * shakeSpeed * (1f + breathless * 1.5f + runWeight * runShakeSpeedBoost);
        Vector3 shakeRot = Noise3(shakeTime, 0f) * shakeAmt;

        // 2) мелкая дрожь при беге
        Vector3 jitterRot = Noise3(Time.time * runJitterSpeed, 50f)
                          * (runJitter * runWeight + preFatigueJitter * pre * fade) * bobWeight;

        // 3) мягкие толчки шагов
        stepImpact = Mathf.Max(0f, stepImpact - stepImpactDecay * dt);
        stepImpactTime += dt * stepImpactSpeed;
        Vector3 stepRot = Noise3(stepImpactTime, 300f) * stepImpactAngle * stepImpact;

        // Пока бежишь и ещё не устал — сглаживаем тряску, чтобы не дёргалась резко.
        // После усталости фильтр отключается и тряска снова резкая.
        // С 6-й секунды фильтр постепенно ослабевает — тряска становится резче.
        float calmRun = exhausted ? 0f : runWeight * (1f - preFatigue);
        float filterSpeed = Mathf.Lerp(60f, runShakeSmoothing, calmRun);
        shakeFiltered = Vector3.Lerp(shakeFiltered, shakeRot + jitterRot + stepRot,
                                     1f - Mathf.Exp(-filterSpeed * dt));

        // 4) резкие толчки от прыжка и приземления (без сглаживания)
        impact = Mathf.Max(0f, impact - impactDecay * dt);
        impactTime += dt * impactSpeed;
        float imp = impact * impact;
        Vector3 impactRot = Noise3(impactTime, 100f) * impactMaxAngle * imp;
        Vector3 impactPos = Noise3(impactTime, 200f) * impactMaxOffset * imp;

        // 5) отдача — резко и без сглаживания, иначе она «плывёт»
        RecoilSpring.Step(ref recoilCam, ref recoilCamVel, recoilRiseTime, recoilReturnTime, 1f, dt);
        Vector3 recoilRot = new Vector3(-recoilCam.x, 0f, recoilCam.z);

        transform.localPosition = posCurrent + impactPos;
        transform.localRotation = Quaternion.Euler(rotCurrent + shakeFiltered * aimCalm + impactRot + recoilRot);
    }

    void AddImpact(float amount) => impact = Mathf.Min(1f, impact + amount);

    // ---------- Для отдачи и других толчков извне ----------
    // Резкая тряска камеры (0..1)
    public void AddShake(float amount) => AddImpact(amount);
    // Толчок «кивка»: камеру подбрасывает вверх (°/с) и слегка кренит (°/с), затем пружина возвращает
    public void AddRecoilKick(float pitchUpVelocity, float rollVelocity) => nodVel += new Vector2(-pitchUpVelocity, rollVelocity);
    // Короткий резкий толчок головы от выстрела: верхняя точка в градусах (вверх, крен)
    public void AddRecoilPunch(float pitchUpDegrees, float rollDegrees)
        => recoilCamVel += new Vector3(pitchUpDegrees, 0f, rollDegrees) * RecoilSpring.ImpulseFor(1f, recoilRiseTime);

    // Шум Перлина в диапазоне -1..1 по трём осям
    static Vector3 Noise3(float t, float seed) => new Vector3(
        Mathf.PerlinNoise(t, seed + 0.1f) * 2f - 1f,
        Mathf.PerlinNoise(seed + 3.3f, t) * 2f - 1f,
        Mathf.PerlinNoise(t, seed + 7.7f) * 2f - 1f);

    // Не даём выглядывать сквозь стены
    void UpdateLeanLimits(float maxLean)
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        leanLimitRight = CastLean(root, root.right, maxLean);
        leanLimitLeft = CastLean(root, -root.right, maxLean);
    }

    float CastLean(Transform root, Vector3 dir, float maxLean)
    {
        const float radius = 0.15f;
        if (Physics.SphereCast(root.position, radius, dir, out RaycastHit hit, maxLean,
                leanCollisionMask, QueryTriggerInteraction.Ignore)
            && hit.collider != controller)
            return Mathf.Max(0f, hit.distance - 0.05f);
        return maxLean;
    }

    static float Damp(float current, float target, float speed, float dt)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));

    // ---------- Ввод ----------
#if ENABLE_INPUT_SYSTEM
    Vector2 ReadLook()     => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
    bool ReadLeanLeft()    => Keyboard.current != null && Keyboard.current.qKey.isPressed;
    bool ReadLeanRight()   => Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
    Vector2 ReadLook()     => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
    bool ReadLeanLeft()    => Input.GetKey(KeyCode.Q);
    bool ReadLeanRight()   => Input.GetKey(KeyCode.E);
#endif
}