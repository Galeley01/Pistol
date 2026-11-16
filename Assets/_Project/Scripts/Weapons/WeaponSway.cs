using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Player;
using Pistol.Weapons.Revolver;

namespace Pistol.Weapons
{
    // "Alive" weapon in the hands.
    // - Weight and inertia: when the camera turns, the weapon stays put in the world and catches up with a delay
    //   (spring with mass), with a slight overshoot.
    // - Roll from turn speed: the faster the turn, the more the barrel rolls on Z towards the turn.
    // - Rotation around the wrist: the barrel travels noticeably more than the grip, so the wrists move, not the elbows.
    // - Free-Aim: the weapon rotates by FreeAimOffset from PlayerMovement (deadzone).
    // - Movement bob, random hand drift, jump/landing kicks, lowered weapon while sprinting.
    //
    // Goes on Revolver. On start it inserts WeaponSwayPivot between the camera and the revolver.
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

        [Foldout("Отдача (как подлетает ствол)"), Range(0.01f, 0.15f), Tooltip("За сколько секунд ствол подлетает до верхней точки. Меньше — резче")]
        public float recoilRiseTime = 0.035f;
        [Foldout("Отдача (как подлетает ствол)"), Range(0.05f, 0.8f), Tooltip("За сколько секунд оружие почти вернулось на место (на 90%)")]
        public float recoilReturnTime = 0.22f;
        [Foldout("Отдача (как подлетает ствол)"), Range(0.4f, 1f), Tooltip("1 — возвращается ровно, без перелёта. Меньше — с лёгким «клевком» ниже исходного")]
        public float recoilReturnDamping = 0.85f;
        [Foldout("Отдача (как подлетает ствол)"), Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.1f)]
        [Tooltip("Точка, вокруг которой подлетает ствол: от кисти на рукояти (IK_RightHand). Ниже/назад — рукоять сильнее упирается, ствол сильнее подлетает")]
        public Vector3 recoilPivotOffset = Vector3.zero;

        [Foldout("Осмотр барабана (Alt)"), Tooltip("Насколько сдвинуть оружие в режиме осмотра: ближе к лицу, задним торцом барабана к глазам")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.25f)]
        public Vector3 inspectPosition = new Vector3(-0.04f, 0.06f, -0.06f);
        [Foldout("Осмотр барабана (Alt)"), Tooltip("Поворот оружия в режиме осмотра. X+ — ствол вниз, Z− — откинутый барабан поднимается")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 90f)]
        public Vector3 inspectRotation = new Vector3(20f, 10f, -35f);
        [Foldout("Осмотр барабана (Alt)"), Range(0f, 1f), Tooltip("Сколько покачивания и дрейфа рук остаётся при осмотре")]
        public float inspectSwayMultiplier = 0.3f;
        [Foldout("Осмотр барабана (Alt)"), Range(0f, 1f), Tooltip("Ставит позу осмотра без Alt — удобно подбирать ползунки в Play Mode (барабан при этом можно не открывать)")]
        public float inspectPreview = 0f;

        // 0..1, how far the weapon is in the inspect pose (set by RevolverInspect)
        [HideInInspector] public float inspectAmount;

        [Foldout("Прицеливание"), Range(0f, 1.5f), Tooltip("Сколько инерции (заноса) остаётся в прицеле. 1 = как от бедра")]
        public float aimLagMultiplier = 0.85f;
        [Foldout("Прицеливание"), Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.15f)]
        [Tooltip("Точка у целика (относительно Revolver). В прицеле оружие вращается вокруг неё: целик у глаза, а мушка гуляет")]
        public Vector3 rearSightPoint = new Vector3(0f, 0.055f, 0.01f);
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

        [Foldout("Точка прицела"), Tooltip("Маленькая точка там, куда смотрит ствол (удобно с Free-Aim)")]
        public bool showAimPoint = false;
        [Foldout("Точка прицела"), ShowIf("showAimPoint"), Range(2f, 12f)] public float aimPointSize = 4f;

        Transform pivot;
        Camera cam;
        Transform muzzle;
        PlayerMovement movement;
        CharacterController controller;
        WeaponAim weaponAim;

        // Weapon lag behind the view: x = pitch (down +), y = yaw (right +), z = roll
        Vector3 lag, lagVel;
        Vector3 posSpring, posVel;
        Vector2 lastFreeAim;
        float worldYawRate;
        float bobPhase, runWeight, runPose, driftTime, moveNoiseTime, sprintStoppedTime;
        int lastStep;
        bool wasGrounded = true;
        float lastAirVelocity;
        float aimBreathPhase, aimShakeTime, instability, instabilityTime;
        Vector3 recoilRot, recoilRotVel, recoilPos, recoilPosVel;   // separate stiff recoil spring

        // External kick (e.g. the weapon reached the eye): rotation velocity (deg/s) and position velocity (m/s)
        public void AddKick(Vector3 rotationVelocity, Vector3 positionVelocity)
        {
            lagVel += rotationVelocity;
            posVel += positionVelocity;
        }

        // Shot recoil: how many degrees the barrel flips (x- = up, y = side, z = roll)
        // and how many meters the weapon moves (z- = back towards the face). These are peaks, not velocities.
        // Rotation is around the wrist on the grip: the barrel flips, the grip stays in the hand.
        public void AddRecoil(Vector3 peakRotation, Vector3 peakPosition)
        {
            float k = RecoilSpring.ImpulseFor(1f, recoilRiseTime);
            recoilRotVel += peakRotation * k;
            recoilPosVel += peakPosition * k;
        }

        // Disturb the grip (0..1): the barrel wanders until the grip settles (like after turning while aiming)
        public void AddInstability(float amount) => instability = Mathf.Clamp01(instability + amount);

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
            float ip = Mathf.SmoothStep(0f, 1f, Mathf.Max(inspectAmount, inspectPreview));   // inspect pose
            float k = masterIntensity * Mathf.Lerp(1f, aimMultiplier, aim)       // bob, drift
                    * Mathf.Lerp(1f, inspectSwayMultiplier, ip);
            float kLag = masterIntensity * Mathf.Lerp(1f, aimLagMultiplier, aim); // inertia and lag

            // ---------- Player movement ----------
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

            // ---------- Where the weapon should point (Free-Aim) and how the view turned ----------
            Vector2 freeAim = movement != null ? movement.FreeAimOffset : Vector2.zero;
            Vector2 camDelta = movement != null ? movement.AppliedLookDelta : Vector2.zero;
            Vector2 aimDelta = camDelta + (freeAim - lastFreeAim);   // total world rotation of the "target" this frame
            lastFreeAim = freeAim;

            // ---------- 1. Weight: the weapon stays put in the world, the target moves ahead ----------
            lag.y -= aimDelta.x;
            lag.x += aimDelta.y;
            lag.x = Mathf.Clamp(lag.x, -maxLag, maxLag);
            lag.y = Mathf.Clamp(lag.y, -maxLag, maxLag);

            // ---------- 2. Roll from turn speed (towards the turn) ----------
            float yawRate = aimDelta.x / dt + lagVel.y;   // how fast the barrel turns in the world
            worldYawRate = Mathf.Lerp(worldYawRate, yawRate, 1f - Mathf.Exp(-12f * dt));
            float rollTarget = Mathf.Clamp(-worldYawRate * rollPerTurnSpeed, -maxRoll, maxRoll);
            float dipTarget = Mathf.Min(Mathf.Abs(worldYawRate) * dipPerTurnSpeed, maxRoll * 0.5f);

            // ---------- 3. Spring targets from movement ----------
            Vector3 lagTarget = new Vector3(dipTarget + localVel.z * forwardDip, 0f, rollTarget - localVel.x * strafeRoll);
            Vector3 posTarget = -localVel * moveLag;

            // ---------- 4. Jump and landing ----------
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

            // ---------- Springs with mass ----------
            float stiffness = responsiveness / Mathf.Max(weaponWeight, 0.05f);
            float damping = 2f * dampingRatio * Mathf.Sqrt(stiffness);
            StepSpring(ref lag, ref lagVel, lagTarget, stiffness, damping, dt);
            StepSpring(ref posSpring, ref posVel, posTarget, stiffness, damping, dt);

            // ---------- 5. Chaotic movement bob ----------
            // Strength follows speed smoothly: a calm walk is weak, running is strong.
            float speed01 = grounded ? Mathf.Clamp01(hSpeed / Mathf.Max(run, 0.1f)) : 0f;
            float intensity = Mathf.Pow(speed01, speedCurve);
            runWeight = Mathf.Lerp(runWeight, running && grounded ? 1f : 0f, 1f - Mathf.Exp(-5f * dt));
            float rate = hSpeed < walk ? walkStepRate * Mathf.Lerp(0.75f, 1f, hSpeed / walk)
                                       : Mathf.Lerp(walkStepRate, runStepRate, Mathf.InverseLerp(walk, run, hSpeed));
            bobPhase += Mathf.PI * rate * dt * Mathf.Clamp01(hSpeed / 0.3f);

            // Every step is a random kick into the spring: direction and strength differ each time,
            // and the spring turns them into lively, uneven hand motion
            int step = Mathf.FloorToInt(bobPhase / Mathf.PI);
            if (step != lastStep && intensity > 0.01f)
            {
                float side = (step & 1) == 0 ? 1f : -1f;          // steps alternate: left / right foot
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

            // Uneven "floating": frequency and strength grow with speed
            moveNoiseTime += dt * Mathf.Lerp(0.5f, 2.4f, speed01);
            Vector3 bobPos = Noise3(moveNoiseTime, 80f) * moveNoisePosition * intensity * k;
            Vector3 bobRot = Noise3(moveNoiseTime, 120f) * moveNoiseRotation * intensity * k;

            // ---------- 6. Random hand drift ----------
            float driftMul = Mathf.Lerp(1f, breathlessDrift, breathless) * k;
            driftTime += dt * driftSpeed * Mathf.Lerp(1f, 2.2f, breathless);
            Vector3 driftPos = Noise3(driftTime, 0f) * driftPosition * driftMul;
            Vector3 driftRot = Noise3(driftTime, 40f) * driftRotation * driftMul;

            // ---------- 7. Weapon lowered while sprinting ----------
            bool sprintPose = lowerWhileRunning && running && grounded && aim < 0.5f;
            if (sprintPose) sprintStoppedTime = 0f; else sprintStoppedTime += dt;
            if (sprintPose)
                runPose = Mathf.MoveTowards(runPose, 1f, dt / Mathf.Max(sprintPoseInTime, 0.01f));
            else if (sprintStoppedTime >= sprintPoseHold || aim > 0f)
                runPose = Mathf.MoveTowards(runPose, 0f, dt / Mathf.Max(aim > 0f ? sprintPoseInTime : sprintPoseOutTime, 0.01f));
            float rp = Mathf.SmoothStep(0f, 1f, runPose);

            // ---------- Assembly ----------
            Vector3 aimRot = new Vector3(-freeAim.y, freeAim.x, 0f) * (1f - ip);   // Free-Aim is exact; centered while inspecting
            // ---------- 8. Aiming: rotate around the rear sight, front sight wanders ----------
            // Fatigue: 0 = rested, 1 = exhausted
            float fatigue = movement != null ? movement.Fatigue : 0f;

            // Breathing: a slow figure eight, faster and wider when tired
            aimBreathPhase += dt * Mathf.Lerp(1.6f, 5.5f, fatigue);
            float breathAmp = Mathf.Lerp(aimBreathCalm, aimBreathTired, fatigue) * aim;
            Vector3 aimBreath = new Vector3(Mathf.Sin(aimBreathPhase) * breathAmp,
                                            Mathf.Sin(aimBreathPhase * 0.5f + 0.7f) * breathAmp * 0.7f, 0f);

            // Chaotic tremor: faster and stronger when tired
            aimShakeTime += dt * Mathf.Lerp(0.6f, 2.6f, fatigue);
            Vector3 aimShake = (Noise3(aimShakeTime, 200f) + Noise3(aimShakeTime * 2.7f, 260f) * 0.4f)
                             * Mathf.Lerp(aimShakeCalm, aimShakeTired, fatigue) * aim;

            // Barrel settling: turning while aiming disturbs the grip, the barrel wanders until it settles
            instability = Mathf.Clamp01(instability + Mathf.Abs(worldYawRate) * instabilityGain * aim * dt);
            instability = Mathf.MoveTowards(instability, 0f, dt / Mathf.Lerp(stabilizeTime, tiredStabilizeTime, fatigue));
            instabilityTime += dt * 3.5f;
            Vector3 aimInstab = Noise3(instabilityTime, 320f) * instabilityAmount * instability * aim;

            Vector3 aimSway = aimBreath + aimShake + aimInstab;

            // ---------- Assembly ----------
            Vector3 euler = aimRot + lag * kLag + bobRot + driftRot + aimSway + sprintRotation * rp + inspectRotation * ip;
            Quaternion rot = Quaternion.Euler(euler);

            // The hand shifts slightly after the rotation (as if leading with the wrist)
            Vector3 shift = new Vector3((aimRot.y + lag.y * kLag) * handShift, -(aimRot.x + lag.x * kLag) * handShift, 0f);
            Vector3 offset = posSpring * k + bobPos + driftPos + sprintPosition * rp + inspectPosition * ip + shift;

            // From the hip rotate around the wrist (the barrel travels more than the grip);
            // while aiming, around the rear sight: it stays at the eye while the front sight wanders (sights misalign)
            Vector3 localPivot = Vector3.Lerp(wristPivot, rearSightPoint, aim);
            Vector3 wrist = transform.localPosition + transform.localRotation * localPivot;
            Vector3 pivotPos = offset + wrist - rot * wrist;

            // ---------- 9. Recoil: rotation around the wrist on the grip ----------
            RecoilSpring.Step(ref recoilRot, ref recoilRotVel, recoilRiseTime, recoilReturnTime, recoilReturnDamping, dt);
            RecoilSpring.Step(ref recoilPos, ref recoilPosVel, recoilRiseTime, recoilReturnTime, recoilReturnDamping, dt);
            Quaternion gunFrame = rot * transform.localRotation;                    // weapon axes in camera space
            Vector3 hand = pivotPos + rot * (transform.localPosition + transform.localRotation * (wristPivot + recoilPivotOffset));
            Quaternion kick = gunFrame * Quaternion.Euler(recoilRot) * Quaternion.Inverse(gunFrame);
            pivot.localRotation = kick * rot;
            pivot.localPosition = hand + kick * (pivotPos - hand) + gunFrame * recoilPos;
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
    }
}
