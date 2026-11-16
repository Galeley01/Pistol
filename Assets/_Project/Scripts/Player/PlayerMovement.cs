using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

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

        [BoxGroup("Профиль"), Required("Назначьте профиль: движение"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        MovementProfile profile;
        public MovementProfile Profile => profile;

        // Deadzone scale: WeaponAim lowers it to 0 while aiming so the weapon centers exactly
        float freeAimScale = 1f;
        public void SetFreeAimScale(float scale) => freeAimScale = Mathf.Clamp01(scale);


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
            if (profile == null) { Debug.LogError("PlayerMovement: no profile assigned.", this); enabled = false; return; }
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
            currentRunSpeed = profile.runSpeed;
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
            Vector2 look = context.LookDelta * profile.mouseSensitivity;
            Vector2 toCamera;

            if (!profile.freeAim)
            {
                toCamera = look;
                FreeAimOffset = Vector2.zero;
            }
            else
            {
                // Fast flick: with a fast mouse the rotation goes straight into the camera
                float speed = look.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
                float direct = Mathf.InverseLerp(profile.fastTurnSpeed * 0.5f, profile.fastTurnSpeed, speed);
                toCamera = look * direct;
                Vector2 fa = FreeAimOffset + look * (1f - direct);

                // The body slowly turns after the weapon (if enabled)
                if (profile.bodyFollowSpeed > 0f)
                {
                    Vector2 follow = fa * (1f - Mathf.Exp(-profile.bodyFollowSpeed * Time.deltaTime));
                    fa -= follow;
                    toCamera += follow;
                }

                // Anything outside the deadzone turns the camera
                Vector2 clamped = ClampEllipse(fa, profile.deadzoneYaw * freeAimScale, profile.deadzonePitch * freeAimScale);
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
            float sprintBlend = Mathf.InverseLerp(profile.walkSpeed, profile.runSpeed, new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude);
            if (IsRunning && sprintBlend > 0f)
            {
                float limit = Mathf.Lerp(10000f, profile.sprintTurnSpeed, sprintBlend) * Time.deltaTime;
                toCamera.x = Mathf.Clamp(toCamera.x, -limit, limit);
            }

            transform.Rotate(Vector3.up * toCamera.x);
            float newPitch = Mathf.Clamp(pitch - toCamera.y, -profile.maxLookAngle, profile.maxLookAngle);
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

            CrouchAmount = Smoothing.Damp(CrouchAmount, IsCrouching ? 1f : 0f, profile.crouchTransitionSpeed, Time.deltaTime);

            // Change the capsule height so the feet stay in place
            float height = Mathf.Lerp(standHeight, profile.crouchHeight, CrouchAmount);
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
                ? (wantsRun ? profile.crouchRunSpeed : profile.crouchWalkSpeed)
                : (IsRunning ? currentRunSpeed : profile.walkSpeed);
            if (!moving) targetSpeed = 0f;
            TargetSpeed = targetSpeed;

            Vector3 dir = transform.right * input.x + transform.forward * input.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            // Heavy body: reaches walking speed quickly, running speed slowly, and brakes with inertia
            Vector3 targetVel = dir * targetSpeed;
            Vector3 prevVel = horizontalVelocity;
            float curSpeed = horizontalVelocity.magnitude;
            if (targetSpeed > profile.walkSpeed + 0.05f && targetVel.magnitude > curSpeed && curSpeed >= profile.walkSpeed * 0.9f)
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVel, profile.runAcceleration * Time.deltaTime);
            else if (targetVel.magnitude < curSpeed - 0.01f)
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVel, profile.deceleration * Time.deltaTime);
            else
                horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVel, 1f - Mathf.Exp(-profile.acceleration * Time.deltaTime));
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
                    verticalVelocity = Mathf.Sqrt(profile.jumpHeight * -2f * profile.gravity);
                    JumpedThisFrame = true;
                }
            }

            verticalVelocity += profile.gravity * Time.deltaTime;
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
                    if (sprintTimer >= profile.sprintTimeBeforeTired)
                    {
                        IsExhausted = true;
                        recoveryTimer = profile.recoveryDuration;
                    }
                }
                else
                {
                    sprintTimer = Mathf.Max(0f, sprintTimer - profile.sprintCooldownRate * dt);
                }
            }
            else
            {
                if (IsRunning)
                    recoveryTimer = profile.recoveryDuration;      // started running again: recovery restarts
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
            currentRunSpeed = Smoothing.Damp(currentRunSpeed, IsExhausted ? profile.tiredRunSpeed : profile.runSpeed, profile.tiredSpeedChange, Time.deltaTime);

            // Build-up before exhaustion: 0 until fatigueBuildStart, smoothly up to 1 at exhaustion
            PreFatigue = IsExhausted ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(profile.fatigueBuildStart, profile.sprintTimeBeforeTired, sprintTimer));

            // Breathlessness for the camera
            float target;
            if (IsExhausted)
                target = IsRunning ? 1f : Mathf.Clamp01(recoveryTimer / 1.5f); // fades out over the last 1.5 s
            else
                target = sprintTimer / profile.sprintTimeBeforeTired * 0.35f;          // breathing gradually gets heavier
            Breathless = Smoothing.Damp(Breathless, target, 3f, Time.deltaTime);
        }
    }
}
