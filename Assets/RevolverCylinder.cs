using UnityEngine;
using NaughtyAttributes;

// Барабан: хранит логику камор (CylinderModel), показывает патроны в 6 гнёздах
// и сам поворачивает барабан на каждый выстрел.
// Вешается на Revolver. Гнёзда Chamber_0…5 создаются внутри Crane/Cylinder при запуске игры.
//
// Экстрактор: в модели две детали — звезда (Crane/Ejector) и шток (Crane/EjectorRod).
// Их сетки построены в системе координат барабана, но положение из FBX ненадёжно,
// поэтому скрипт сам ставит их по барабану: звезду кладёт внутрь барабана (крутится с ним),
// шток держит на Crane в точке барабана (не крутится). Вне игры — кнопка «Поставить звезду и шток на место».
// Ползунок Eject Stroke (0…1) выдвигает звезду вместе с патронами назад, а шток — внутрь.
//
// Почему поворот делает скрипт, а не анимация: анимация Fire каждый раз крутит барабан 0° → −60°
// и потом возвращает в 0°. Для пустого барабана это незаметно (он симметричный), но с патронами
// они бы «отпрыгивали» назад. Поэтому скрипт держит накопленный угол (камора × 60°) сам,
// а из анимации берёт только раскрутку (Spin).
[DefaultExecutionOrder(100)]
public class RevolverCylinder : MonoBehaviour
{
    [BoxGroup("Патроны"), Expandable, Tooltip("Тип патрона. Пусто — .357 Magnum по умолчанию")]
    public AmmoType ammoType;
    [BoxGroup("Патроны"), Tooltip("Зарядить все 6 камор при запуске")]
    public bool startLoaded = true;

    [BoxGroup("Гнёзда камор"), Tooltip("Барабан (найдётся сам: Crane/Cylinder)")]
    public Transform cylinder;
    [BoxGroup("Гнёзда камор"), Range(0.005f, 0.02f), Tooltip("Расстояние от оси барабана до оси каморы, м")]
    public float chamberRadius = 0.0100f;
    [BoxGroup("Гнёзда камор"), Range(-0.04f, 0.04f), Tooltip("Где задний торец барабана по оси Z (донца патронов), м")]
    public float rearFace = -0.0230f;
    [BoxGroup("Гнёзда камор"), Range(-180f, 180f), Tooltip("Поворот всей шестёрки гнёзд, градусы. Камора 0 должна стоять напротив ствола")]
    public float angleOffset = 0f;
    [BoxGroup("Гнёзда камор"), Tooltip("Рисовать гнёзда в окне Scene")]
    public bool showGizmos = true;

    [BoxGroup("Экстрактор"), Tooltip("Звезда экстрактора (найдётся сама: Crane/Ejector)")]
    public Transform ejector;
    [BoxGroup("Экстрактор"), Tooltip("Шток экстрактора (найдётся сам: Crane/EjectorRod)")]
    public Transform ejectorRod;
    [BoxGroup("Экстрактор"), Range(0f, 0.03f), Tooltip("Полный ход звезды назад, м")]
    public float ejectorTravel = 0.012f;
    [BoxGroup("Экстрактор"), Range(0f, 1f), Tooltip("Насколько выдвинута звезда: 0 — на месте, 1 — до конца. Можно двигать в Play Mode для проверки")]
    public float ejectStroke = 0f;

    [BoxGroup("Поворот"), Tooltip("Шаг поворота на одну камору (берётся из настроек анимаций, обычно −60)")]
    public float stepAngle = -60f;

    [ShowNativeProperty] public string ChambersState => model != null ? model.Describe() : "—";
    [ShowNativeProperty] public int LiveRounds => model != null ? model.CountOf(RoundState.Live) : 0;

    public CylinderModel Model => model;
    public AmmoType Ammo => ammoType;

    CylinderModel model;
    readonly Transform[] sockets = new Transform[CylinderModel.Count];
    readonly GameObject[] liveViews = new GameObject[CylinderModel.Count];
    readonly GameObject[] spentViews = new GameObject[CylinderModel.Count];

    Animator animator;
    float indexAngle, angleFrom, angleTo, turnTime, turnDuration;

