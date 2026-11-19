using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // A pane of glass (a car window, a headlight) that breaks under fire.
    //   - Every hit leaves a crack web around a hole and throws a few chips back; the bullet flies on through.
    //   - Tempered glass (side and rear windows, headlights) falls apart after the first hit, a moment later so the
    //     crack is seen: the pane disappears and a burst of glass shards flies along the bullet, falls, bounces off
    //     the ground and fades out after a while.
    //   - Laminated glass (the windshield) holds and collects cracks; after "hits to shatter" it falls apart too.
    // The crack sound comes from the glass Physics Material (SurfaceTable); the shatter sound is given to BulletSystem
    // with the hit that breaks the pane (IHitSound), timed with the shatter.
    // Shards are particles with small glass meshes, created on the first hit (nothing runs before that).
    // Reset (T) makes the pane whole again. Set up by Pistol > Setup Car Damage.
    [DisallowMultipleComponent]
    public class GlassPane : ShootingTarget, IBulletPassThrough, IHitSound
    {
        public enum Kind { Tempered, Laminated }

        [BoxGroup("Стекло"), SerializeField, Tooltip("Закалённое (боковые, задние, фары) — осыпается целиком; триплекс (лобовое) — держит и копит трещины")]
        Kind kind = Kind.Tempered;
        [BoxGroup("Стекло"), SerializeField, Range(1, 20), Tooltip("После скольких попаданий стекло осыпается")]
        int hitsToShatter = 1;
        [BoxGroup("Стекло"), SerializeField, Range(0f, 0.5f), Tooltip("Задержка осыпания после попадания, сек: успевает мелькнуть трещина")]
        float shatterDelay = 0.08f;

        [BoxGroup("Трещины"), SerializeField, Tooltip("След попадания: паутина трещин (атлас)")]
        ImpactDecal crack;
        [BoxGroup("Трещины"), SerializeField, Range(1, 32), Tooltip("Сколько трещин держит одно стекло (старые переиспользуются)")]
        int maxCracks = 12;

        [BoxGroup("Осколки"), SerializeField, Tooltip("Модели осколков")]
        Mesh[] shardMeshes = new Mesh[0];
        [BoxGroup("Осколки"), SerializeField, Tooltip("Материал осколков (Particles/Standard Surface, прозрачный)")]
        Material shardMaterial;
        [BoxGroup("Осколки"), SerializeField, Range(0, 400), Tooltip("Сколько осколков при осыпании")]
        int shards = 120;
        [BoxGroup("Осколки"), SerializeField, Range(0, 30), Tooltip("Сколько мелкой крошки вылетает назад при каждом попадании")]
        int chipsPerHit = 8;
        [BoxGroup("Осколки"), SerializeField, Tooltip("Размер осколка, м (от … до)")]
        Vector2 shardSize = new Vector2(0.012f, 0.045f);
        [BoxGroup("Осколки"), SerializeField, Tooltip("Скорость осколков по ходу пули, м/с (от … до)")]
        Vector2 shardSpeed = new Vector2(1.2f, 4f);
        [BoxGroup("Осколки"), SerializeField, Range(1f, 30f), Tooltip("Сколько секунд осколки лежат, прежде чем исчезнуть")]
        float shardLifetime = 8f;

        [BoxGroup("Звук"), SerializeField, Tooltip("Звук осыпания стекла")]
        SoundEvent shatterSound;

        Renderer paneRenderer;
        Collider paneCollider;
        MeshFilter paneFilter;
        ParticleSystem burst;
        class Crack { public Transform transform; public MeshFilter filter; public MeshRenderer renderer; }
        readonly List<Crack> cracks = new List<Crack>();
        int hits, nextCrack;
        bool broken, shatterPending;
        Vector3 lastDirection = Vector3.forward;
        bool shatterStarted;   // this hit started the shatter: its sound goes with the hit (HitSound)

        static bool warmed;
        static ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[256];

        bool HasCrack => crack != null && crack.material != null && crack.Count > 0;

        void Awake()
        {
            paneRenderer = GetComponent<Renderer>();
            paneCollider = GetComponent<Collider>();
            paneFilter = GetComponent<MeshFilter>();
        }

        void Start()
        {
            if (!warmed) { warmed = true; StartCoroutine(WarmUp()); }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            warmed = false;   // a reloaded scene warms up again
        }

        // A build (Metal especially) compiles a shader the first time it is drawn: the first shatter froze for a moment.
        // At the start a tiny shard and a tiny crack are drawn once right in front of the camera, out of sight in practice.
        System.Collections.IEnumerator WarmUp()
        {
            yield return null;   // the camera and the player are set up
            var cam = Camera.main;
            var system = Burst();
            if (cam == null || system == null) yield break;
            Vector3 at = cam.transform.position + cam.transform.forward * (cam.nearClipPlane + 0.05f);
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, applyShapeToPosition = false, startSize = 0.0003f, startLifetime = 0.2f, velocity = Vector3.zero
            }, 1);

            GameObject crackProbe = null;
            if (HasCrack)
            {
                crackProbe = new GameObject("Crack warm-up", typeof(MeshFilter), typeof(MeshRenderer));
                crackProbe.GetComponent<MeshFilter>().sharedMesh = crack.MeshFor(0);
                crackProbe.GetComponent<MeshRenderer>().sharedMaterial = crack.material;
                crackProbe.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-cam.transform.forward));
                crackProbe.transform.localScale = Vector3.one * 0.0003f;
            }
            yield return null;
            yield return null;
            if (crackProbe != null) Destroy(crackProbe);
        }

        // The bullet always flies on: through the glass, into the car. A broken pane has no collider.
        public bool PassesThrough(in BulletHit hit, out bool counts)
        {
            counts = !broken;
            return true;
        }

        protected override void OnHit(in BulletHit hit)
        {
            if (broken) return;
            hits++;
            lastDirection = hit.direction.sqrMagnitude > 1e-6f ? hit.direction.normalized : transform.forward;
            AddCrack(hit);
            Chips(hit.point, -lastDirection);
            if (hits >= hitsToShatter && !shatterPending)
            {
                shatterPending = true;
                shatterStarted = true;
                Invoke(nameof(Shatter), shatterDelay);
            }
        }

        // Asked by BulletSystem right after the hit: the shatter sound when this hit breaks the glass, timed with it
        public bool HitSound(in BulletHit hit, out SoundEvent sound, out Vector3 position, out float delay)
        {
            sound = shatterSound;
            position = paneRenderer != null ? paneRenderer.bounds.center : transform.position;
            delay = shatterDelay;
            bool play = shatterStarted;
            shatterStarted = false;
            return play;
        }

        // ---------- Cracks ----------
        void AddCrack(in BulletHit hit)
        {
            if (!HasCrack) return;
            Crack c;
            if (cracks.Count < maxCracks)
            {
                var go = new GameObject("Crack", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);   // the pane's scale is uniform: the crack keeps its shape
                c = new Crack { transform = go.transform, filter = go.GetComponent<MeshFilter>(), renderer = go.GetComponent<MeshRenderer>() };
                c.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.renderer.receiveShadows = false;
                cracks.Add(c);
            }
            else c = cracks[nextCrack++ % cracks.Count];

            c.filter.sharedMesh = crack.MeshFor(Random.Range(0, crack.Count));
            c.renderer.sharedMaterial = crack.material;
            float size = crack.RandomSize(hit.ammo != null ? hit.ammo.holeSize : 0.009f);
            // Facing the shooter, just in front of the glass, turned at random
            Vector3 normal = Vector3.Dot(hit.normal, lastDirection) < 0f ? hit.normal : -hit.normal;
            c.transform.SetPositionAndRotation(hit.point + normal * 0.002f,
                Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, Random.value * 360f));
            c.transform.localScale = Vector3.one * (size / Mathf.Max(transform.lossyScale.x, 1e-6f));
            c.transform.gameObject.SetActive(true);
        }

        void HideCracks()
        {
            foreach (var c in cracks) if (c.transform != null) c.transform.gameObject.SetActive(false);
        }

        // ---------- Shattering ----------
        void Shatter()
        {
            shatterPending = false;
            if (broken) return;
            broken = true;
            if (paneRenderer != null) paneRenderer.enabled = false;
            if (paneCollider != null) paneCollider.enabled = false;
            HideCracks();

            var system = Burst();
            if (system != null && shards > 0)
            {
                var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
                int before = system.particleCount;
                system.Emit(emit, shards);
                // Along the bullet with a spread, a little outwards from the glass; tumbling. Only the new shards
                // are read back (they come after the ones already flying), into a buffer shared by all panes.
                int added = system.particleCount - before;
                if (particleBuffer.Length < added) particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(added)];
                int count = system.GetParticles(particleBuffer, added, before);
                for (int i = 0; i < count; i++)
                {
                    particleBuffer[i].velocity = lastDirection * Random.Range(shardSpeed.x, shardSpeed.y) + Random.insideUnitSphere * 1.2f;
                    particleBuffer[i].startSize = Random.Range(shardSize.x, shardSize.y);
                    particleBuffer[i].rotation3D = new Vector3(Random.value, Random.value, Random.value) * 360f;
                }
                system.SetParticles(particleBuffer, count, before);
            }

        }

        // A few small chips thrown back towards the shooter on every hit
        void Chips(Vector3 point, Vector3 back)
        {
            var system = chipsPerHit > 0 ? Burst() : null;
            if (system == null) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < chipsPerHit; i++)
            {
                emit.position = point;
                emit.velocity = (back + Random.insideUnitSphere * 0.6f) * Random.Range(1f, 3f);
                emit.startSize = Random.Range(shardSize.x * 0.4f, shardSize.x);
                emit.rotation3D = new Vector3(Random.value, Random.value, Random.value) * 360f;
                system.Emit(emit, 1);
            }
        }

        // The particle system for this pane, made on first use: shards emitted over the pane's mesh, world space,
        // gravity, bouncing off the world, fading out at the end of their life
        ParticleSystem Burst()
        {
            if (burst != null) return burst;
            if (shardMaterial == null || shardMeshes == null || shardMeshes.Length == 0 || paneFilter == null) return null;

            var go = new GameObject("Shards");
            go.transform.SetParent(transform, false);
            burst = go.AddComponent<ParticleSystem>();
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = burst.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startLifetime = new ParticleSystem.MinMaxCurve(shardLifetime * 0.8f, shardLifetime * 1.2f);
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.startRotation3D = true;
            main.gravityModifier = 1f;
            main.maxParticles = Mathf.Max(64, shards + chipsPerHit * hitsToShatter + 32);

            var emission = burst.emission;
            emission.enabled = false;

            var shape = burst.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.mesh = paneFilter.sharedMesh;

            var rotation = burst.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            // Each shard tumbles its own way while flying, and stops once it has settled
            var spinMax = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.12f, 0.15f), new Keyframe(0.3f, 0f), new Keyframe(1f, 0f));
            var spinMin = new AnimationCurve(new Keyframe(0f, -1f), new Keyframe(0.12f, -0.15f), new Keyframe(0.3f, 0f), new Keyframe(1f, 0f));
            var spin = new ParticleSystem.MinMaxCurve(Mathf.Deg2Rad * 900f, spinMin, spinMax);
            rotation.x = spin;
            rotation.y = spin;
            rotation.z = spin;

            var collision = burst.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            collision.radiusScale = 0.5f;
            collision.enableDynamicColliders = false;

            var colour = burst.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.SetMeshes(shardMeshes);
            renderer.sharedMaterial = shardMaterial;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Plain mesh particles: with GPU instancing the shader reads per-shard data in its own layout, and a
            // mismatch turns shards into huge coloured planes across the scene. A few hundred shards don't need it.
            renderer.enableGPUInstancing = false;
            burst.Play();
            return burst;
        }

        // ---------- Reset ----------
        public override void ResetTarget()
        {
            CancelInvoke(nameof(Shatter));
            shatterPending = shatterStarted = false;
            broken = false;
            hits = 0;
            nextCrack = 0;
            if (paneRenderer != null) paneRenderer.enabled = true;
            if (paneCollider != null) paneCollider.enabled = true;
            HideCracks();
            if (burst != null) burst.Clear();
        }

        // Set by Pistol > Setup Car Damage
        public void Configure(Kind kind, int hitsToShatter, int shards, ImpactDecal crack, Mesh[] shardMeshes,
                              Material shardMaterial, SoundEvent shatterSound)
        {
            this.kind = kind;
            this.hitsToShatter = hitsToShatter;
            this.shards = shards;
            this.crack = crack;
            this.shardMeshes = shardMeshes;
            this.shardMaterial = shardMaterial;
            this.shatterSound = shatterSound;
        }
    }
}
