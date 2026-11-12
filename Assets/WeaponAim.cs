using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Прицеливание на ПКМ. Вешается на Revolver (внутри Main Camera).
// Оружие плавно уходит в центр к глазу, камера чуть приближает,
// а оружие и руки размываются — глаз фокусируется вдаль (Depth of Field).
public class WeaponAim : MonoBehaviour
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

    public enum FocusMode { FrontSight, Far, HandsAndSights }

    [BoxGroup("Фокус глаз в прицеле")] public bool blurWeapon = true;
    [BoxGroup("Фокус глаз в прицеле"), ShowIf("blurWeapon"),
     Tooltip("FrontSight — глаз на мушке: мушка и целик резкие, даль слегка мягкая.\nHandsAndSights — резкие ладони, целик и мушка; предплечья (они ближе к глазу) и даль размыты.\nFar — глаз вдаль: оружие слегка размыто")]
    public FocusMode focusMode = FocusMode.FrontSight;
    [BoxGroup("Фокус глаз в прицеле"), ShowIf("blurWeapon"), Range(0f, 1f), Tooltip("Сила размытия того, что не в фокусе")]
    public float blurStrength = 0.25f;
    [BoxGroup("Фокус глаз в прицеле"), ShowIf("blurWeapon"), Range(1f, 50f), Tooltip("Для режима Far: на каком расстоянии резкость, метры")]
    public float farFocusDistance = 8f;
    [BoxGroup("Фокус глаз в прицеле"), ShowIf("blurWeapon"), Range(0f, 1f),
     Tooltip("Для HandsAndSights: где точка резкости. 0 — на ладонях, 1 — на мушке. Чем ближе к ладоням, тем сильнее размыты предплечья")]
    public float focusBias = 0.3f;
    [BoxGroup("Фокус глаз в прицеле"), ShowIf("blurWeapon"), Tooltip("Размер размытия. Large — мягче и сильнее, но дороже для видеокарты")]
    public KernelSize blurQuality = KernelSize.Medium;

    [BoxGroup("Размытие по краям экрана"), Tooltip("В прицеле края экрана размываются — взгляд сосредоточен в центре")]
    public bool edgeBlur = true;
    [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0f, 1f), Tooltip("Сила размытия краёв")]
    public float edgeBlurIntensity = 0.7f;
    [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0f, 0.8f), Tooltip("Радиус чёткой зоны в центре (доля высоты экрана)")]
    public float edgeBlurRadius = 0.3f;
    [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0.05f, 1f), Tooltip("Плавность перехода от чёткого к размытому")]
    public float edgeBlurSoftness = 0.45f;
    [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0.005f, 0.08f), Tooltip("Ширина размытия")]
    public float edgeBlurSpread = 0.025f;
    [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0f, 0.3f), Tooltip("Насколько сужается чёткая зона у измотанного персонажа (эффект тоннеля)")]
    public float tiredTunnel = 0.1f;

    [BoxGroup("Настройка"), Tooltip("Держать прицел без ПКМ — удобно, чтобы подбирать положение в Play Mode")]
    public bool forceAim;

    [ShowNativeProperty] public float AimAmount => aim;
    public bool IsAiming => aim > 0.5f;
    [ShowNativeProperty] public float CurrentAimInTime => currentAimIn;

    Camera cam;
    RevolverFire revolver;
    PlayerMovement movement;
    float aim, baseFov = -1f, currentAimIn;
    WeaponSway sway;
    PostProcessVolume dofVolume;
    DepthOfField dof;
    EdgeBlur edge;

    void Start()
    {
        cam = GetComponentInParent<Camera>();
        revolver = GetComponent<RevolverFire>();
        movement = GetComponentInParent<PlayerMovement>();
        sway = GetComponent<WeaponSway>();
        if (useScenePosition) hipPosition = transform.localPosition;   // берём положение из сцены как есть
        else transform.localPosition = hipPosition;
    }

    void Update()
    {
        if (cam == null) return;
        if (baseFov < 0f) baseFov = cam.fieldOfView;   // берём FOV после того, как BodycamEffects его выставил

        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool cylinderOpen = revolver != null && revolver.IsOpen;
        bool want = (forceAim || (locked && ReadAim())) && !cylinderOpen;

        // Чем сильнее персонаж измотан, тем медленнее поднимает оружие к лицу
        float fatigue = Fatigue();
        currentAimIn = Mathf.Lerp(aimInTime, tiredAimInTime, fatigue)
                     + (movement != null && movement.IsRunning ? fromSprintExtraTime : 0f);
        float prevAim = aim;
        float time = want ? currentAimIn : aimOutTime;
        aim = Mathf.MoveTowards(aim, want ? 1f : 0f, Time.deltaTime / Mathf.Max(time, 0.01f));
        float t = Mathf.SmoothStep(0f, 1f, aim);

        // Оружие встало у глаза — небольшой толчок «в плечо», у уставшего сильнее и неровнее
        if (prevAim < 1f && aim >= 1f && sway != null)
        {
            float f = settleKick * Mathf.Lerp(1f, 2f, fatigue);
            sway.AddKick(new Vector3(f, Random.Range(-0.4f, 0.4f) * f, Random.Range(-0.6f, 0.6f) * f),
                         new Vector3(0f, -0.02f, -0.02f) * (f / 10f));
        }

        // Путь к лицу — по дуге снизу и с креном посередине, а не по прямой
        float mid = Mathf.Sin(t * Mathf.PI);
        transform.localPosition = Vector3.Lerp(hipPosition, aimPosition, t) + Vector3.down * (transitionDip * mid);
        transform.localRotation = Quaternion.Euler(0f, 0f, transitionRoll * mid);
        if (movement != null) movement.freeAimScale = 1f - t;   // в прицеле мёртвая зона исчезает — оружие по центру
        if (changeFov) cam.fieldOfView = Mathf.Lerp(baseFov, aimFov, t);

        UpdateBlur(t);
    }

    // 0 — отдохнул, 1 — измотан (отдышка после бега)
    float Fatigue()
    {
        if (movement == null) return 0f;
        float f = movement.Breathless;
        if (movement.IsExhausted) f = Mathf.Max(f, 0.8f);
        return Mathf.Clamp01(f);
    }

    void UpdateBlur(float t)
    {
        if (!blurWeapon && !edgeBlur)
        {
            if (dofVolume) { dofVolume.weight = 0f; dofVolume.enabled = false; }
            return;
        }
        if (dofVolume == null && !CreateAimVolume()) return;

        // Фокус глаз (Depth of Field)
        dof.enabled.value = blurWeapon;
        dof.focusDistance.value = CurrentFocusDistance();
        dof.aperture.value = Mathf.Lerp(22f, 2.8f, blurStrength);   // меньше диафрагма — сильнее размытие
        dof.focalLength.value = Mathf.Lerp(18f, 70f, blurStrength);
        dof.kernelSize.value = blurQuality;

        // Размытие по краям: у уставшего чёткая зона уже
        edge.enabled.value = edgeBlur;
        edge.intensity.value = edgeBlurIntensity;
        edge.radius.value = Mathf.Max(0f, edgeBlurRadius - tiredTunnel * Fatigue());
        edge.softness.value = edgeBlurSoftness;
        edge.spread.value = edgeBlurSpread;

        dofVolume.weight = t;
        dofVolume.enabled = t > 0.001f;
    }

    // Отдельный глобальный Volume для прицеливания (фокус + края) — его сила плавно растёт при прицеливании
    bool CreateAimVolume()
    {
        var layer = cam.GetComponent<PostProcessLayer>();
        if (layer == null)
        {
            Debug.LogWarning("WeaponAim: на камере нет Post-process Layer — размытие отключено.");
            blurWeapon = false; edgeBlur = false;
            return false;
        }
        int mask = layer.volumeLayer.value, layerIndex = 0;
        while (layerIndex < 32 && (mask & (1 << layerIndex)) == 0) layerIndex++;
        if (layerIndex >= 32) { blurWeapon = false; edgeBlur = false; return false; }

        if (Shader.Find("Hidden/Custom/EdgeBlur") == null && edgeBlur)
            Debug.LogWarning("WeaponAim: не найден шейдер EdgeBlur.shader — положите его в проект, иначе края не размываются.");

        dof = ScriptableObject.CreateInstance<DepthOfField>();
        dof.enabled.Override(blurWeapon);
        dof.focusDistance.Override(CurrentFocusDistance());
        dof.aperture.Override(16f);
        dof.focalLength.Override(30f);
        dof.kernelSize.Override(KernelSize.Medium);

        edge = ScriptableObject.CreateInstance<EdgeBlur>();
        edge.enabled.Override(edgeBlur);
        edge.intensity.Override(edgeBlurIntensity);
        edge.radius.Override(edgeBlurRadius);
        edge.softness.Override(edgeBlurSoftness);
        edge.spread.Override(edgeBlurSpread);

        dofVolume = PostProcessManager.instance.QuickVolume(layerIndex, 100f, dof, edge);
        dofVolume.weight = 0f;
        return true;
    }

    void OnDestroy()
    {
        if (dofVolume != null) RuntimeUtilities.DestroyVolume(dofVolume, true, true);
    }

    // ---------- Кнопки ----------
    // При добавлении компонента сразу запоминаем текущее положение револьвера
    void Reset() => hipPosition = transform.localPosition;

    [Button("Запомнить текущее положение как обычное"), HideIf("useScenePosition")]
    void CaptureHip() => hipPosition = transform.localPosition;

    [Button("Запомнить текущее положение как прицел")]
    void CaptureAim() => aimPosition = transform.localPosition;

    [Button("Поставить целик и мушку в центр экрана")]
    void AlignSights()
    {
        var s = GetComponent<WeaponSway>();
        Vector3 rear = s != null ? s.rearSightPoint : new Vector3(0f, 0.055f, 0.01f);
        aimPosition = new Vector3(-rear.x, -rear.y, aimDistance - rear.z);
        Debug.Log("Aim Position = " + aimPosition.ToString("F3"));
    }

    // Глаз фокусируется на мушке (дуло) или вдаль
    float CurrentFocusDistance()
    {
        if (focusMode == FocusMode.Far || cam == null) return farFocusDistance;
        if (focusMode == FocusMode.HandsAndSights)
        {
            // Между ладонями (точка хвата) и мушкой — всё, что ближе к глазу (предплечья), уходит в размытие
            Transform hand = transform.Find("IK_RightHand");
            Vector3 palms = hand != null ? hand.position : transform.position;
            float dHands = Vector3.Distance(cam.transform.position, palms);
            float dFront = Vector3.Distance(cam.transform.position, FrontSightWorld());
            return Mathf.Max(0.1f, Mathf.Lerp(dHands, dFront, focusBias));
        }
        return Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, FrontSightWorld()));
    }

    Vector3 FrontSightWorld()
    {
        var fire = GetComponent<RevolverFire>();
        Transform m = fire != null && fire.muzzle != null ? fire.muzzle : transform.Find("Muzzle");
        return m != null ? m.position + transform.up * 0.012f : transform.position + transform.forward * 0.25f;
    }

#if ENABLE_INPUT_SYSTEM
    bool ReadAim() => Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
    bool ReadAim() => Input.GetMouseButton(1);
#endif
}