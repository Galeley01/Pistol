using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;
using Pistol.Core;
using Pistol.Items;
using Pistol.Player;

namespace Pistol.Weapons.Revolver
{
    // Fires from the muzzle (ItemRig) along the barrel, plus a debug "future bullet" line.
    // Goes on Revolver. The bullet flies where the barrel actually points at the moment of the shot:
    // all weapon motion (inertia, roll, breathing, Free-Aim) is accounted for automatically.
    //
    // Zeroing: the barrel sits below the eye, so it is tilted up slightly so the bullet crosses
    // the sight line (rear sight to front sight) at the zero distance.
    [DefaultExecutionOrder(1000)]   // after the camera, so the line doesn't lag the weapon by a frame
    [RequireComponent(typeof(ItemRig))]
    public class RevolverShooter : MonoBehaviour, IItemContextUser
    {
        [BoxGroup("Ствол"), Range(5f, 50f), Tooltip("На какой дистанции пуля попадает точно в точку прицеливания, м")]
        public float zeroDistance = 15f;
        [BoxGroup("Ствол"), Range(0f, 0.04f), Tooltip("Насколько линия прицела выше оси ствола, м")]
        public float sightHeight = 0.013f;

        // The line is switched on in DebugSettings (Trajectory Preview)
        [BoxGroup("Линия будущей пули (отладка)"), Range(10f, 300f), Tooltip("Длина линии, м")]
        public float previewDistance = 80f;
        [BoxGroup("Линия будущей пули (отладка)")] public Color lineColor = new Color(1f, 0.25f, 0.2f, 0.75f);
        [BoxGroup("Линия будущей пули (отладка)")] public Color hitColor = new Color(1f, 0.2f, 0.15f, 0.95f);

        Transform ignoreRoot;
        Transform muzzle;
        AmmoType fallbackAmmo;   // used when there is no cylinder; created once
        RevolverCylinder cylinder;
        LineRenderer line;
        GameObject hitMarker;
        readonly List<Vector3> points = new List<Vector3>(64);

        BulletSystem bullets;
        DebugSettings debug;

        // The bullet ignores the player's own colliders
        public void Bind(ItemContext context)
        {
            ignoreRoot = context.Movement != null ? context.Movement.transform : transform.root;
            bullets = context.Bullets;
            debug = context.Debug;
        }

        bool ShowTrajectory => debug != null && debug.TrajectoryPreview && bullets != null;

        void Start()
        {
            var rig = ItemRig.On(this);
            muzzle = rig != null ? rig.MuzzleOrRoot : transform;
            if (rig != null && rig.Muzzle == null) Debug.LogError("RevolverShooter: ItemRig has no Muzzle, shooting from the weapon root.", this);
            cylinder = GetComponent<RevolverCylinder>();
        }

        // The debug line and its hit marker live in the scene root (world space), so they are made only
        // when the line is first switched on and are removed together with the weapon
        void CreatePreview()
        {
            var go = new GameObject("TrajectoryPreview");
            line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = BulletSystem.GetMaterial();
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.0015f), new Keyframe(1f, 0.03f));
            hitMarker = BulletSystem.CreateDisc("TrajectoryHit", hitColor);
        }

        // Where the bullet would start and go if fired now (zeroing included)
        public void GetShot(AmmoType ammo, out Vector3 origin, out Vector3 direction)
        {
            origin = muzzle != null ? muzzle.position : transform.position;

            float v = ammo != null ? ammo.muzzleVelocity : 440f;
            float g = BulletMath.Gravity(ammo).magnitude;
            float t = zeroDistance / v;
            float drop = 0.5f * g * t * t;
            float angle = Mathf.Atan2(sightHeight + drop, zeroDistance) * Mathf.Rad2Deg;
            direction = Quaternion.AngleAxis(-angle, transform.right) * transform.forward;   // slightly up
        }

        // Called from RevolverFire when the hammer strikes a live round
        public void Fire(AmmoType ammo)
        {
            if (ammo == null && cylinder != null) ammo = cylinder.Ammo;
            if (ammo == null)
            {
                if (fallbackAmmo == null) fallbackAmmo = AmmoType.CreateDefault();
                ammo = fallbackAmmo;
            }
            GetShot(ammo, out var origin, out var dir);
            if (bullets != null) bullets.Fire(origin, dir, ammo, ignoreRoot);
            else Debug.LogWarning("RevolverShooter: no BulletSystem, the shot goes nowhere.", this);
        }

        void LateUpdate()
        {
            bool show = ShowTrajectory;
            if (line == null)
            {
                if (!show) return;
                CreatePreview();
            }
            line.enabled = show;
            if (!show) { hitMarker.SetActive(false); return; }

            var ammo = cylinder != null ? cylinder.Ammo : null;
            GetShot(ammo, out var pos, out var dir);
            float speed = ammo != null ? ammo.muzzleVelocity : 440f;
            Vector3 g = BulletMath.Gravity(ammo);
            Vector3 vel = dir * speed;
            LayerMask mask = bullets.hitMask;

            // Same flight calculation as the real bullet, in small time steps
            points.Clear();
            points.Add(pos);
            float travelled = 0f;
            bool hitSomething = false;
            RaycastHit hit = default;
            float dt = Mathf.Max(0.005f, previewDistance / speed / 60f);   // ~60 points along the whole line
            while (travelled < previewDistance && points.Count < 64)
            {
                BulletMath.Step(pos, vel, g, dt, out Vector3 newPos, out Vector3 newVel);
                Vector3 seg = newPos - pos;
                if (PhysicsQuery.NearestHit(pos, seg, seg.magnitude, mask, ignoreRoot, out hit))
                {
                    points.Add(hit.point);
                    hitSomething = true;
                    break;
                }
                travelled += seg.magnitude;
                pos = newPos;
                vel = newVel;
                points.Add(pos);
            }

            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            line.startColor = lineColor;
            line.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.3f);

            hitMarker.SetActive(hitSomething);
            if (hitSomething)
            {
                float size = Mathf.Clamp((travelled + hit.distance) * 0.004f + 0.01f, 0.01f, 0.08f);   // bigger when farther, so it stays visible
                hitMarker.transform.SetPositionAndRotation(hit.point + hit.normal * 0.002f, Quaternion.LookRotation(hit.normal));
                hitMarker.transform.localScale = Vector3.one * size;
            }
        }

        [Button("Взять высоту прицела из точки целика")]
        void SightHeightFromRearSight()
        {
            var rig = GetComponent<ItemRig>();
            if (rig == null || rig.Muzzle == null || rig.RearSight == null) { Debug.LogWarning("ItemRig with Muzzle and Rear Sight is required"); return; }
            sightHeight = Mathf.Max(0f, rig.LocalPoint(rig.RearSight, Vector3.zero).y - rig.LocalPoint(rig.Muzzle, Vector3.zero).y);
            Debug.Log($"Sight height above the bore: {sightHeight * 1000f:F1} mm");
        }

        // Out of the hands: no bullets and no preview line
        public void Unbind()
        {
            bullets = null;
            debug = null;
            ignoreRoot = null;
            HidePreview();
        }

        void HidePreview()
        {
            if (line != null) line.enabled = false;
            if (hitMarker != null) hitMarker.SetActive(false);
        }

        void OnDisable() => HidePreview();

        void OnDestroy()
        {
            if (line != null) Destroy(line.gameObject);
            if (hitMarker != null) Destroy(hitMarker);
        }
    }
}
