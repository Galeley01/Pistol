using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Вешается на корень Revolver (рядом с Animator).
// ЛКМ — выстрел (барабан закрыт) или раскрутка барабана (барабан открыт).
// Выстрел: барабан проворачивается на камору (RevolverCylinder), курок падает (OnShot),
// если там патрон — пуля летит из дула (RevolverShooter), иначе сухой щелчок.
// R — открыть / закрыть барабан.
// Методы OnShot / OnSpinStart / OnCylinderClosed вызываются из анимаций (Animation Events).
[RequireComponent(typeof(Animator))]
public class RevolverFire : MonoBehaviour
{
    [BoxGroup("Стрельба"), MinValue(0.05f), Tooltip("Минимальная пауза между выстрелами, сек")]
    public float fireCooldown = 0.35f;

    [BoxGroup("Стрельба"), Tooltip("Точка дула (дочерний объект Muzzle)")]
    public Transform muzzle;

    [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float fireSpeed = 1f;
    [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float openSpeed = 1f;
    [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float closeSpeed = 1f;
    [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float spinSpeed = 1f;

    [BoxGroup("Анимации"), Expandable, Tooltip("Углы и тайминги анимаций. Раскройте, чтобы править")]
    public RevolverAnimationSettings animationSettings;

    [BoxGroup("Отладка")] public bool logEvents = true;

    [ShowNativeProperty] public bool IsOpen => isOpen;
    // Барабан открыт и стоит (не открывается, не крутится) — можно осматривать
    public bool IsOpenIdle => isOpen && animator != null && InState("OpenIdle");

    // Подписываются руки (FPSArmsRig): убрать / вернуть левую руку
    public event System.Action CylinderOpening;
    public event System.Action CylinderClosing;

    Animator animator;
    RevolverCylinder cylinder;
    RevolverShooter shooter;
    readonly System.Collections.Generic.HashSet<string> animParams = new System.Collections.Generic.HashSet<string>();
    bool isOpen;
    float nextShotTime;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (muzzle == null) muzzle = transform.Find("Muzzle");
        foreach (var p in animator.parameters) animParams.Add(p.name);
        cylinder = GetComponent<RevolverCylinder>();
        shooter = GetComponent<RevolverShooter>();
    }

    void ApplySpeeds()
    {
        SetSpeed("FireSpeed", fireSpeed); SetSpeed("OpenSpeed", openSpeed);
        SetSpeed("CloseSpeed", closeSpeed); SetSpeed("SpinSpeed", spinSpeed);
    }

    void SetSpeed(string param, float value)
    {
        if (animParams.Contains(param)) animator.SetFloat(param, value);
    }

    void Update()
    {
        ApplySpeeds();

        // Пока курсор не захвачен (нажат Esc), клик только возвращает курсор
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (ReadToggleCylinder()) ToggleCylinder();

        if (ReadFire())
        {
            if (isOpen) Spin();
            else Fire();
        }
    }

    // ---------- Действия ----------
    public void Fire()
    {
        if (isOpen || Time.time < nextShotTime || !InState("Idle")) return;
        nextShotTime = Time.time + fireCooldown;
        animator.SetTrigger("Fire");

        // Самовзвод: пока нажимается спуск, барабан проворачивается на одну камору
        float cock = (animationSettings != null ? animationSettings.fireCockTime : 0.117f) / Mathf.Max(fireSpeed, 0.01f);
        if (cylinder != null) cylinder.AdvanceForShot(cock);
    }

    public void ToggleCylinder()
    {
        if (!isOpen && InState("Idle"))
        {
            isOpen = true;
            animator.SetTrigger("Open");
            CylinderOpening?.Invoke();
        }
        else if (isOpen && (InState("OpenIdle") || InState("Spin")))
        {
            isOpen = false;
            animator.ResetTrigger("Spin");
            animator.SetTrigger("Close");
            CylinderClosing?.Invoke();
        }
    }

    public void Spin()
    {
        if (isOpen && InState("OpenIdle")) animator.SetTrigger("Spin");
    }

    bool InState(string state) => animator.GetCurrentAnimatorStateInfo(0).IsName(state) && !animator.IsInTransition(0);

    // ---------- Animation Events ----------
    // Курок упал на камору под ним
    public void OnShot()
    {
        if (cylinder == null)
        {
            // Без учёта патронов — стреляем всегда
            if (shooter != null) shooter.Fire(null);
            if (logEvents) Debug.Log("БАХ!");
            return;
        }

        int chamber = cylinder.Model.UnderHammer;
        var state = cylinder.Model.GetState(chamber);
        if (cylinder.TryFire(out var ammo))
        {
            if (shooter != null) shooter.Fire(ammo);
            if (logEvents) Debug.Log($"БАХ! (камора {chamber})  {cylinder.Model.Describe()}");
        }
        else if (logEvents)
        {
            Debug.Log($"Щёлк — в каморе {chamber} {(state == RoundState.Spent ? "стреляная гильза" : "пусто")}  {cylinder.Model.Describe()}");
        }
        // Позже: вспышка, звук, отдача
    }

    public void OnSpinStart()
    {
        if (logEvents) Debug.Log("Барабан раскручен");
        // Здесь позже: звук трещотки барабана
    }

    public void OnCylinderClosed()
    {
        if (logEvents) Debug.Log("Щёлк — барабан закрыт");
        // Здесь позже: звук защёлки
    }

    // ---------- Кнопки в Inspector для проверки (в Play Mode) ----------
    [Button("Тест: выстрел")] void TestFire() { if (Application.isPlaying) Fire(); }
    [Button("Тест: открыть / закрыть")] void TestToggle() { if (Application.isPlaying) ToggleCylinder(); }
    [Button("Тест: раскрутить")] void TestSpin() { if (Application.isPlaying) Spin(); }

    // ---------- Кнопки для анимаций (только в редакторе) ----------
    [Button("Создать недостающие анимации")]
    void CreateMissingAnimations()
    {
#if UNITY_EDITOR
        if (animationSettings == null) animationSettings = RevolverAnimationSettings.GetOrCreateDefault();
        animationSettings.Generate(false);
#endif
    }

    [Button("Перезаписать анимации из настроек")]
    void OverwriteAnimations()
    {
#if UNITY_EDITOR
        if (animationSettings == null) animationSettings = RevolverAnimationSettings.GetOrCreateDefault();
        animationSettings.OverwriteButton();
#endif
    }

    // ---------- Ввод ----------
#if ENABLE_INPUT_SYSTEM
    bool ReadFire()           => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    bool ReadToggleCylinder() => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
    bool ReadFire()           => Input.GetMouseButtonDown(0);
    bool ReadToggleCylinder() => Input.GetKeyDown(KeyCode.R);
#endif
}