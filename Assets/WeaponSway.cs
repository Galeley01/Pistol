using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// «Живое» оружие в руках в стиле Bodycam.
// • Вес и инерция: при повороте камеры оружие остаётся на месте в мире и догоняет взгляд с задержкой
//   (пружина с массой), с лёгким перелётом.
// • Крен от скорости поворота: чем быстрее поворот, тем сильнее ствол заваливается по оси Z в сторону поворота.
// • Вращение вокруг запястья: ствол уходит заметно дальше рукояти — ощущение, что двигаются кисти, а не локти.
// • Free-Aim: оружие поворачивается на угол FreeAimOffset из PlayerMovement (мёртвая зона).
// • Покачивание при ходьбе, случайный дрейф рук, толчки прыжка/приземления, опущенное оружие при беге.
//
// Вешается на Revolver. При запуске сам вставляет WeaponSwayPivot между камерой и револьвером.
public class WeaponSway : MonoBehaviour
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

    [Foldout("Кисти, а не локти"), Tooltip("Взять точку запястья из рига рук (IK_RightHand)")]
    public bool wristFromRig = true;
    [Foldout("Кисти, а не локти"), HideIf("wristFromRig")]
    [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.2f), Tooltip("Вокруг какой точки (относительно Revolver) вращается оружие")]
    public Vector3 wristPivot = new Vector3(0.015f, -0.03f, -0.07f);
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

    [Foldout("Прицеливание (как в Squad)"), Range(0f, 1.5f), Tooltip("Сколько инерции (заноса) остаётся в прицеле. 1 = как от бедра")]
    public float aimLagMultiplier = 0.85f;
    [Foldout("Прицеливание (как в Squad)"), Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.15f)]
    [Tooltip("Точка у целика (относительно Revolver). В прицеле оружие вращается вокруг неё: целик у глаза, а мушка гуляет")]
    public Vector3 rearSightPoint = new Vector3(0f, 0.055f, 0.01f);
    [Foldout("Прицеливание (как в Squad)"), Range(0f, 2f), Tooltip("Покачивание от дыхания в прицеле у отдохнувшего, градусы")]
    public float aimBreathCalm = 0.15f;
    [Foldout("Прицеливание (как в Squad)"), Range(0f, 6f), Tooltip("Покачивание от дыхания в прицеле у измотанного, градусы")]
    public float aimBreathTired = 2.2f;
    [Foldout("Прицеливание (как в Squad)"), Range(0f, 2f), Tooltip("Хаотичная дрожь в прицеле у отдохнувшего, градусы")]
    public float aimShakeCalm = 0.1f;
    [Foldout("Прицеливание (как в Squad)"), Range(0f, 5f), Tooltip("Хаотичная дрожь в прицеле у измотанного, градусы")]
    public float aimShakeTired = 1.6f;
    [Foldout("Прицеливание (как в Squad)"), Range(0f, 0.02f), Tooltip("Насколько поворот в прицеле сбивает хват (на каждый °/с)")]
    public float instabilityGain = 0.005f;
    [Foldout("Прицеливание (как в Squad)"), Range(0f, 6f), Tooltip("Сила «гуляния» ствола, пока хват не стабилизировался, градусы")]
    public float instabilityAmount = 2.2f;
    [Foldout("Прицеливание (как в Squad)"), Range(0.2f, 3f), Tooltip("За сколько секунд стабилизируется хват после поворота (отдохнувший)")]
    public float stabilizeTime = 0.9f;
    [Foldout("Прицеливание (как в Squad)"), Range(0.2f, 5f), Tooltip("За сколько секунд стабилизируется хват после поворота (измотанный)")]
    public float tiredStabilizeTime = 2f;

    [Foldout("Точка прицела"), Tooltip("Маленькая точка там, куда смотрит ствол (удобно с Free-Aim)")]
    public bool showAimPoint = false;
    [Foldout("Точка прицела"), ShowIf("showAimPoint"), Range(2f, 12f)] public float aimPointSize = 4f;

    Transform pivot;
    Camera cam;
    Transform muzzle;
    PlayerMovement movement;
    CharacterController controller;
    WeaponAim weaponAim;

    // Отставание оружия от взгляда: x — наклон (вниз +), y — поворот (вправо +), z — крен
    Vector3 lag, lagVel;
    Vector3 posSpring, posVel;
    Vector2 lastFreeAim;
    float worldYawRate;
    float bobPhase, runWeight, runPose, driftTime, moveNoiseTime, sprintStoppedTime;
    int lastStep;
    bool wasGrounded = true;
    float lastAirVelocity;
    float aimBreathPhase, aimShakeTime, instability, instabilityTime;

    // Толчок извне (например, оружие встало у глаза): скорость поворота (°/с) и смещения (м/с)
    public void AddKick(Vector3 rotationVelocity, Vector3 positionVelocity)
    {
        lagVel += rotationVelocity;
        posVel += positionVelocity;
    }

    [ShowNativeProperty] public float Instability => instability;

    void Awake()
    {
        pivot = new GameObject("WeaponSwayPivot").transform;
        pivot.SetParent(transform.parent, false);
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        transform.SetParent(pivot, false);

        cam = GetComponentInParent<Camera>();
        movement = GetComponentInParent<PlayerMovement>();
        controller = movement != null ? movement.GetComponent<CharacterController>()
                                      : GetComponentInParent<CharacterController>();
        weaponAim = GetComponent<WeaponAim>();
        var fire = GetComponent<RevolverFire>();
        muzzle = fire != null && fire.muzzle != null ? fire.muzzle : transform.Find("Muzzle");
    }

    void Start()
    {
        if (wristFromRig)
        {
            var hand = transform.Find("IK_RightHand");
            if (hand != null) wristPivot = hand.localPosition;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float aim = weaponAim != null ? weaponAim.AimAmount : 0f;
        float k = masterIntensity * Mathf.Lerp(1f, aimMultiplier, aim);      // покачивание, дрейф
        float kLag = masterIntensity * Mathf.Lerp(1f, aimLagMultiplier, aim); // инерция и занос

        // ---------- Движение игрока ----------
        Vector3 localVel = Vector3.zero;
        bool grounded = true;
        if (controller != null)
        {
            localVel = controller.transform.InverseTransformDirection(controller.velocity);
            grounded = controller.isGrounded;
        }
        float hSpeed = new Vector2(localVel.x, localVel.z).magnitude;
        float walk = movement != null ? movement.walkSpeed : 3f;
        float run = movement != null ? movement.runSpeed : 6f;
        bool running = movement != null && movement.IsRunning;
        float breathless = movement != null ? movement.Breathless : 0f;

        // ---------- Куда должно смотреть оружие (Free-Aim) и как повернулся взгляд ----------
        Vector2 freeAim = movement != null ? movement.FreeAimOffset : Vector2.zero;
        Vector2 camDelta = movement != null ? movement.AppliedLookDelta
                         : (Cursor.lockState == CursorLockMode.Locked ? ReadLook() : Vector2.zero);
        Vector2 aimDelta = camDelta + (freeAim - lastFreeAim);   // полный поворот «цели» в мире за кадр
        lastFreeAim = freeAim;

        // ---------- 1. Вес: оружие остаётся на месте в мире, цель уходит вперёд ----------
        lag.y -= aimDelta.x;
        lag.x += aimDelta.y;
        lag.x = Mathf.Clamp(lag.x, -maxLag, maxLag);
        lag.y = Mathf.Clamp(lag.y, -maxLag, maxLag);

        // ---------- 2. Крен от скорости поворота (в сторону поворота) ----------
        float yawRate = aimDelta.x / dt + lagVel.y;   // как быстро ствол поворачивается в мире
        worldYawRate = Mathf.Lerp(worldYawRate, yawRate, 1f - Mathf.Exp(-12f * dt));
        float rollTarget = Mathf.Clamp(-worldYawRate * rollPerTurnSpeed, -maxRoll, maxRoll);
        float dipTarget = Mathf.Min(Mathf.Abs(worldYawRate) * dipPerTurnSpeed, maxRoll * 0.5f);

        // ---------- 3. Цели пружины от движения ----------
        Vector3 lagTarget = new Vector3(dipTarget + localVel.z * forwardDip, 0f, rollTarget - localVel.x * strafeRoll);
        Vector3 posTarget = -localVel * moveLag;

        // ---------- 4. Прыжок и приземление ----------
        if (movement != null && movement.JumpedThisFrame)
        {
            posVel += new Vector3(0f, -0.2f, 0f) * jumpKick;
            lagVel += new Vector3(-40f, 0f, 10f) * jumpKick;
        }
        if (controller != null)
        {
            if (!grounded) lastAirVelocity = controller.velocity.y;
            if (grounded && !wasGrounded && lastAirVelocity < -2f)
            {
                float f = Mathf.Min(-lastAirVelocity * landKick, 1.5f);
                posVel += new Vector3(0f, -0.5f, -0.08f) * f;
                lagVel += new Vector3(70f, Random.Range(-15f, 15f), Random.Range(-25f, 25f)) * f;
            }
        }
        wasGrounded = grounded;

        // ---------- Пружины с массой ----------
        float stiffness = responsiveness / Mathf.Max(weaponWeight, 0.05f);
        float damping = 2f * dampingRatio * Mathf.Sqrt(stiffness);
        StepSpring(ref lag, ref lagVel, lagTarget, stiffness, damping, dt);
        StepSpring(ref posSpring, ref posVel, posTarget, stiffness, damping, dt);

        // ---------- 5. Хаотичное покачивание при движении ----------
        // Сила зависит от скорости плавно: спокойная ходьба — слабо, бег — сильно.
        float speed01 = grounded ? Mathf.Clamp01(hSpeed / Mathf.Max(run, 0.1f)) : 0f;
        float intensity = Mathf.Pow(speed01, speedCurve);
        runWeight = Mathf.Lerp(runWeight, running && grounded ? 1f : 0f, 1f - Mathf.Exp(-5f * dt));
        float rate = hSpeed < walk ? walkStepRate * Mathf.Lerp(0.75f, 1f, hSpeed / walk)
                                   : Mathf.Lerp(walkStepRate, runStepRate, Mathf.InverseLerp(walk, run, hSpeed));
        bobPhase += Mathf.PI * rate * dt * Mathf.Clamp01(hSpeed / 0.3f);

        // Каждый шаг — случайный толчок в пружину: направление и сила каждый раз разные,
        // а пружина сама превращает их в живое, неровное движение рук
        int step = Mathf.FloorToInt(bobPhase / Mathf.PI);
        if (step != lastStep && intensity > 0.01f)
        {
            float side = (step & 1) == 0 ? 1f : -1f;          // шаги чередуются: левая / правая нога
            float a = stepKickPosition * intensity;
            float r = stepKickRotation * intensity;
            posVel += new Vector3(side * Random.Range(0.2f, 1f) * a * 0.6f,
                                  -Random.Range(0.5f, 1.2f) * a,
                                  Random.Range(-0.4f, 0.4f) * a);
            lagVel += new Vector3(Random.Range(0.3f, 1.2f) * r,
                                  side * Random.Range(0.1f, 0.8f) * r * 0.6f + Random.Range(-0.3f, 0.3f) * r,
                                  side * Random.Range(0.3f, 1.2f) * r);
        }
        lastStep = step;

        // Неровное «плавание» — частота и сила растут со скоростью
        moveNoiseTime += dt * Mathf.Lerp(0.5f, 2.4f, speed01);
        Vector3 bobPos = Noise3(moveNoiseTime, 80f) * moveNoisePosition * intensity * k;
        Vector3 bobRot = Noise3(moveNoiseTime, 120f) * moveNoiseRotation * intensity * k;

        // ---------- 6. Случайный дрейф рук ----------
        float driftMul = Mathf.Lerp(1f, breathlessDrift, breathless) * k;
        driftTime += dt * driftSpeed * Mathf.Lerp(1f, 2.2f, breathless);
        Vector3 driftPos = Noise3(driftTime, 0f) * driftPosition * driftMul;
        Vector3 driftRot = Noise3(driftTime, 40f) * driftRotation * driftMul;

        // ---------- 7. Оружие опущено при беге ----------
        bool sprintPose = lowerWhileRunning && running && grounded && aim < 0.5f;
        if (sprintPose) sprintStoppedTime = 0f; else sprintStoppedTime += dt;
        if (sprintPose)
            runPose = Mathf.MoveTowards(runPose, 1f, dt / Mathf.Max(sprintPoseInTime, 0.01f));
        else if (sprintStoppedTime >= sprintPoseHold || aim > 0f)
            runPose = Mathf.MoveTowards(runPose, 0f, dt / Mathf.Max(aim > 0f ? sprintPoseInTime : sprintPoseOutTime, 0.01f));
        float rp = Mathf.SmoothStep(0f, 1f, runPose);

        // ---------- Сборка ----------
        Vector3 aimRot = new Vector3(-freeAim.y, freeAim.x, 0f);   // Free-Aim — точный, без ослабления
        // ---------- 8. Прицеливание как в Squad ----------
        // Усталость: 0 — отдохнул, 1 — измотан
        float fatigue = movement != null ? Mathf.Clamp01(Mathf.Max(breathless, movement.IsExhausted ? 0.8f : 0f)) : 0f;

        // Дыхание: медленная «восьмёрка», у уставшего — частая и широкая
        aimBreathPhase += dt * Mathf.Lerp(1.6f, 5.5f, fatigue);
        float breathAmp = Mathf.Lerp(aimBreathCalm, aimBreathTired, fatigue) * aim;
        Vector3 aimBreath = new Vector3(Mathf.Sin(aimBreathPhase) * breathAmp,
                                        Mathf.Sin(aimBreathPhase * 0.5f + 0.7f) * breathAmp * 0.7f, 0f);

        // Хаотичная дрожь: у уставшего быстрее и сильнее
        aimShakeTime += dt * Mathf.Lerp(0.6f, 2.6f, fatigue);
        Vector3 aimShake = (Noise3(aimShakeTime, 200f) + Noise3(aimShakeTime * 2.7f, 260f) * 0.4f)
                         * Mathf.Lerp(aimShakeCalm, aimShakeTired, fatigue) * aim;

        // Выравнивание ствола: поворот в прицеле сбивает хват, ствол гуляет, пока хват не стабилизируется
        instability = Mathf.Clamp01(instability + Mathf.Abs(worldYawRate) * instabilityGain * aim * dt);
        instability = Mathf.MoveTowards(instability, 0f, dt / Mathf.Lerp(stabilizeTime, tiredStabilizeTime, fatigue));
        instabilityTime += dt * 3.5f;
        Vector3 aimInstab = Noise3(instabilityTime, 320f) * instabilityAmount * instability * aim;

        Vector3 aimSway = aimBreath + aimShake + aimInstab;

        // ---------- Сборка ----------
        Vector3 euler = aimRot + lag * kLag + bobRot + driftRot + aimSway + sprintRotation * rp;
        Quaternion rot = Quaternion.Euler(euler);

        // Кисть немного смещается вслед за поворотом (как будто ведёт запястьем)
        Vector3 shift = new Vector3((aimRot.y + lag.y * kLag) * handShift, -(aimRot.x + lag.x * kLag) * handShift, 0f);
        Vector3 offset = posSpring * k + bobPos + driftPos + sprintPosition * rp + shift;

        // От бедра вращаем вокруг запястья (ствол уходит дальше рукояти),
        // в прицеле — вокруг целика: он остаётся у глаза, а мушка гуляет отдельно (мушка и целик не совпадают)
        Vector3 localPivot = Vector3.Lerp(wristPivot, rearSightPoint, aim);
        Vector3 wrist = transform.localPosition + transform.localRotation * localPivot;
        pivot.localRotation = rot;
        pivot.localPosition = offset + wrist - rot * wrist;
    }

    static void StepSpring(ref Vector3 x, ref Vector3 v, Vector3 target, float stiffness, float damping, float dt)
    {
        int steps = Mathf.CeilToInt(dt / 0.005f);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 a = (target - x) * stiffness - v * damping;
            v += a * h;
            x += v * h;
        }
    }

    static Vector3 Noise3(float t, float seed) => new Vector3(
        Mathf.PerlinNoise(t, seed + 0.1f) * 2f - 1f,
        Mathf.PerlinNoise(seed + 3.3f, t) * 2f - 1f,
        Mathf.PerlinNoise(t, seed + 7.7f) * 2f - 1f);

    void OnGUI()
    {
        if (!showAimPoint || cam == null) return;
        Transform from = muzzle != null ? muzzle : transform;
        Vector3 sp = cam.WorldToScreenPoint(from.position + transform.forward * 30f);
        if (sp.z <= 0f) return;
        float s = aimPointSize;
        var r = new Rect(sp.x - s * 0.5f, Screen.height - sp.y - s * 0.5f, s, s);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.8f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

#if ENABLE_INPUT_SYSTEM
    Vector2 ReadLook() => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
#else
    Vector2 ReadLook() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
#endif
}