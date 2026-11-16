using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Items;
using Pistol.Player;

namespace Pistol.Weapons.Revolver
{
    // Chamber selection in inspect mode (Alt). Goes on Revolver (next to RevolverInspect and RevolverCylinder).
    //
    //   - A white outline around the rear face of the cylinder.
    //   - White rings over empty chambers, light grey over spent cases,
    //     nothing over live rounds.
    //   - The ring under the cursor is highlighted. Clicking it raises ChamberClicked.
    //
    // Until the hand animation exists (next step), "instant actions" are on:
    // clicking an empty chamber inserts a round immediately, clicking a spent one removes the case.
    // Rings and outline are flat meshes built in code, no separate assets.
    [RequireComponent(typeof(RevolverInspect))]
    public class RevolverChamberPicker : MonoBehaviour, IItemContextUser
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

        [ShowNativeProperty] public int Hovered => hovered;

        // Chamber click: index and what it held (Empty or Spent; live rounds never produce clicks)
        public event System.Action<int, RoundState> ChamberClicked;

        RevolverInspect inspect;
        RevolverCylinder cylinder;
        PlayerContext context;
        Camera cam;
        Material material;
        Mesh ringMesh, outlineMesh;
        readonly MeshRenderer[] rings = new MeshRenderer[CylinderModel.Count];
        MeshRenderer outline;
        MaterialPropertyBlock block;
        int hovered = -1;
        float builtRingR, builtRingW, builtOutR, builtOutW;

        // Pointer and clicks in Inspect mode come from the player context
        public void Bind(ItemContext itemContext)
        {
            cam = itemContext.Camera;
            context = itemContext.Player;
            debug = itemContext.Debug;
        }

        DebugSettings debug;
        bool LogClicks => debug != null && debug.WeaponEvents;

        void Start()
        {
            inspect = GetComponent<RevolverInspect>();
            cylinder = GetComponent<RevolverCylinder>();
            block = new MaterialPropertyBlock();

            if (cylinder == null || cylinder.CylinderTransform == null || cylinder.Model == null)
            {
                Debug.LogWarning("RevolverChamberPicker: needs a RevolverCylinder with the Cylinder assigned.", this);
                enabled = false;
                return;
            }
            CreateMaterial();
            RebuildMeshes();

            for (int i = 0; i < CylinderModel.Count; i++)
                rings[i] = CreatePart("ChamberRing_" + i, cylinder.GetSocket(i), ringMesh, new Vector3(0f, 0f, ringDepth));
            outline = CreatePart("RearOutline", cylinder.CylinderTransform, outlineMesh, new Vector3(0f, 0f, outlineDepth));
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
                RebuildMeshes();   // sliders were changed in Play Mode

            // Hover and click only once the weapon is almost in the inspect pose
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
                // Under the cursor the ring gets bigger and thicker (thickness grows with the XY scale)
                tr.localScale = Vector3.Lerp(tr.localScale, new Vector3(s, s, 1f), 1f - Mathf.Exp(-20f * Time.deltaTime));
            }

            Color oc = outlineColor; oc.a *= t;
            SetColor(outline, oc);
            outline.transform.localPosition = new Vector3(0f, 0f, outlineDepth);

            if (hovered >= 0 && context != null && context.SelectPressed) Click(hovered);
        }

        // ---------- Hover ----------
        // Project each chamber to the screen; pick the one closest to the cursor if the cursor is inside its ring
        int FindHovered()
        {
            if (cam == null) return -1;
            Vector2 mouse = context != null ? context.PointerPosition : Vector2.zero;
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
            // Highlight nothing over a live round (and don't fall back to a neighbouring chamber)
            if (best >= 0 && cylinder.Model.GetState(best) == RoundState.Live) return -1;
            return best;
        }

        void Click(int i)
        {
            var model = cylinder.Model;
            var state = model.GetState(i);
            if (state == RoundState.Live) return;   // live round: nothing to do
            ChamberClicked?.Invoke(i, state);

            if (!instantActions) return;
            if (state == RoundState.Empty)
            {
                model.Insert(i, cylinder.Ammo);
                if (LogClicks) Debug.Log($"Chamber {i}: round inserted  {model.Describe()}");
            }
            else if (state == RoundState.Spent)
            {
                model.RemoveSpent(i);
                if (LogClicks) Debug.Log($"Chamber {i}: spent case removed  {model.Describe()}");
            }
        }

        // ---------- Visuals ----------
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
            go.transform.localRotation = Quaternion.identity;   // the ring lies in the XY plane, the cylinder's rear face
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void CreateMaterial()
        {
            // Built-in shader (present in every build): color = vertex color x _Color, with transparency.
            // Writes depth so the background blur (inspect focus) doesn't blur the rings themselves.
            material = DebugMaterials.CreateColored(zWrite: true, renderQueue: 3100);
        }

        void RebuildMeshes()
        {
            builtRingR = ringRadius; builtRingW = ringWidth;
            builtOutR = outlineRadius; builtOutW = outlineWidth;
            ringMesh = BuildRing(ringMesh, "ChamberRing", ringRadius - ringWidth * 0.5f, ringRadius + ringWidth * 0.5f, ringWidth * 0.6f, 48);
            outlineMesh = BuildRing(outlineMesh, "RearOutline", outlineRadius - outlineWidth * 0.5f, outlineRadius + outlineWidth * 0.5f, outlineWidth * 1.2f, 96);
        }

        // Flat ring with soft edges: 4 circles, transparent, bright, bright, transparent
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
    }
}
