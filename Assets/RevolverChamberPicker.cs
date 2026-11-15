using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Выбор каморы в режиме осмотра (Alt). Вешается на Revolver (рядом с RevolverInspect и RevolverCylinder).
//
//   • По краю заднего торца барабана — белая обводка.
//   • Над пустыми каморами — белые кружки, над стреляными гильзами — светло-серые,
//     над целыми патронами — ничего.
//   • Кружок под курсором подсвечивается. Клик по нему — событие ChamberClicked.
//
// Пока нет анимации рук (следующий шаг), включены «мгновенные действия»:
// клик по пустой каморе сразу вставляет патрон, по стреляной — убирает гильзу.
// Кружки и обводка — плоские сетки, построенные в коде, без отдельных ассетов.
[RequireComponent(typeof(RevolverInspect))]
public class RevolverChamberPicker : MonoBehaviour
{
    [BoxGroup("Кружки над каморами"), Tooltip("Цвет над пустой каморой")]
    public Color emptyColor = new Color(1f, 1f, 1f, 0.9f);
    [BoxGroup("Кружки над каморами"), Tooltip("Цвет над стреляной гильзой")]
    public Color spentColor = new Color(0.68f, 0.68f, 0.68f, 0.85f);
    [BoxGroup("Кружки над каморами"), Range(0.002f, 0.008f), Tooltip("Радиус кружка, м (камора — 4.6 мм)")]
    public float ringRadius = 0.0044f;
    [BoxGroup("Кружки над каморами"), Range(0.0002f, 0.003f), Tooltip("Толщина кружка, м")]
    public float ringWidth = 0.0007f;
    [BoxGroup("Кружки над каморами"), Range(1f, 1.5f), Tooltip("Во сколько раз увеличивается кружок под курсором")]
    public float hoverScale = 1.15f;
    [BoxGroup("Кружки над каморами"), Range(1f, 3f), Tooltip("Во сколько раз ярче (непрозрачнее) кружок под курсором")]
    public float hoverBoost = 1.6f;
    [BoxGroup("Кружки над каморами"), Range(-0.004f, 0.004f), Tooltip("Сдвиг кружков от донца патрона к глазам (−) или внутрь (+), м")]
    public float ringDepth = -0.0006f;

    [BoxGroup("Обводка торца"), Tooltip("Цвет обводки заднего торца")]
    public Color outlineColor = new Color(1f, 1f, 1f, 0.75f);
    [BoxGroup("Обводка торца"), Range(0.01f, 0.025f), Tooltip("Радиус обводки, м (торец барабана — 15.2 мм)")]
    public float outlineRadius = 0.0154f;
    [BoxGroup("Обводка торца"), Range(0.0002f, 0.003f), Tooltip("Толщина обводки, м")]
    public float outlineWidth = 0.0006f;
    [BoxGroup("Обводка торца"), Range(-0.03f, 0f), Tooltip("Где задний торец по оси барабана, м")]
    public float outlineDepth = -0.0221f;

    [BoxGroup("Выбор"), Range(1f, 2f), Tooltip("Насколько далеко от кружка ещё срабатывает наведение (в радиусах кружка)")]
    public float pickTolerance = 1.3f;
    [BoxGroup("Выбор"), Tooltip("Временно: клик сразу вставляет патрон / убирает гильзу (пока нет анимации рук)")]
    public bool instantActions = true;
    [BoxGroup("Выбор")] public bool logClicks = true;

    [ShowNativeProperty] public int Hovered => hovered;

    // Клик по каморе: номер и что в ней было (Empty или Spent; по целым патронам клики не приходят)
    public event System.Action<int, RoundState> ChamberClicked;

    RevolverInspect inspect;
    RevolverCylinder cylinder;
    Camera cam;
    Material material;
    Mesh ringMesh, outlineMesh;
    readonly MeshRenderer[] rings = new MeshRenderer[CylinderModel.Count];
    MeshRenderer outline;
    MaterialPropertyBlock block;
    int hovered = -1;
    float builtRingR, builtRingW, builtOutR, builtOutW;

