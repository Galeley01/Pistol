using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Ходилка от первого лица.
// WASD — движение, мышь — обзор, Shift — бег, Space — прыжок, C — присесть/встать,
// Esc — освободить курсор (клик ЛКМ — снова захватить).
// Усталость: бег дольше sprintTimeBeforeTired секунд → скорость бега падает до tiredRunSpeed,
// после остановки нужна отдышка recoveryDuration секунд (бег всё это время медленный).
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [BoxGroup("Ссылки"), Required("Назначьте CameraRoot (родитель Main Camera)")]
    [Tooltip("Сюда — CameraRoot (родитель Main Camera), а не сама камера")]
    public Transform cameraTransform;

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

    // Масштаб мёртвой зоны: WeaponAim уменьшает до 0 при прицеливании, чтобы оружие встало ровно по центру
    [HideInInspector] public float freeAimScale = 1f;

    bool IsBuildStartValid(float value) => value < sprintTimeBeforeTired;

    // ---- Состояние, которое читает BodycamEffects ----
    public bool IsRunning { get; private set; }       // бег стоя
    public bool IsCrouching { get; private set; }
    public float CrouchAmount { get; private set; }   // 0 = стоит, 1 = сидит (плавно)
    public bool IsExhausted { get; private set; }
    public float Breathless { get; private set; }     // 0..1 — сила отдышки
    public bool JumpedThisFrame { get; private set; }
    public float LeanInput { get; private set; }      // -1 = Q, 1 = E, 0 = нет
    public bool IsLeaning => LeanInput != 0f;
    public Vector2 FreeAimOffset { get; private set; }     // куда смотрит оружие относительно камеры: x — вправо, y — вверх (градусы)
    public Vector2 AppliedLookDelta { get; private set; }
    public Vector3 LocalAcceleration { get; private set; }  // ускорение тела в его координатах (x — вбок, z — вперёд), м/с²  // на сколько повернулась камера в этом кадре: x — вправо, y — вверх (градусы)
    public float PreFatigue { get; private set; }     // 0..1 — нарастание перед усталостью
    // Режим осмотра барабана (RevolverInspect): камера стоит, курсор свободен, ходить нельзя
    public bool InspectMode { get; private set; }

    // ---- Для отладочного UI ----
    public float SprintTimer => sprintTimer;          // сколько секунд уже бежим
    public float RecoveryTimer => recoveryTimer;      // сколько осталось отдышки
    public float CurrentRunSpeed => currentRunSpeed;  // текущий предел бега (6 → 4.5)
    public float TargetSpeed { get; private set; }    // скорость, к которой разгоняемся
    public float ActualSpeed => controller != null
        ? new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude : 0f;
    public bool IsGrounded => controller != null && controller.isGrounded;

    CharacterController controller;
    float verticalVelocity, pitch;
    Vector3 horizontalVelocity;
    float sprintTimer, recoveryTimer, currentRunSpeed;
    float standHeight, standCameraY;
    Vector3 standCenter;

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
        LockCursor(true);
    }

    void Update()
    {
        if (!InspectMode) HandleCursor();
        AppliedLookDelta = Vector2.zero;
        if (Cursor.lockState == CursorLockMode.Locked && !InspectMode)
            Look();
        HandleCrouch();
        Move();
    }

    // Включить / выключить режим осмотра. При выходе курсор снова захватывается
    public void SetInspectMode(bool on)
    {
        InspectMode = on;
        LockCursor(!on);
    }

    // ---------- Обзор мышью ----------
    // Отдача поднимает взгляд: накапливается и плавно применяется за несколько кадров
    Vector2 pendingRecoil;
    public void AddRecoilLook(Vector2 degrees) => pendingRecoil += degrees;   // x — вправо, y — вверх

    void Look()
    {
        Vector2 look = ReadLook() * mouseSensitivity;
        Vector2 toCamera;

        if (!freeAim)
        {
            toCamera = look;
            FreeAimOffset = Vector2.zero;
        }
        else
        {
            // Резкий разворот: при быстрой мыши поворот идёт сразу в камеру
            float speed = look.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            float direct = Mathf.InverseLerp(fastTurnSpeed * 0.5f, fastTurnSpeed, speed);
            toCamera = look * direct;
            Vector2 fa = FreeAimOffset + look * (1f - direct);

            // Корпус медленно доворачивается за оружием (если включено)
            if (bodyFollowSpeed > 0f)
            {
                Vector2 follow = fa * (1f - Mathf.Exp(-bodyFollowSpeed * Time.deltaTime));
                fa -= follow;
                toCamera += follow;
            }

            // Всё, что вышло за мёртвую зону, поворачивает камеру
            Vector2 clamped = ClampEllipse(fa, deadzoneYaw * freeAimScale, deadzonePitch * freeAimScale);
            toCamera += fa - clamped;
            FreeAimOffset = clamped;
        }

        // Отдача — прямо в камеру (мимо мёртвой зоны Free-Aim)
        if (pendingRecoil.sqrMagnitude > 0.000001f)
        {
            Vector2 r = pendingRecoil * (1f - Mathf.Exp(-25f * Time.deltaTime));
            pendingRecoil -= r;
            toCamera += r;
        }

        // На бегу резко развернуться нельзя: скорость поворота ограничена
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

    // ---------- Приседание (переключение на C) ----------
    void HandleCrouch()
    {
        if (!InspectMode && ReadCrouch())
        {
            if (!IsCrouching) IsCrouching = true;
            else if (CanStandUp()) IsCrouching = false;   // под низким потолком не встанет
        }

        CrouchAmount = Damp(CrouchAmount, IsCrouching ? 1f : 0f, crouchTransitionSpeed);

        // Меняем высоту капсулы так, чтобы ноги оставались на месте
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

    // ---------- Движение ----------
    void Move()
    {
        JumpedThisFrame = false;

        // В режиме осмотра клавиши движения не работают — персонаж плавно останавливается
        LeanInput = InspectMode ? 0f : (ReadLeanRight() ? 1f : 0f) - (ReadLeanLeft() ? 1f : 0f);

        Vector2 input = InspectMode ? Vector2.zero : ReadMove();
        bool moving = input.sqrMagnitude > 0.01f;
        bool wantsRun = !InspectMode && ReadRun() && moving && !IsLeaning;   // при выглядывании бег запрещён
        IsRunning = wantsRun && !IsCrouching;                // стамина тратится только здесь

        UpdateStamina();

        float targetSpeed = IsCrouching
            ? (wantsRun ? crouchRunSpeed : crouchWalkSpeed)
            : (IsRunning ? currentRunSpeed : walkSpeed);
        if (!moving) targetSpeed = 0f;
        TargetSpeed = targetSpeed;

        Vector3 dir = transform.right * input.x + transform.forward * input.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        // Тяжёлое тело: до шага разгоняется быстро, до бега — медленно, тормозит с инерцией
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

        if (controller.isGrounded && !InspectMode && ReadJump())
        {
            if (IsCrouching)
            {
                if (CanStandUp()) IsCrouching = false;           // Space сидя — встать
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

    // ---------- Усталость ----------
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
                recoveryTimer = recoveryDuration;      // снова побежал — отдышка начнётся заново
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

        // Скорость бега плавно падает до tiredRunSpeed и возвращается после отдышки
        currentRunSpeed = Damp(currentRunSpeed, IsExhausted ? tiredRunSpeed : runSpeed, tiredSpeedChange);

        // Нарастание перед усталостью: 0 до fatigueBuildStart, плавно до 1 к моменту усталости
        PreFatigue = IsExhausted ? 1f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fatigueBuildStart, sprintTimeBeforeTired, sprintTimer));

        // Сила отдышки для камеры
        float target;
        if (IsExhausted)
            target = IsRunning ? 1f : Mathf.Clamp01(recoveryTimer / 1.5f); // затихает в последние 1.5 с
        else
            target = sprintTimer / sprintTimeBeforeTired * 0.35f;          // постепенно сбивается дыхание
        Breathless = Damp(Breathless, target, 3f);
    }

    // ---------- Курсор ----------
    void HandleCursor()
    {
        if (ReadEscape()) LockCursor(false);
        else if (ReadClick() && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static float Damp(float current, float target, float speed)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime));

    // ---------- Ввод (новая и старая система ввода) ----------
#if ENABLE_INPUT_SYSTEM
    Vector2 ReadMove()
    {
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
        float y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
        return new Vector2(x, y);
    }
    Vector2 ReadLook()  => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
    bool ReadJump()     => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
    bool ReadRun()      => Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
    bool ReadCrouch()   => Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
    bool ReadLeanLeft() => Keyboard.current != null && Keyboard.current.qKey.isPressed;
    bool ReadLeanRight()=> Keyboard.current != null && Keyboard.current.eKey.isPressed;
    bool ReadEscape()   => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    bool ReadClick()    => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    Vector2 ReadMove()  => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    Vector2 ReadLook()  => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
    bool ReadJump()     => Input.GetKeyDown(KeyCode.Space);
    bool ReadRun()      => Input.GetKey(KeyCode.LeftShift);
    bool ReadCrouch()   => Input.GetKeyDown(KeyCode.C);
    bool ReadLeanLeft() => Input.GetKey(KeyCode.Q);
    bool ReadLeanRight()=> Input.GetKey(KeyCode.E);
    bool ReadEscape()   => Input.GetKeyDown(KeyCode.Escape);
    bool ReadClick()    => Input.GetMouseButtonDown(0);
#endif
}