using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // A cardboard target with real holes: every hit cuts a round hole (slightly torn) into a mask texture,
    // the PaperTargetCutout shader doesn't draw the card there, and the bullet flies on behind it
    // (IBulletPassThrough). A bullet through an existing hole doesn't touch the card at all.
    // The mask maps onto the card by its local X/Y (the mesh bounds), the same way in the shader and here,
    // so it doesn't depend on UVs. Reset closes all holes.
    //
    // Goes on the card (PaperTarget_Card, with a MeshCollider). Its material must use Pistol/PaperTargetCutout.
    public class PaperTargetCard : ShootingTarget, IBulletPassThrough
    {
        [BoxGroup("Дыры"), SerializeField, Range(256, 2048), Tooltip("Разрешение маски дыр по ширине картона, пикселей. Больше — ровнее края")]
        int maskWidth = 512;
        [BoxGroup("Дыры"), SerializeField, Range(0.5f, 3f), Tooltip("Размер дыры относительно калибра пули")]
        float holeScale = 1f;
        [BoxGroup("Дыры"), SerializeField, Range(0f, 0.6f), Tooltip("Рваность края: 0 — ровный круг")]
        float tear = 0.25f;

        static readonly int MaskId = Shader.PropertyToID("_HoleMask");
        static readonly int CardRectId = Shader.PropertyToID("_CardRect");

        Texture2D mask;
        byte[] pixels;
        int width, height;
        Vector2 center, size;
        Material material;
        bool dirty;

        [ShowNativeProperty] public int Holes { get; private set; }

        void Awake()
        {
            var mf = GetComponent<MeshFilter>();
            var bounds = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(0.46f, 0.76f, 0.01f));
            center = bounds.center;
            size = new Vector2(Mathf.Max(bounds.size.x, 0.01f), Mathf.Max(bounds.size.y, 0.01f));

            width = maskWidth;
            height = Mathf.Max(16, Mathf.RoundToInt(maskWidth * size.y / size.x));
            mask = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = name + " holes",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            pixels = new byte[width * height];
            Clear();

            var r = GetComponent<Renderer>();
            if (r != null)
            {
                material = r.material;   // own copy: every card has its own holes
                if (!material.HasProperty(MaskId))
                    Debug.LogWarning($"PaperTargetCard: material {material.name} doesn't use the Pistol/PaperTargetCutout shader, holes won't show.", this);
                material.SetTexture(MaskId, mask);
                material.SetVector(CardRectId, new Vector4(size.x, size.y, center.x, center.y));
            }
        }

        // Where on the mask a world point is, 0..1
        Vector2 MaskUV(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2((local.x - center.x) / size.x + 0.5f, (local.y - center.y) / size.y + 0.5f);
        }

        public bool PassesThrough(in BulletHit hit, out bool counts)
        {
            Vector2 uv = MaskUV(hit.point);
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height - 1);
            counts = pixels[y * width + x] > 127;   // paper there: a new hole; already a hole: flies through untouched
            return true;
        }

        protected override void OnHit(in BulletHit hit)
        {
            float caliber = hit.ammo != null ? hit.ammo.holeSize : 0.009f;
            Punch(MaskUV(hit.point), caliber * 0.5f * holeScale);
        }

        // Cuts a slightly torn round hole of the given radius (m) at uv
        void Punch(Vector2 uv, float radius)
        {
            float pxPerM = width / size.x;
            float r = radius * pxPerM;
            int cx = Mathf.RoundToInt(uv.x * width), cy = Mathf.RoundToInt(uv.y * height);
            int reach = Mathf.CeilToInt(r * (1f + tear)) + 1;
            float seed = Random.value * 100f;
            for (int y = cy - reach; y <= cy + reach; y++)
            {
                if (y < 0 || y >= height) continue;
                for (int x = cx - reach; x <= cx + reach; x++)
                {
                    if (x < 0 || x >= width) continue;
                    float dx = x - cx, dy = y - cy;
                    float a = Mathf.Atan2(dy, dx);
                    float edge = r * (1f + tear * (Mathf.PerlinNoise(seed + Mathf.Cos(a) * 1.5f, seed + Mathf.Sin(a) * 1.5f) - 0.5f) * 2f);
                    if (dx * dx + dy * dy <= edge * edge) pixels[y * width + x] = 0;
                }
            }
            Holes++;
            dirty = true;
        }

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            mask.SetPixelData(pixels, 0);
            mask.Apply(false);
        }

        void Clear()
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;
            Holes = 0;
            dirty = true;
        }

        public override void ResetTarget() => Clear();

        void OnDestroy()
        {
            if (mask != null) Destroy(mask);
            if (material != null) Destroy(material);
        }
    }
}