    void Start()
    {
        inspect = GetComponent<RevolverInspect>();
        cylinder = GetComponent<RevolverCylinder>();
        cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;
        block = new MaterialPropertyBlock();

        if (cylinder == null || cylinder.cylinder == null || cylinder.Model == null)
        {
            Debug.LogWarning("RevolverChamberPicker: нужен RevolverCylinder с найденным барабаном (Crane/Cylinder).");
            enabled = false;
            return;
        }
        CreateMaterial();
        RebuildMeshes();

        for (int i = 0; i < CylinderModel.Count; i++)
            rings[i] = CreatePart("ChamberRing_" + i, cylinder.GetSocket(i), ringMesh, new Vector3(0f, 0f, ringDepth));
        outline = CreatePart("RearOutline", cylinder.cylinder, outlineMesh, new Vector3(0f, 0f, outlineDepth));
        SetVisible(false);
    }

    void Update()
    {
        float t = Mathf.SmoothStep(0f, 1f, inspect.Amount);
        bool visible = t > 0.001f;
        SetVisible(visible);
        if (!visible) { hovered = -1; return; }

        if (!Mathf.Approximately(builtRingR, ringRadius) || !Mathf.Approximately(builtRingW, ringWidth)
         || !Mathf.Approximately(builtOutR, outlineRadius) || !Mathf.Approximately(builtOutW, outlineWidth))
            RebuildMeshes();   // ползунки двигали в Play Mode

        // Наводиться и кликать можно, только когда оружие почти встало в позу осмотра
        hovered = inspect.IsInspecting && t > 0.8f ? FindHovered() : -1;

        var model = cylinder.Model;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
        for (int i = 0; i < CylinderModel.Count; i++)
        {
            var state = model.GetState(i);
            bool show = state != RoundState.Live;
            rings[i].enabled = show;
            if (!show) continue;

            bool hover = i == hovered;
            Color c = state == RoundState.Empty ? emptyColor : spentColor;
            if (hover) c = new Color(Mathf.Min(1f, c.r * 1.1f), Mathf.Min(1f, c.g * 1.1f), Mathf.Min(1f, c.b * 1.1f),
                                     Mathf.Min(1f, c.a * hoverBoost) * pulse);
            c.a *= t;
            SetColor(rings[i], c);

            var tr = rings[i].transform;
            tr.localPosition = new Vector3(0f, 0f, ringDepth);
            float s = hover ? hoverScale : 1f;
            // Под курсором кружок и больше, и толще (толщина растёт за счёт масштаба по XY)
            tr.localScale = Vector3.Lerp(tr.localScale, new Vector3(s, s, 1f), 1f - Mathf.Exp(-20f * Time.deltaTime));
        }

        Color oc = outlineColor; oc.a *= t;
        SetColor(outline, oc);
        outline.transform.localPosition = new Vector3(0f, 0f, outlineDepth);

        if (hovered >= 0 && ReadClick()) Click(hovered);
    }

