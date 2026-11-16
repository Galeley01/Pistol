using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Items;
using Pistol.Player;
using Pistol.PostFx;
using Pistol.Weapons.Revolver;

namespace Pistol.Weapons
{
    // Aiming on RMB. Goes on the weapon root (Revolver), held by ItemHolderWithPose.
    // The weapon moves smoothly to the center in front of the eye, the camera zooms slightly,
    // and Depth of Field focuses the eye according to the focus mode.
    // Pose layer (base step): gives the hip / aim pose of the weapon and publishes the aim weight.
    [RequireComponent(typeof(ItemRig))]
    public class WeaponAim : MonoBehaviour, IPoseLayer
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: прицеливание"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        AimProfile profile;
        public AimProfile Profile => profile;

        [BoxGroup("Эффекты экрана"), SerializeField]
        [Tooltip("Фокус глаз и размытие краёв в прицеле. Где именно фокус — в профиле прицела (Фокус глаз в прицеле). Пусто — без эффектов")]
        ScreenEffectsProfile effects;

        [BoxGroup("Настройка"), Tooltip("Держать прицел без ПКМ — удобно, чтобы подбирать положение в Play Mode")]
        public bool forceAim;

        [ShowNativeProperty] public float AimAmount => aim;
        public bool IsAiming => aim > 0.5f;
        [ShowNativeProperty] public float CurrentAimInTime => currentAimIn;

        Camera cam;
        PlayerContext context;
        RevolverFire revolver;   // TODO(stage 6): ask the held item whether it can aim
        ItemRig rig;
        PlayerMovement movement;
        float aim, baseFov = -1f, currentAimIn;
        WeaponSway sway;
        CameraEffects cameraEffects;
        ScreenEffectsRequest effectsRequest;
        Vector3 hip;                 // hip pose used at runtime (never written back to the profile)
        float aimT, aimArc;          // smoothed aim (0..1) and the arc to the face (0 at the ends, 1 halfway)

        // Default front sight above the muzzle when ItemRig has no Front Sight
        const float DefaultFrontSightHeight = 0.012f;

        public int PoseOrder => Items.PoseOrder.Base;

        public void Bind(ItemContext pose)
        {
            cam = pose.Camera;
            movement = pose.Movement;
            context = pose.Player;
            cameraEffects = pose.Effects;
        }

        void Start()
        {
            if (profile == null) { Debug.LogError("WeaponAim: no profile assigned.", this); enabled = false; return; }
            if (cam == null) { Debug.LogError("WeaponAim: not held by an ItemHolderWithPose, aiming is off.", this); enabled = false; return; }
            revolver = GetComponent<RevolverFire>();
            rig = ItemRig.On(this);
            sway = GetComponent<WeaponSway>();
            // Hip pose: the position the weapon has in the scene/prefab, or the one from the profile
            hip = profile.useScenePosition ? transform.localPosition : profile.hipPosition;
        }

        public void ApplyPose(ref PoseFrame frame)
        {
            frame.Aim = aim;
            // The path to the face is an arc from below with a roll halfway, not a straight line
            frame.BasePosition = Vector3.Lerp(hip, profile.aimPosition, aimT) + Vector3.down * (profile.transitionDip * aimArc);
            frame.BaseRotation = Quaternion.Euler(0f, 0f, profile.transitionRoll * aimArc);
        }

