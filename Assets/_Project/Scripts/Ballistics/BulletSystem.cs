using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Core;

namespace Pistol.Ballistics
{
    // Bullet flight. A bullet is a projectile, not an instant ray: every frame it travels one segment
    // at its own speed and drops under gravity. Hits are checked with a ray along that segment,
    // so bullets never pass through walls. No Rigidbody or collider is needed for bullets.
    // Bullet holes follow what was hit: they move with a falling box and disappear with a destroyed one.
    // Every hit leaves the mark of the surface (SurfaceTable: a concrete crater...; a plain dark dot when none)
    // and sounds at the hit point: the sound of the collider's Physics Material (SurfaceTable),
    // or of an ImpactSurface on the object; the hit angle changes it and it arrives after the time the sound
    // needs to come back along the bullet's path.
    //
    // Goes on its own object in the scene; held weapons get it through ItemContext.
    [DisallowMultipleComponent]
    public class BulletSystem : MonoBehaviour
    {
        [BoxGroup("Попадания"), Tooltip("Слои, в которые попадают пули")]
        public LayerMask hitMask = ~0;
        [BoxGroup("Попадания"), Range(10, 500), Tooltip("Сколько отметин от пуль держать одновременно (старые исчезают)")]
        public int maxHoles = 100;
        [BoxGroup("Попадания")] public Color holeColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);

        [BoxGroup("Звук попаданий"), SerializeField, Tooltip("Звук игры. Пусто — попадания беззвучны")]
        GameAudio gameAudio;
        [BoxGroup("Звук попаданий"), SerializeField, Tooltip("Какой звук у какой поверхности (по Physics Material коллайдера) и звук по умолчанию")]
        SurfaceTable surfaceSounds;
        [BoxGroup("Следы"), SerializeField, Tooltip("Стили следа для сравнения (DebugSettings → Bullet Hole Style), по порядку: " +
                                                    "фото, скан-наклейка вход, выход, скан-воронка вход, выход, скан с глубиной вход, выход")]
        ImpactDecal[] holeStyles = new ImpactDecal[0];

        [BoxGroup("Отладка"), SerializeField, Tooltip("Отладочные флаги: пути пуль и логи попаданий включаются там")]
        DebugSettings debug;
        [BoxGroup("Отладка"), Range(0.5f, 20f), Tooltip("Сколько секунд держать линию пути пули")]
        public float pathLifetime = 4f;
        [BoxGroup("Отладка")] public Color pathColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        bool ShowPaths => debug != null && debug.BulletPaths;
        bool LogHits => debug != null && debug.BulletHits;

        class Bullet
        {
            public Vector3 pos, vel;
            public float age, travelled;
            public AmmoType ammo;
            public Transform ignoreRoot;
            public List<Vector3> path;
        }

        readonly List<Bullet> bullets = new List<Bullet>();
        class Hole
        {
            public Transform transform;
            public Transform surface;          // what was hit
            public bool follows;               // false for static geometry: it never moves
            public Vector3 localPosition;      // pose relative to the surface, scale ignored
            public Quaternion localRotation;
        }

