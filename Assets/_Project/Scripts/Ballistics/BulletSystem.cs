using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Ballistics
{
    // Bullet flight. A bullet is a projectile, not an instant ray: every frame it travels one segment
    // at its own speed and drops under gravity. Hits are checked with a ray along that segment,
    // so bullets never pass through walls. No Rigidbody or collider is needed for bullets.
    // Created on demand on the first shot (a BulletSystem object in the scene).
    public class BulletSystem : MonoBehaviour
    {
        static BulletSystem instance;
        public static BulletSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<BulletSystem>();
                    if (instance == null) instance = new GameObject("BulletSystem").AddComponent<BulletSystem>();
                }
                return instance;
            }
        }

        [BoxGroup("Попадания"), Tooltip("Слои, в которые попадают пули")]
        public LayerMask hitMask = ~0;
        [BoxGroup("Попадания"), Range(10, 500), Tooltip("Сколько отметин от пуль держать одновременно (старые исчезают)")]
        public int maxHoles = 100;
        [BoxGroup("Попадания")] public Color holeColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);

        [BoxGroup("Отладка"), Tooltip("Рисовать путь каждой пули жёлтой линией")]
        public bool showBulletPaths = true;
        [BoxGroup("Отладка"), ShowIf("showBulletPaths"), Range(0.5f, 20f), Tooltip("Сколько секунд держать линию пути")]
        public float pathLifetime = 4f;
        [BoxGroup("Отладка"), ShowIf("showBulletPaths")] public Color pathColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        [BoxGroup("Отладка")] public bool logHits = true;

        class Bullet
        {
            public Vector3 pos, vel;
            public float age, travelled;
            public AmmoType ammo;
            public Transform ignoreRoot;
            public List<Vector3> path;
        }

        readonly List<Bullet> bullets = new List<Bullet>();
        readonly Queue<GameObject> holes = new Queue<GameObject>();
        static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        static Material sharedMaterial;
        static Mesh discMesh;

        // ---------- Firing ----------
        public void Fire(Vector3 origin, Vector3 direction, AmmoType ammo, Transform ignoreRoot)
        {
            var b = new Bullet
            {
                pos = origin,
                vel = direction.normalized * ammo.muzzleVelocity,
                ammo = ammo,
                ignoreRoot = ignoreRoot,
                path = showBulletPaths ? new List<Vector3> { origin } : null
            };
            bullets.Add(b);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                Vector3 g = Physics.gravity * b.ammo.gravityScale;
                Vector3 newVel = b.vel + g * dt;
                Vector3 newPos = b.pos + (b.vel + newVel) * 0.5f * dt;
                Vector3 seg = newPos - b.pos;

                if (Raycast(b.pos, seg, seg.magnitude, hitMask, b.ignoreRoot, out RaycastHit hit))
                {
                    b.travelled += hit.distance;
                    b.path?.Add(hit.point);
                    OnHit(b, hit, newVel.magnitude);
                    Finish(i);
                    continue;
                }

                b.travelled += seg.magnitude;
                b.pos = newPos;
                b.vel = newVel;
                b.age += dt;
                b.path?.Add(newPos);
                if (b.age > b.ammo.maxLifetime) Finish(i);
            }
        }

        void Finish(int index)
        {
            var b = bullets[index];
            if (b.path != null && b.path.Count > 1) DrawPath(b.path);
            bullets.RemoveAt(index);
        }

        // Ray along a segment, ignoring the shooter
        public static bool Raycast(Vector3 from, Vector3 dir, float dist, LayerMask mask, Transform ignoreRoot, out RaycastHit best)
        {
            best = default;
            if (dist <= 0f) return false;
            int n = Physics.RaycastNonAlloc(from, dir.normalized, hitBuffer, dist, mask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = hitBuffer[i];
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        void OnHit(Bullet b, RaycastHit hit, float speed)
        {
            var info = new BulletHit
            {
                point = hit.point, normal = hit.normal, direction = b.vel.normalized,
                speed = speed, distance = b.travelled, ammo = b.ammo, collider = hit.collider
            };

            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(info.direction * b.ammo.bulletMass * speed, hit.point, ForceMode.Impulse);

            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target is Object unityObj ? unityObj != null : target != null) target.TakeHit(info);

            PlaceHole(hit.point, hit.normal, b.ammo.holeSize);

            if (logHits)
                Debug.Log($"Hit: {hit.collider.name}, {info.distance:F1} m, speed {speed:F0} m/s");
        }

        // ---------- Bullet holes ----------
        void PlaceHole(Vector3 point, Vector3 normal, float size)
        {
            GameObject go;
            if (holes.Count >= maxHoles) go = holes.Dequeue();
            else go = CreateDisc("BulletHole", holeColor);
            go.transform.SetPositionAndRotation(point + normal * 0.0015f, Quaternion.LookRotation(normal));
            go.transform.localScale = Vector3.one * size;
            go.SetActive(true);
            holes.Enqueue(go);
        }

        // A flat disc of diameter 1, used for bullet holes and the aim marker
        public static GameObject CreateDisc(string name, Color color)
        {
            if (discMesh == null) discMesh = BuildDisc(16);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = discMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = GetMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            return go;
        }

        // Plain untextured material using a built-in shader that exists in every build
        public static Material GetMaterial()
        {
            if (sharedMaterial == null)
            {
                sharedMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                sharedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                sharedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                sharedMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                sharedMaterial.SetInt("_ZWrite", 0);
            }
            return sharedMaterial;
        }

        static Mesh BuildDisc(int segments)
        {
            var verts = new Vector3[segments + 1];
            var colors = new Color[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            colors[0] = Color.white;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f;
                colors[i + 1] = Color.white;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 1 < segments ? i + 2 : 1;
            }
            var m = new Mesh { name = "Disc", vertices = verts, triangles = tris, colors = colors };
            m.RecalculateBounds();
            return m;
        }

        // ---------- Bullet path line (debug) ----------
        void DrawPath(List<Vector3> path)
        {
            var go = new GameObject("BulletPath");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = GetMaterial();
            lr.useWorldSpace = true;
            lr.positionCount = path.Count;
            lr.SetPositions(path.ToArray());
            lr.widthMultiplier = 0.004f;
            lr.startColor = lr.endColor = pathColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Destroy(go, pathLifetime);
        }
    }
}