        void Update()
        {
            if (cam == null) return;
            if (baseFov < 0f) baseFov = cam.fieldOfView;   // read the FOV after CameraMotion has set it

            bool cylinderOpen = revolver != null && revolver.IsOpen;
            bool want = (forceAim || (context != null && context.AimHeld)) && !cylinderOpen;

            // The more exhausted the character, the slower the weapon comes up to the face
            float fatigue = Fatigue();
            currentAimIn = Mathf.Lerp(profile.aimInTime, profile.tiredAimInTime, fatigue)
                         + (movement != null && movement.IsRunning ? profile.fromSprintExtraTime : 0f);
            float prevAim = aim;
            float time = want ? currentAimIn : profile.aimOutTime;
            aim = Mathf.MoveTowards(aim, want ? 1f : 0f, Time.deltaTime / Mathf.Max(time, 0.01f));
            float t = Mathf.SmoothStep(0f, 1f, aim);

            // Weapon reached the eye: a small "into the shoulder" kick, stronger and less even when tired
            if (prevAim < 1f && aim >= 1f && sway != null)
            {
                float f = profile.settleKick * Mathf.Lerp(1f, 2f, fatigue);
                sway.AddKick(new Vector3(f, Random.Range(-0.4f, 0.4f) * f, Random.Range(-0.6f, 0.6f) * f),
                             new Vector3(0f, -0.02f, -0.02f) * (f / 10f));
            }

            aimT = t;
            aimArc = Mathf.Sin(t * Mathf.PI);   // the pose itself is given in ApplyPose
            if (movement != null) movement.SetFreeAimScale(1f - t);   // the deadzone disappears while aiming, weapon centered
            if (profile.changeFov) cam.fieldOfView = Mathf.Lerp(baseFov, profile.aimFov, t);

            UpdateBlur(t);
        }

        // 0 = rested, 1 = exhausted (panting after a sprint)
        float Fatigue() => movement != null ? movement.Fatigue : 0f;

        // Eye focus and edge blur: a request to CameraEffects, its strength follows the aim
        void UpdateBlur(float t)
        {
            if (effectsRequest == null)
            {
                if (effects == null || cameraEffects == null) return;
                effectsRequest = cameraEffects.CreateRequest(effects, "Aim");
            }
            effectsRequest.Set(t, CurrentFocusDistance(), profile.tiredTunnel * Fatigue());
        }

        void OnDestroy() => effectsRequest?.Release();

        // ---------- Buttons (write into the profile asset, with Undo) ----------
        bool UsesScenePosition => profile != null && profile.useScenePosition;

        [Button("Запомнить текущее положение как обычное"), HideIf("UsesScenePosition")]
        void CaptureHip() => ChangeProfile("Capture Hip Position", p => p.hipPosition = transform.localPosition);

        [Button("Запомнить текущее положение как прицел")]
        void CaptureAim() => ChangeProfile("Capture Aim Position", p => p.aimPosition = transform.localPosition);

        [Button("Поставить целик и мушку в центр экрана")]
        void AlignSights()
        {
            var r = GetComponent<ItemRig>();
            if (r == null || r.RearSight == null) { Debug.LogWarning("WeaponAim: assign the Rear Sight in ItemRig first.", this); return; }
            Vector3 rear = r.LocalPoint(r.RearSight, Vector3.zero);
            ChangeProfile("Align Sights", p => p.aimPosition = new Vector3(-rear.x, -rear.y, p.aimDistance - rear.z));
            if (profile != null) Debug.Log("Aim Position = " + profile.aimPosition.ToString("F3"));
        }

        void ChangeProfile(string undoName, System.Action<AimProfile> change)
        {
            if (profile == null) { Debug.LogWarning("WeaponAim: assign a profile first.", this); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(profile, undoName);
#endif
            change(profile);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(profile);
#endif
        }

        // The eye focuses on the front sight (muzzle) or far away
        float CurrentFocusDistance()
        {
            if (profile.focusMode == AimFocusMode.Far || cam == null) return profile.farFocusDistance;
            if (profile.focusMode == AimFocusMode.HandsAndSights)
            {
                // Between the palms (grip point) and the front sight; everything closer to the eye (forearms) goes out of focus
                Transform hand = rig != null ? rig.RightHandGrip : null;
                Vector3 palms = hand != null ? hand.position : transform.position;
                float dHands = Vector3.Distance(cam.transform.position, palms);
                float dFront = Vector3.Distance(cam.transform.position, FrontSightWorld());
                return Mathf.Max(0.1f, Mathf.Lerp(dHands, dFront, profile.focusBias));
            }
            return Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, FrontSightWorld()));
        }

        Vector3 FrontSightWorld()
        {
            if (rig != null && rig.FrontSight != null) return rig.FrontSight.position;
            Transform m = rig != null ? rig.Muzzle : null;
            return m != null ? m.position + transform.up * DefaultFrontSightHeight : transform.position + transform.forward * 0.25f;
        }
    }
}