        readonly Queue<Hole> holes = new Queue<Hole>();
        Transform holeRoot;
        static RaycastHit[] hitBuffer = new RaycastHit[16];

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
                path = ShowPaths ? new List<Vector3> { origin } : null
            };
            bullets.Add(b);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                BulletMath.Step(b.pos, b.vel, BulletMath.Gravity(b.ammo), dt, out Vector3 newPos, out Vector3 newVel);
                Vector3 seg = newPos - b.pos;

                // A surface that lets bullets through (a cardboard target) is crossed and the ray goes on
                Vector3 from = b.pos;
                float left = seg.magnitude;
                Vector3 dir = left > 0f ? seg / left : b.vel.normalized;
                bool stopped = false;
                int pass = 0;
                for (; pass < MaxPassThrough && Raycast(from, dir, left, hitMask, b.ignoreRoot, out RaycastHit hit); pass++)
                {
                    b.travelled += hit.distance;
                    var through = hit.collider.GetComponentInParent<IBulletPassThrough>();
                    if (through is Object throughObj ? throughObj != null : through != null)
                    {
                        var info = MakeHit(b, hit, newVel.magnitude);
                        if (through.PassesThrough(info, out bool counts))
                        {
                            if (counts) HitThrough(info, hit, b.travelled);
                            float step = hit.distance + PassThroughStep;
                            from += dir * step;
                            left -= step;
                            b.travelled += PassThroughStep;
                            if (left <= 0f) break;
                            continue;
                        }
                    }
                    b.path?.Add(hit.point);
                    OnHit(b, hit, newVel.magnitude);
                    Finish(i);
                    stopped = true;
                    break;
                }
                // Too many cards crossed in one frame: whatever comes next stops the bullet, it never flies through a wall
                if (!stopped && pass == MaxPassThrough && left > 0f && Raycast(from, dir, left, hitMask, b.ignoreRoot, out RaycastHit last))
                {
                    b.travelled += last.distance;
                    b.path?.Add(last.point);
                    OnHit(b, last, newVel.magnitude);
                    Finish(i);
                    stopped = true;
                }
                if (stopped) continue;

                b.travelled += left > 0f ? left : 0f;
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
            // The buffer gets hits in no particular order: when it is full the nearest one may be missing, so grow it and ask again
            int n = Physics.RaycastNonAlloc(from, dir.normalized, hitBuffer, dist, mask, QueryTriggerInteraction.Ignore);
            while (n == hitBuffer.Length && hitBuffer.Length < MaxHitBuffer)
            {
                hitBuffer = new RaycastHit[hitBuffer.Length * 2];
                n = Physics.RaycastNonAlloc(from, dir.normalized, hitBuffer, dist, mask, QueryTriggerInteraction.Ignore);
            }
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

        const int MaxHitBuffer = 256;
        const int MaxPassThrough = 8;            // surfaces a bullet may cross in one frame
        const float PassThroughStep = 0.006f;    // past the surface (a card is a few mm thick)

        static BulletHit MakeHit(Bullet b, RaycastHit hit, float speed) => new BulletHit
        {
            point = hit.point, normal = hit.normal, direction = b.vel.normalized,
            speed = speed, distance = b.travelled, ammo = b.ammo, collider = hit.collider
        };

        // A hit on a surface the bullet goes through: the surface reacts and sounds, but no hole mark and no stop
        void HitThrough(in BulletHit info, RaycastHit hit, float travelled)
        {
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target is Object unityObj ? unityObj != null : target != null) target.TakeHit(info);
            PlayImpact(info, hit.collider, travelled);
            if (LogHits) Debug.Log($"Hit through: {hit.collider.name}, {info.distance:F1} m");
        }

        void OnHit(Bullet b, RaycastHit hit, float speed)
        {
            var info = MakeHit(b, hit, speed);

            // One push per hit: a target (IDamageable) pushes itself with its own tuned push; anything else
            // with a Rigidbody gets the bullet's momentum
            var target = hit.collider.GetComponentInParent<IDamageable>();
            bool isTarget = target is Object unityObj ? unityObj != null : target != null;
            if (!isTarget && hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(info.direction * b.ammo.bulletMass * speed, hit.point, ForceMode.Impulse);
            if (isTarget) target.TakeHit(info);

            var decal = surfaceSounds != null && (debug == null || debug.NewBulletHoles) ? surfaceSounds.DecalFor(hit.collider.sharedMaterial) : null;
            if (decal != null && surfaceSounds != null && decal == surfaceSounds.defaultDecal) decal = StyleOverride(decal);
            if (decal != null && debug != null && debug.AngledBulletHoles) decal = decal.ForAngle(Vector3.Angle(-info.direction, hit.normal));   // angled hits: an elongated mark
            PlaceHole(hit.point, hit.normal, info.direction, b.ammo.holeSize, hit.collider.transform, decal);
            PlayImpact(info, hit.collider, b.travelled);

            if (LogHits)
                Debug.Log($"Hit: {hit.collider.name}, {info.distance:F1} m, speed {speed:F0} m/s");
        }

        // Debug comparison: the default (concrete) mark replaced by the style picked in DebugSettings, or a random one
        ImpactDecal StyleOverride(ImpactDecal decal)
        {
            if (debug == null || holeStyles == null || holeStyles.Length == 0) return decal;
            int style = debug.BulletHoleStyleIndex;   // -1 = as in the surface table, -2 = random of all
            if (style == -2) return holeStyles[Random.Range(0, holeStyles.Length)] ?? decal;
            return style >= 0 && style < holeStyles.Length && holeStyles[style] != null ? holeStyles[style] : decal;
        }

        public void SetHoleStyles(ImpactDecal[] styles) => holeStyles = styles;   // Pistol > Setup Bullet Decals

        // ---------- Impact sound ----------
        void PlayImpact(in BulletHit hit, Collider collider, float travelled)
        {
            if (gameAudio == null) return;
            var surface = collider.GetComponentInParent<ImpactSurface>();
            var profile = surface != null && surface.Sound != null ? surface.Sound
                        : surfaceSounds != null ? surfaceSounds.For(collider.sharedMaterial) : null;
            if (profile == null) return;

            float incidence = Vector3.Angle(-hit.direction, hit.normal);
            profile.Evaluate(incidence, hit.speed, out float directWeight, out float glancingWeight, out float pitch, out float volume);
            float delay = profile.Delay(travelled);   // the shooter hears it after it travels back along the path

            const float Audible = 0.05f;
            if (directWeight > Audible) gameAudio.Play(profile.direct, hit.point, volume * directWeight, pitch, delay);
            if (glancingWeight > Audible) gameAudio.Play(profile.glancing, hit.point, volume * glancingWeight, pitch, delay);
        }

        // ---------- Bullet holes ----------
        // A hole is never a child of what it hit: under a stretched object (a wall is a scaled cube) a rotated
        // child gets squashed into a line. Holes live in their own unscaled container and follow the surface
        // by its position and rotation only, so they are always round and of the right size.
        void PlaceHole(Vector3 point, Vector3 normal, Vector3 direction, float size, Transform surface, ImpactDecal decal = null)
        {
            if (holeRoot == null) holeRoot = new GameObject("BulletHoles").transform;

            // Reuse the oldest hole once the limit is reached
            Hole hole = holes.Count >= maxHoles ? holes.Dequeue() : null;
            if (hole == null || hole.transform == null)
            {
                hole = new Hole { transform = CreateDisc("BulletHole", holeColor).transform };
                hole.transform.SetParent(holeRoot, false);
            }

            Vector3 position = point + normal * 0.0015f;
            Quaternion rotation = Quaternion.LookRotation(normal);
            var mf = hole.transform.GetComponent<MeshFilter>();
            var mr = hole.transform.GetComponent<MeshRenderer>();
            Mesh mesh = decal != null && (decal.material != null || decal.kind == ImpactDecal.Kind.Mesh) && decal.Count > 0
                ? decal.MeshFor(Random.Range(0, decal.Count)) : null;
            if (mesh != null)
            {
                // The surface's own mark: a random variant, size and turn around the normal
                mf.sharedMesh = mesh;
                mr.sharedMaterials = decal.MaterialsFor();
                mr.SetPropertyBlock(null);
                mr.receiveShadows = true;
                size = decal.Size(size) * (1f + Random.Range(-decal.sizeJitter, decal.sizeJitter));
                // Along the bullet's travel on the surface (an elongated mark, its +X downrange), or a random turn
                Vector3 travel = Vector3.ProjectOnPlane(direction, normal);
                if (decal.alongTravel && travel.sqrMagnitude > 1e-6f)
                    rotation = Quaternion.LookRotation(normal, Vector3.Cross(normal, travel.normalized))
                             * Quaternion.Euler(0f, 0f, Random.Range(-decal.travelJitter, decal.travelJitter));
                else if (decal.randomRotation) rotation *= Quaternion.Euler(0f, 0f, Random.value * 360f);
                rotation *= Quaternion.Euler(decal.meshRotation);
                if (decal.kind == ImpactDecal.Kind.Mesh) position = point;   // a crater mesh starts at the surface level
            }
            else
            {
                // The plain dark dot
                if (discMesh == null) discMesh = BuildDisc(16);
                mf.sharedMesh = discMesh;
                mr.sharedMaterial = GetMaterial();
                mr.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", holeColor);
                mr.SetPropertyBlock(block);
            }
            hole.transform.SetPositionAndRotation(position, rotation);
            hole.transform.localScale = Vector3.one * size;
            hole.transform.gameObject.SetActive(true);

            // Where the hole sits relative to the surface, without the surface's scale
            hole.surface = surface;
            hole.follows = surface != null && !surface.gameObject.isStatic;
            if (hole.follows)
            {
                Quaternion inverse = Quaternion.Inverse(surface.rotation);
                hole.localPosition = inverse * (position - surface.position);
                hole.localRotation = inverse * rotation;
            }
            holes.Enqueue(hole);
        }

        // After physics and animation moved the surfaces: holes follow them; a hole on a destroyed object disappears
        void LateUpdate()
        {
            foreach (var hole in holes)
            {
                if (!hole.follows || hole.transform == null) continue;
                if (hole.surface == null)
                {
                    hole.follows = false;
                    hole.transform.gameObject.SetActive(false);
                    continue;
                }
                hole.transform.SetPositionAndRotation(hole.surface.position + hole.surface.rotation * hole.localPosition,
                                                      hole.surface.rotation * hole.localRotation);
            }
        }

        void OnDestroy()
        {
            if (holeRoot != null) Destroy(holeRoot.gameObject);
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
                sharedMaterial = DebugMaterials.CreateColored(zWrite: false);
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
