using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Player
{
    // First-person character controller. Input comes from PlayerContext (WASD move, mouse look,
    // Shift run, Space jump, C crouch/stand, Q/E lean); movement and look work only in Gameplay mode.
    // Fatigue: running longer than sprintTimeBeforeTired seconds drops the run speed to tiredRunSpeed;
    // after stopping, recovery takes recoveryDuration seconds (running stays slow the whole time).
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerContext))]
    public class PlayerMovement : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте CameraRoot (родитель Main Camera)")]
        [Tooltip("Сюда — CameraRoot (родитель Main Camera), а не сама камера")]
        public Transform cameraTransform;

        [BoxGroup("Ссылки"), Tooltip("Находится сам на этом объекте")]
        [SerializeField] PlayerContext context;

        [BoxGroup("Движение"), MinValue(0)] public float walkSpeed = 3f;
        [BoxGroup("Движение"), MinValue(0)] public float runSpeed = 6f;
        [BoxGroup("Движение"), Tooltip("Плавность разгона/торможения при ходьбе")] public float acceleration = 10f;

        [BoxGroup("Инерция тела"), Range(1f, 20f), Tooltip("Разгон до бега, м/с². Меньше = тяжелее разгоняется")]
        public float runAcceleration = 5f;
        [BoxGroup("Инерция тела"), Range(1f, 30f), Tooltip("Торможение, м/с². Меньше = дольше останавливается")]
        public float deceleration = 9f;
        [BoxGroup("Инерция тела"), Range(30f, 720f), Tooltip("Максимальная скорость поворота на бегу, °/с")]
        public float sprintTurnSpeed = 170f;
        [BoxGroup("Движение")] public float jumpHeight = 1.2f;
        [BoxGroup("Движение")] public float gravity = -20f;

        [BoxGroup("Усталость"), MinValue(0.1f)] public float sprintTimeBeforeTired = 8f;
        [BoxGroup("Усталость"), MinValue(0)]
        [ValidateInput("IsBuildStartValid", "Должно быть меньше Sprint Time Before Tired")]
        [Tooltip("С какой секунды бега начинает нарастать тряска перед усталостью")]
        public float fatigueBuildStart = 6f;
        [BoxGroup("Усталость"), MinValue(0)] public float tiredRunSpeed = 4.5f;
        [BoxGroup("Усталость"), MinValue(0)] public float recoveryDuration = 5f;
        [BoxGroup("Усталость"), Tooltip("Как быстро «остывает» счётчик бега, если остановился раньше, чем устал")]
        public float sprintCooldownRate = 2f;
        [BoxGroup("Усталость"), Tooltip("Насколько плавно падает скорость при усталости")]
        public float tiredSpeedChange = 1.5f;

        [BoxGroup("Приседание (C)")] public float crouchHeight = 1.2f;
        [BoxGroup("Приседание (C)")] public float crouchWalkSpeed = 1.6f;
        [BoxGroup("Приседание (C)"), Tooltip("Медленнее бега стоя, без усталости")] public float crouchRunSpeed = 3f;
        [BoxGroup("Приседание (C)")] public float crouchTransitionSpeed = 10f;

        [BoxGroup("Обзор")] public float mouseSensitivity = 1f;
        [BoxGroup("Обзор"), Range(10f, 89f)] public float maxLookAngle = 85f;

        [BoxGroup("Свободное прицеливание (Free-Aim)"), Tooltip("Небольшие движения мыши сначала двигают только оружие, а камера поворачивается, когда оружие выходит за мёртвую зону")]
        public bool freeAim = true;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(0f, 20f), Tooltip("Мёртвая зона по горизонтали, градусы")]
        public float deadzoneYaw = 7f;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(0f, 15f), Tooltip("Мёртвая зона по вертикали, градусы")]
        public float deadzonePitch = 4f;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(100f, 2000f), Tooltip("Скорость мыши (°/с), при которой камера поворачивается сразу, минуя мёртвую зону (резкий разворот)")]
        public float fastTurnSpeed = 450f;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(0f, 5f), Tooltip("Корпус медленно доворачивается за оружием. 0 = выключено")]
        public float bodyFollowSpeed = 0f;

        // Deadzone scale: WeaponAim lowers it to 0 while aiming so the weapon centers exactly
        [HideInInspector] public float freeAimScale = 1f;

        bool IsBuildStartValid(float value) => value < sprintTimeBeforeTired;

        // ---- State read by CameraMotion ----
        public bool IsRunning { get; private set; }       // running while standing
        public bool IsCrouching { get; private set; }
        public float CrouchAmount { get; private set; }   // 0 = standing, 1 = crouched (smoothed)
        public bool IsExhausted { get; private set; }
        public float Breathless { get; private set; }     // 0..1, how out of breath
        public bool JumpedThisFrame { get; private set; }
        public float LeanInput { get; private set; }      // -1 = Q, 1 = E, 0 = none
        public bool IsLeaning => LeanInput != 0f;
        public Vector2 FreeAimOffset { get; private set; }     // where the weapon points relative to the camera: x = right, y = up (degrees)
        public Vector2 AppliedLookDelta { get; private set; }  // how much the camera turned this frame: x = right, y = up (degrees)
        public Vector3 LocalAcceleration { get; private set; }  // body acceleration in its own space (x = sideways, z = forward), m/s^2
        public float PreFatigue { get; private set; }     // 0..1, build-up before exhaustion
        // 0 = rested, 1 = exhausted: one formula for everything that reacts to fatigue (aim, sway, recoil)
        public float Fatigue => Mathf.Clamp01(Mathf.Max(Breathless, IsExhausted ? 0.8f : 0f));

        // ---- For the debug UI ----
        public float SprintTimer => sprintTimer;          // seconds spent sprinting
        public float RecoveryTimer => recoveryTimer;      // recovery time left
        public float CurrentRunSpeed => currentRunSpeed;  // current run speed limit (6 -> 4.5)
        public float TargetSpeed { get; private set; }    // speed we are accelerating towards
        public float ActualSpeed => controller != null
            ? new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude : 0f;
        public bool IsGrounded => controller != null && controller.isGrounded;

        CharacterController controller;
        float verticalVelocity, pitch;
        Vector3 horizontalVelocity;
        float sprintTimer, recoveryTimer, currentRunSpeed;
        float standHeight, standCameraY;
        Vector3 standCenter;

        void Reset() => context = GetComponent<PlayerContext>();

        void Awake()
        {
            if (context == null) context = GetComponent<PlayerContext>();
            if (context == null)
            {
                Debug.LogError("PlayerMovement: PlayerContext is missing on the player.", this);
                enabled = false;
            }
        }

        void Start()
        {
            controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null)
            {
                Transform cam = Camera.main.transform;
                cameraTransform = (cam.parent != null && cam.parent != transform) ? cam.parent : cam;
            }

            standHeight = controller.height;
            standCenter = controller.center;
            standCameraY = cameraTransform.localPosition.y;
            currentRunSpeed = runSpeed;
        }

        void Update()
        {
            AppliedLookDelta = Vector2.zero;
            if (context.Mode == ControlMode.Gameplay) Look();
            HandleCrouch();
            Move();
        }

        // ---------- Mouse look ----------
        // Recoil raises the view: accumulated and applied smoothly over a few frames
        Vector2 pendingRecoil;
        public void AddRecoilLook(Vector2 degrees) => pendingRecoil += degrees;   // x = right, y = up

        void Look()
        {
            Vector2 look = context.LookDelta * mouseSensitivity;
            Vector2 toCamera;

            if (!freeAim)
            {
                toCamera = look;
                FreeAimOffset = Vector2.zero;
            }
            else
            {
                // Fast flick: with a fast mouse the rotation goes straight into the camera
                float speed = look.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
                float direct = Mathf.InverseLerp(fastTurnSpeed * 0.5f, fastTurnSpeed, speed);
                toCamera = look * direct;
                Vector2 fa = FreeAimOffset + look * (1f - direct);

                // The body slowly turns after the weapon (if enabled)
                if (bodyFollowSpeed > 0f)
                {
                    Vector2 follow = fa * (1f - Mathf.Exp(-bodyFollowSpeed * Time.deltaTime));
                    fa -= follow;
                    toCamera += follow;
                }

                // Anything outside the deadzone turns the camera
                Vector2 clamped = ClampEllipse(fa, deadzoneYaw * freeAimScale, deadzonePitch * freeAimScale);
                toCamera += fa - clamped;
                FreeAimOffset = clamped;
            }

            // Recoil goes straight into the camera (bypassing the Free-Aim deadzone)
            if (pendingRecoil.sqrMagnitude > 0.000001f)
            {
                Vector2 r = pendingRecoil * (1f - Mathf.Exp(-25f * Time.deltaTime));
                pendingRecoil -= r;
                toCamera += r;
            }

            // No sharp turns while running: turn speed is limited
            float sprintBlend = Mathf.InverseLerp(walkSpeed, runSpeed, new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude);
            if (IsRunning && sprintBlend > 0f)
            {
                float limit = Mathf.Lerp(10000f, sprintTurnSpeed, sprintBlend) * Time.deltaTime;
                toCamera.x = Mathf.Clamp(toCamera.x, -limit, limit);
            }

            transform.Rotate(Vector3.up * toCamera.x);
            float newPitch = Mathf.Clamp(pitch - toCamera.y, -maxLookAngle, maxLookAngle);
            AppliedLookDelta = new Vector2(toCamera.x, pitch - newPitch);
            pitch = newPitch;
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        static Vector2 ClampEllipse(Vector2 v, float a, float b)
        {
            if (a < 0.001f || b < 0.001f)
                return new Vector2(a < 0.001f ? 0f : Mathf.Clamp(v.x, -a, a), b < 0.001f ? 0f : Mathf.Clamp(v.y, -b, b));
            float e = (v.x / a) * (v.x / a) + (v.y / b) * (v.y / b);
            return e <= 1f ? v : v / Mathf.Sqrt(e);
        }

        // ---------- Crouch (toggle on C) ----------
        void HandleCrouch()
        {
            if (context.CrouchPressed)
            {
                if (!IsCrouching) IsCrouching = true;
                else if (CanStandUp()) IsCrouching = false;   // can't stand up under a low ceiling
            }

            CrouchAmount = Damp(CrouchAmount, IsCrouching ? 1f : 0f, crouchTransitionSpeed);

            // Change the capsule height so the feet stay in place
            float height = Mathf.Lerp(standHeight, crouchHeight, CrouchAmount);
            float drop = standHeight - height;
            controller.height = height;
            controller.center = standCenter - new Vector3(0f, drop * 0.5f, 0f);

            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = standCameraY - drop;
            cameraTransform.localPosition = camPos;
        }

        bool CanStandUp()
        {
            Vector3 top = transform.position + controller.center
                        + Vector3.up * (controller.height * 0.5f - controller.radius);
            float distance = standHeight - controller.height + 0.05f;
            return !Physics.SphereCast(top, controller.radius * 0.9f, Vector3.up, out _, distance,
                                       ~0, QueryTriggerInteraction.Ignore);
        }

        // ---------- Movement ----------
        void Move()
        {
            JumpedThisFrame = false;

            // Outside Gameplay mode the actions read as zero, so the character slows to a stop
            LeanInput = context.Lean;

            Vector2 input = context.Move;
            bool moving = input.sqrMagnitude > 0.01f;
            bool wantsRun = context.SprintHeld && moving && !IsLeaning;   // no running while leaning
            IsRunning = wantsRun && !IsCrouching;                // stamina is spent only here

            UpdateStamina();

            float targetSpeed = IsCrouching
                ? (wantsRun ? crouchRunSpeed : crouchWalkSpeed)
                : (IsRunning ? currentRunSpeed : walkSpeed);
            if (!moving) targetSpeed = 0f;
            TargetSpeed = targetSpeed;

            Vector3 dir = transform.right * input.x + transform.forward * input.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            // Heavy body: reaches walking speed quickly, running speed slowly, and brakes with inertia
            Vector3 targetVel = dir * targetSpeed;
            Vector3 prevVel = horizontalVelocity;
            float curSpeed = horizontalVelocity.magnitude;
            if (targetSpeed > walkSpeed + 0.05f && targetVel.magnitude > curSpeed && curSpeed >= walkSpeed * 0.9f)
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVel, runAcceleration * Time.deltaTime);
            else if (targetVel.magnitude < curSpeed - 0.01f)
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVel, deceleration * Time.deltaTime);
            else
                horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVel, 1f - Mathf.Exp(-acceleration * Time.deltaTime));
            LocalAcceleration = transform.InverseTransformDirection((horizontalVelocity - prevVel) / Mathf.Max(Time.deltaTime, 0.0001f));

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (controller.isGrounded && context.JumpPressed)
            {
                if (IsCrouching)
                {
                    if (CanStandUp()) IsCrouching = false;           // Space while crouched = stand up
                }
                else
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    JumpedThisFrame = true;
                }
            }

            verticalVelocity += gravity * Time.deltaTime;
            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        // ---------- Fatigue ----------
        void UpdateStamina()
        {
            float dt = Time.deltaTime;

            if (!IsExhausted)
            {
                if (IsRunning)
                {
                    sprintTimer += dt;
                    if (sprintTimer >= sprintTimeBeforeTired)
                    {
                        IsExhausted = true;
                        recoveryTimer = recoveryDuration;
                    }
                }
                else
                {
                    sprintTimer = Mathf.Max(0f, sprintTimer - sprintCooldownRate * dt);
                }
            }
            else
            {
                if (IsRunning)
                    recoveryTimer = recoveryDuration;      // started running again: recovery restarts
                else
                {
                    recoveryTimer -= dt;
                    if (recoveryTimer <= 0f)
                    {
                        IsExhausted = false;
                        sprintTimer = 0f;
                    }
                }
            }

            // Run speed smoothly drops to tiredRunSpeed and recovers after resting
            currentRunSpeed = Damp(currentRunSpeed, IsExhausted ? tiredRunSpeed : runSpeed, tiredSpeedChange);

            // Build-up before exhaustion: 0 until fatigueBuildStart, smoothly up to 1 at exhaustion
            PreFatigue = IsExhausted ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fatigueBuildStart, sprintTimeBeforeTired, sprintTimer));

            // Breathlessness for the camera
            float target;
            if (IsExhausted)
                target = IsRunning ? 1f : Mathf.Clamp01(recoveryTimer / 1.5f); // fades out over the last 1.5 s
            else
                target = sprintTimer / sprintTimeBeforeTired * 0.35f;          // breathing gradually gets heavier
            Breathless = Damp(Breathless, target, 3f);
        }

        static float Damp(float current, float target, float speed)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }
}