    // ---------- Наведение ----------
    // Каждую камору проецируем на экран; выбирается ближайшая к курсору, если курсор внутри её кружка
    int FindHovered()
    {
        if (cam == null) return -1;
        Vector2 mouse = ReadMouse();
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < CylinderModel.Count; i++)
        {
            Transform socket = cylinder.GetSocket(i);
            Vector3 c = cam.WorldToScreenPoint(socket.TransformPoint(0f, 0f, ringDepth));
            if (c.z <= 0f) continue;
            Vector3 e = cam.WorldToScreenPoint(socket.TransformPoint(ringRadius, 0f, ringDepth));
            Vector3 e2 = cam.WorldToScreenPoint(socket.TransformPoint(0f, ringRadius, ringDepth));
            float radiusPx = Mathf.Max(Vector2.Distance(c, e), Vector2.Distance(c, e2));
            float d = Vector2.Distance(mouse, c);
            if (d < radiusPx * pickTolerance && d < bestDist) { bestDist = d; best = i; }
        }
        // Над целым патроном ничего не подсвечиваем (и соседнюю камору не выбираем)
        if (best >= 0 && cylinder.Model.GetState(best) == RoundState.Live) return -1;
        return best;
    }

    void Click(int i)
    {
        var model = cylinder.Model;
        var state = model.GetState(i);
        if (state == RoundState.Live) return;   // по целому патрону — ничего
        ChamberClicked?.Invoke(i, state);

        if (!instantActions) return;
        if (state == RoundState.Empty)
        {
            model.Insert(i, cylinder.Ammo);
            if (logClicks) Debug.Log($"Камора {i}: вставлен патрон  {model.Describe()}");
        }
        else if (state == RoundState.Spent)
        {
            model.RemoveSpent(i);
            if (logClicks) Debug.Log($"Камора {i}: гильза вынута  {model.Describe()}");
        }
    }

    // ---------- Визуал ----------
    void SetVisible(bool on)
    {
        if (outline == null) return;
        if (outline.enabled != on) outline.enabled = on;
        if (!on) foreach (var r in rings) if (r != null) r.enabled = false;
    }

    void SetColor(Renderer r, Color c)
    {
        block.SetColor("_Color", c);
        r.SetPropertyBlock(block);
    }

    MeshRenderer CreatePart(string name, Transform parent, Mesh mesh, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;   // кольцо лежит в плоскости XY — торец барабана
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    void CreateMaterial()
    {
        // Встроенный шейдер (есть в любой сборке): цвет = цвет вершины × _Color, с прозрачностью.
        // Пишет глубину, чтобы размытие фона (фокус в режиме осмотра) не размывало сами кружки.
        material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", 1);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        material.renderQueue = 3100;
    }

    void RebuildMeshes()
    {
        builtRingR = ringRadius; builtRingW = ringWidth;
        builtOutR = outlineRadius; builtOutW = outlineWidth;
        ringMesh = BuildRing(ringMesh, "ChamberRing", ringRadius - ringWidth * 0.5f, ringRadius + ringWidth * 0.5f, ringWidth * 0.6f, 48);
        outlineMesh = BuildRing(outlineMesh, "RearOutline", outlineRadius - outlineWidth * 0.5f, outlineRadius + outlineWidth * 0.5f, outlineWidth * 1.2f, 96);
    }

    // Плоское кольцо с мягкими краями: 4 окружности — прозрачная, яркая, яркая, прозрачная
    static Mesh BuildRing(Mesh mesh, string name, float rIn, float rOut, float soft, int seg)
    {
        if (mesh == null) mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.Clear();
        float[] radii = { Mathf.Max(0f, rIn - soft), rIn, rOut, rOut + soft };
        float[] alpha = { 0f, 1f, 1f, 0f };
        var v = new Vector3[seg * 4];
        var col = new Color[seg * 4];
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            for (int k = 0; k < 4; k++)
            {
                v[i * 4 + k] = new Vector3(cs * radii[k], sn * radii[k], 0f);
                col[i * 4 + k] = new Color(1f, 1f, 1f, alpha[k]);
            }
        }
        var tris = new int[seg * 3 * 6];
        int n = 0;
        for (int i = 0; i < seg; i++)
        {
            int a = i * 4, b = ((i + 1) % seg) * 4;
            for (int k = 0; k < 3; k++)
            {
                tris[n++] = a + k; tris[n++] = b + k; tris[n++] = b + k + 1;
                tris[n++] = a + k; tris[n++] = b + k + 1; tris[n++] = a + k + 1;
            }
        }
        mesh.vertices = v;
        mesh.colors = col;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (ringMesh != null) Destroy(ringMesh);
        if (outlineMesh != null) Destroy(outlineMesh);
    }

    // ---------- Ввод ----------
#if ENABLE_INPUT_SYSTEM
    Vector2 ReadMouse() => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    bool ReadClick()    => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    Vector2 ReadMouse() => Input.mousePosition;
    bool ReadClick()    => Input.GetMouseButtonDown(0);
#endif
}