    void Awake()
    {
        if (ammoType == null) ammoType = AmmoType.CreateDefault();
        if (cylinder == null) cylinder = transform.Find("Crane/Cylinder");
        animator = GetComponent<Animator>();

        var fire = GetComponent<RevolverFire>();
        if (fire != null && fire.animationSettings != null) stepAngle = fire.animationSettings.cylinderStep;

        model = new CylinderModel();
        model.ChamberChanged += RefreshView;
        if (cylinder != null) { BuildSockets(); SetupEjector(); }
        if (startLoaded) model.FillAll(ammoType);
        else for (int i = 0; i < CylinderModel.Count; i++) RefreshView(i);
    }

    // ---------- Гнёзда и визуал ----------
    // Камора k стоит под углом 90° − шаг·k: камора 0 — сверху, напротив ствола,
    // а при повороте на шаг под курок приходит камора 1.
    Vector3 SocketLocalPosition(int k)
    {
        float a = (90f - stepAngle * k + angleOffset) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * chamberRadius, Mathf.Sin(a) * chamberRadius, rearFace);
    }

    void BuildSockets()
    {
        for (int k = 0; k < CylinderModel.Count; k++)
        {
            string n = "Chamber_" + k;
            var t = cylinder.Find(n);
            if (t == null)
            {
                t = new GameObject(n).transform;
                t.SetParent(cylinder, false);
            }
            t.localPosition = SocketLocalPosition(k);
            t.localRotation = Quaternion.identity;   // +Z — от донца к пуле (к дулу)
            sockets[k] = t;

            liveViews[k] = CreateView(t, ammoType.livePrefab, false);
            spentViews[k] = CreateView(t, ammoType.spentPrefab, true);
        }
    }

    void SetupEjector()
    {
        if (ejector == null) ejector = transform.Find("Crane/Ejector");
        if (ejectorRod == null) ejectorRod = transform.Find("Crane/EjectorRod");
        if (ejector != null)
        {
            // Сетка звезды уже в координатах барабана — внутри него она стоит в нуле
            ejector.SetParent(cylinder, false);
            ejector.localPosition = Vector3.zero;
            ejector.localRotation = Quaternion.identity;
            ejector.localScale = Vector3.one;
        }
        if (ejectorRod != null && cylinder.parent != null)
        {
            ejectorRod.SetParent(cylinder.parent, false);
            ejectorRod.localScale = cylinder.localScale;
        }
    }

    void ApplyEjector()
    {
        float d = ejectorTravel * Mathf.Clamp01(ejectStroke);
        for (int k = 0; k < CylinderModel.Count; k++)
            if (sockets[k] != null) sockets[k].localPosition = SocketLocalPosition(k) + new Vector3(0f, 0f, -d);
        if (ejector != null) ejector.localPosition = new Vector3(0f, 0f, -d);
        // Шток стоит там же, где барабан, но без его вращения, и уходит вдоль оси внутрь
        if (ejectorRod != null)
        {
            ejectorRod.localRotation = Quaternion.identity;
            // Барабан в Crane повёрнут только вокруг своей оси Z, поэтому «без вращения» — это identity
            ejectorRod.localPosition = cylinder.localPosition + new Vector3(0f, 0f, -d * cylinder.localScale.z);
        }
    }

    GameObject CreateView(Transform socket, GameObject prefab, bool spent)
    {
        GameObject go;
        bool spentFromLive = spent && prefab == null && ammoType.livePrefab != null;
        if (spentFromLive) prefab = ammoType.livePrefab;
        if (prefab != null)
        {
            go = Instantiate(prefab, socket);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(ammoType.modelRotation);
            go.transform.localScale *= ammoType.modelScale;
            if (spentFromLive)
            {
                // Стреляная гильза = тот же патрон без пули и чуть темнее
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Bullet")) t.gameObject.SetActive(false);
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", ammoType.spentTint);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
            }
        }
        else
        {
            // Заглушка: латунный цилиндр размером с патрон .357 (Ø 9.6 мм, длина 39 мм; гильза — 33 мм)
            go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = spent ? "Spent_Placeholder" : "Live_Placeholder";
            Destroy(go.GetComponent<Collider>());
            float len = spent ? 0.033f : 0.039f;
            go.transform.SetParent(socket, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // ось цилиндра Y → +Z гнезда
            go.transform.localPosition = new Vector3(0f, 0f, len * 0.5f);
            go.transform.localScale = new Vector3(0.0096f, len * 0.5f, 0.0096f);
            var r = go.GetComponent<Renderer>();
            r.material.color = spent ? new Color(0.42f, 0.33f, 0.17f) : new Color(0.8f, 0.62f, 0.27f);
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        return go;
    }

    void RefreshView(int i)
    {
        if (liveViews[i] == null) return;
        var s = model.GetState(i);
        liveViews[i].SetActive(s == RoundState.Live);
        spentViews[i].SetActive(s == RoundState.Spent);
    }

    public Transform GetSocket(int i) => sockets[CylinderModel.Wrap(i)];

    // ---------- Поворот барабана ----------
    // Вызывается при нажатии на спуск: барабан проворачивается на одну камору за время взвода курка
    public void AdvanceForShot(float duration)
    {
        model.Advance(1);
        angleFrom = indexAngle;
        angleTo += stepAngle;            // всегда ровно на одну камору в сторону шага
        turnTime = 0f;
        turnDuration = Mathf.Max(duration, 0.01f);
    }

    // Курок упал
    public bool TryFire(out AmmoType firedAmmo) => model.TryFire(out firedAmmo);

    void LateUpdate()
    {
        if (cylinder == null) return;

        if (turnTime < turnDuration)
        {
            turnTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(turnTime / turnDuration));
            indexAngle = Mathf.Lerp(angleFrom, angleTo, t);
            if (turnTime >= turnDuration)
            {
                angleTo = Mathf.Repeat(angleTo, 360f);   // чтобы угол не рос бесконечно
                indexAngle = angleTo;
            }
        }

        // Из анимации берём только раскрутку открытого барабана; анимацию поворота в Fire заменяем своим углом
        float spinAngle = 0f;
        if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("Spin"))
            spinAngle = Mathf.DeltaAngle(0f, cylinder.localEulerAngles.z);

        cylinder.localRotation = Quaternion.Euler(0f, 0f, indexAngle + spinAngle);
        ApplyEjector();
    }

    // ---------- Кнопки для проверки (Play Mode) ----------
    [Button("Зарядить все")] void BtnFill() { if (model != null) model.FillAll(ammoType); }
    [Button("Все — стреляные гильзы")] void BtnSpend() { if (model != null) model.SpendAll(); }
    [Button("Опустошить")] void BtnEmpty() { if (model != null) model.EmptyAll(); }

    // Вне игры: ставит звезду и шток туда же, где барабан (чтобы в окне Scene они не висели в стороне).
    // В игре скрипт делает это сам.
    [Button("Поставить звезду и шток на место")]
    void BtnPlaceEjector()
    {
        var cyl = cylinder != null ? cylinder : transform.Find("Crane/Cylinder");
        if (cyl == null) { Debug.LogWarning("Не найден Crane/Cylinder"); return; }
        foreach (var name in new[] { "Crane/Ejector", "Crane/EjectorRod" })
        {
            var t = transform.Find(name);
            if (t == null) t = cyl.Find(name.Substring(6));
            if (t == null) { Debug.LogWarning("Не найден " + name + " — обновлён ли Revolver.fbx?"); continue; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(t, "Place ejector");
#endif
            if (t.parent == cyl) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
            else { t.localPosition = cyl.localPosition; t.localRotation = cyl.localRotation; t.localScale = cyl.localScale; }
        }
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        var cyl = cylinder != null ? cylinder : transform.Find("Crane/Cylinder");
        if (cyl == null) return;
        for (int k = 0; k < CylinderModel.Count; k++)
        {
            Vector3 p = cyl.TransformPoint(SocketLocalPosition(k));
            Gizmos.color = k == 0 ? Color.red : new Color(1f, 0.85f, 0.3f);
            Gizmos.DrawWireSphere(p, 0.0048f);
            Gizmos.DrawLine(p, p + cyl.forward * 0.039f);
        }
    }
}