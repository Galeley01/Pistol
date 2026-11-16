using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Items;
using Pistol.Player;
using Pistol.PostFx;
using Pistol.Weapons.Revolver;

namespace Pistol.Weapons
{
    // Aiming on RMB. Goes on Revolver (inside Main Camera).
    // The weapon moves smoothly to the center in front of the eye, the camera zooms slightly,
    // and Depth of Field focuses the eye according to the focus mode.
    [RequireComponent(typeof(ItemRig))]
    public class WeaponAim : MonoBehaviour
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: прицеливание"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        AimProfile profile;
        public AimProfile Profile => profile;

        public enum FocusMode { FrontSight, Far, HandsAndSights }

        // TODO(stage 5): post-processing settings below move to CameraEffects
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
        PlayerContext context;
        RevolverFire revolver;   // TODO(stage 6): ask the held item whether it can aim
        ItemRig rig;
        PlayerMovement movement;
        float aim, baseFov = -1f, currentAimIn;
        WeaponSway sway;
        PostProcessVolume dofVolume;
        DepthOfField dof;
        EdgeBlur edge;
        Vector3 hip;                 // hip pose used at runtime (never written back to the profile)
        bool postFxSuppressed;

        void Start()
        {
            if (profile == null) { Debug.LogError("WeaponAim: no profile assigned.", this); enabled = false; return; }
            cam = GetComponentInParent<Camera>();
            revolver = GetComponent<RevolverFire>();
            rig = ItemRig.On(this);
            movement = GetComponentInParent<PlayerMovement>();
            context = GetComponentInParent<PlayerContext>();   // TODO(stage 6): passed in on equip
            sway = GetComponent<WeaponSway>();
            // Hip pose: the position the weapon has in the scene/prefab, or the one from the profile
            hip = profile.useScenePosition ? transform.localPosition : profile.hipPosition;
            transform.localPosition = hip;
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

            // The path to the face is an arc from below with a roll halfway, not a straight line
            float mid = Mathf.Sin(t * Mathf.PI);
            transform.localPosition = Vector3.Lerp(hip, profile.aimPosition, t) + Vector3.down * (profile.transitionDip * mid);
            transform.localRotation = Quaternion.Euler(0f, 0f, profile.transitionRoll * mid);
            if (movement != null) movement.SetFreeAimScale(1f - t);   // the deadzone disappears while aiming, weapon centered
            if (profile.changeFov) cam.fieldOfView = Mathf.Lerp(baseFov, profile.aimFov, t);

            UpdateBlur(t);
        }

        // 0 = rested, 1 = exhausted (panting after a sprint)
        float Fatigue() => movement != null ? movement.Fatigue : 0f;

        void UpdateBlur(float t)
        {
            bool useDof = blurWeapon && !postFxSuppressed, useEdge = edgeBlur && !postFxSuppressed;
            if (!useDof && !useEdge)
            {
                if (dofVolume) { dofVolume.weight = 0f; dofVolume.enabled = false; }
                return;
            }
            if (dofVolume == null && !CreateAimVolume()) return;

            // Eye focus (Depth of Field)
            dof.enabled.value = useDof;
            dof.focusDistance.value = CurrentFocusDistance();
            dof.aperture.value = Mathf.Lerp(22f, 2.8f, blurStrength);   // smaller aperture = stronger blur
            dof.focalLength.value = Mathf.Lerp(18f, 70f, blurStrength);
            dof.kernelSize.value = blurQuality;

            // Edge blur: the sharp zone is narrower when tired
            edge.enabled.value = useEdge;
            edge.intensity.value = edgeBlurIntensity;
            edge.radius.value = Mathf.Max(0f, edgeBlurRadius - tiredTunnel * Fatigue());
            edge.softness.value = edgeBlurSoftness;
            edge.spread.value = edgeBlurSpread;

            dofVolume.weight = t;
            dofVolume.enabled = t > 0.001f;
        }

        // A separate global Volume for aiming (focus + edges); its weight rises smoothly while aiming
        bool CreateAimVolume()
        {
            var layer = cam.GetComponent<PostProcessLayer>();
            if (layer == null)
            {
                Debug.LogWarning("WeaponAim: no Post-process Layer on the camera, blur disabled.");
                blurWeapon = false; edgeBlur = false;
                return false;
            }
            int mask = layer.volumeLayer.value, layerIndex = 0;
            while (layerIndex < 32 && (mask & (1 << layerIndex)) == 0) layerIndex++;
            if (layerIndex >= 32) { blurWeapon = false; edgeBlur = false; return false; }

            if (Shader.Find("Hidden/Custom/EdgeBlur") == null && edgeBlur)
                Debug.LogWarning("WeaponAim: EdgeBlur.shader not found. Add it to the project, otherwise edges won't blur.");

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

        // ---------- Buttons (write into the profile asset, with Undo) ----------
        bool UsesScenePosition => profile != null && profile.useScenePosition;

        [Button("Запомнить текущее положение как обычное"), HideIf("UsesScenePosition")]
        void CaptureHip() => ChangeProfile("Capture Hip Position", p => p.hipPosition = transform.localPosition);

        [Button("Запомнить текущее положение как прицел")]
        void CaptureAim() => ChangeProfile("Capture Aim Position", p => p.aimPosition = transform.localPosition);

        [Button("Поставить целик и мушку в центр экрана")]
        void AlignSights()
        {
            var s = GetComponent<WeaponSway>();
            Vector3 rear = s != null ? s.rearSightPoint : new Vector3(0f, 0.055f, 0.01f);
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

        // Debug overlay: temporarily switch aim blur off without touching the settings
        public void SetPostFxSuppressed(bool suppressed) => postFxSuppressed = suppressed;

        // The eye focuses on the front sight (muzzle) or far away
        float CurrentFocusDistance()
        {
            if (focusMode == FocusMode.Far || cam == null) return farFocusDistance;
            if (focusMode == FocusMode.HandsAndSights)
            {
                // Between the palms (grip point) and the front sight; everything closer to the eye (forearms) goes out of focus
                Transform hand = rig != null ? rig.RightHandGrip : null;
                Vector3 palms = hand != null ? hand.position : transform.position;
                float dHands = Vector3.Distance(cam.transform.position, palms);
                float dFront = Vector3.Distance(cam.transform.position, FrontSightWorld());
                return Mathf.Max(0.1f, Mathf.Lerp(dHands, dFront, focusBias));
            }
            return Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, FrontSightWorld()));
        }

        // TODO(stage 4): front and rear sight become explicit ItemRig points
        Vector3 FrontSightWorld()
        {
            Transform m = rig != null ? rig.Muzzle : null;
            return m != null ? m.position + transform.up * 0.012f : transform.position + transform.forward * 0.25f;
        }
    }
}
