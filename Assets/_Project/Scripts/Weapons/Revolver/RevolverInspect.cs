using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;
using Pistol.Items;
using Pistol.Player;

namespace Pistol.Weapons.Revolver
{
    // Cylinder inspect mode. Goes on Revolver (next to RevolverFire).
    //
    // Cylinder open (R) -> left Alt enters inspect mode:
    //   - the camera stops turning, the cursor appears, movement is disabled;
    //   - the weapon moves smoothly into the inspect pose (tuned in the SwayProfile inspect pose foldout;
    //     this component publishes how far we are in it as a pose layer, WeaponSway applies the pose);
    //   - the screen edges get darker;
    //   - the eyes focus on the cylinder: distant objects blur slightly (Depth of Field).
    // Alt again or Esc leaves inspect mode (the cylinder stays open).
    // R during inspect leaves it and closes the cylinder.
    //
    // Chamber picking lives in RevolverChamberPicker; loading and unloading by hand will build on this mode.
    [RequireComponent(typeof(RevolverFire))]
    public class RevolverInspect : MonoBehaviour, IPoseLayer
    {
        [BoxGroup("Переход"), Range(0.1f, 1.5f), Tooltip("За сколько секунд оружие поднимается в позу осмотра")]
        public float inTime = 0.45f;
        [BoxGroup("Переход"), Range(0.1f, 1.5f), Tooltip("За сколько секунд возвращается обратно")]
        public float outTime = 0.35f;

        [BoxGroup("Затемнение краёв"), Range(0f, 1f), Tooltip("Насколько темнеют края экрана в режиме осмотра")]
        public float vignetteIntensity = 0.42f;
        [BoxGroup("Затемнение краёв"), Range(0.01f, 1f), Tooltip("Плавность перехода от центра к тёмным краям")]
        public float vignetteSmoothness = 0.45f;
        [BoxGroup("Затемнение краёв"), Range(0f, 1f), Tooltip("1 — круглое затемнение, меньше — по форме экрана")]
        public float vignetteRoundness = 1f;

        [BoxGroup("Фокус на барабане"), Tooltip("Размывать дальние предметы, пока глаза смотрят на барабан")]
        public bool focusBlur = true;
        [BoxGroup("Фокус на барабане"), ShowIf("focusBlur"), Range(0f, 1f), Tooltip("Сила размытия дальних предметов. 0.3 — чуть-чуть, 1 — сильно")]
        public float blurStrength = 0.3f;
        [BoxGroup("Фокус на барабане"), ShowIf("focusBlur"), Tooltip("Качество размытия. Medium — обычно достаточно, Large/VeryLarge дороже для видеокарты")]
        public KernelSize blurQuality = KernelSize.Medium;
        [BoxGroup("Фокус на барабане"), ShowIf("focusBlur"), Range(-0.2f, 0.5f), Tooltip("Сдвиг фокуса дальше (+) или ближе (−) от барабана, м")]
        public float focusOffset = 0f;

        [BoxGroup("Отладка")] public bool logEvents = true;

        [ShowNativeProperty] public bool IsInspecting => inspecting;
        [ShowNativeProperty] public float Amount => amount;

        // Raised on entering (true) and leaving (false) inspect mode; used by RevolverChamberPicker
        public event System.Action<bool> InspectChanged;

        RevolverFire fire;
        PlayerContext context;
        Camera cam;
        bool inspecting;
        float amount;

        PostProcessVolume volume, focusVolume;
        Vignette vignette;
        DepthOfField dof;
        Transform focusTarget;
        bool volumeFailed;

        public int PoseOrder => Items.PoseOrder.State;

        public void Bind(PoseContext pose)
        {
            context = pose.Player;
            cam = pose.Camera;
        }

        public void ApplyPose(ref PoseFrame frame) => frame.Inspect = amount;

        void Start()
        {
            fire = GetComponent<RevolverFire>();
            if (context == null) Debug.LogError("RevolverInspect: not held by an ItemHolderWithPose, inspect mode is off.", this);
            var cyl = GetComponent<RevolverCylinder>();
            focusTarget = cyl != null && cyl.CylinderTransform != null ? cyl.CylinderTransform : transform;
        }

        void Update()
        {
            if (context == null) return;

            if (!inspecting)
            {
                // Alt works only in Gameplay mode (cursor captured) and only with the cylinder open and still
                if (context.InspectPressed && fire.IsOpenIdle)
                    SetInspecting(true);
            }
            else if (context.Mode != ControlMode.Inspect)
            {
                SetInspecting(false);                      // something else changed the control mode
            }
            else
            {
                if (!fire.IsOpen)
                    SetInspecting(false);                  // the cylinder was closed by something else
                else if (context.InspectReloadPressed)
                {
                    SetInspecting(false);
                    fire.ToggleCylinder();                 // R: leave and close the cylinder right away
                }
                else if (context.InspectExitPressed)       // Alt or Esc
                    SetInspecting(false);
            }

            amount = Mathf.MoveTowards(amount, inspecting ? 1f : 0f,
                                       Time.deltaTime / Mathf.Max(inspecting ? inTime : outTime, 0.01f));
            UpdateVolume(Mathf.SmoothStep(0f, 1f, amount));
        }

        void SetInspecting(bool on)
        {
            if (inspecting == on) return;
            inspecting = on;
            if (context != null) context.SetMode(on ? ControlMode.Inspect : ControlMode.Gameplay);
            if (logEvents) Debug.Log(on ? "Cylinder inspect: on" : "Cylinder inspect: off");
            InspectChanged?.Invoke(on);
        }

        // ---------- Edge darkening and focus (Post Processing: Vignette + Depth of Field) ----------
        void UpdateVolume(float t)
        {
            if (volume == null)
            {
                if (t <= 0.001f || volumeFailed || !CreateVolume()) return;
            }
            vignette.intensity.value = vignetteIntensity;
            vignette.smoothness.value = vignetteSmoothness;
            vignette.roundness.value = vignetteRoundness;

            volume.weight = t;
            volume.enabled = t > 0.001f;

            // Focus is at the cylinder's distance. Smaller aperture and longer "lens" blur the background more.
            // This Volume always has full weight (otherwise Unity would blend the focus with the default 10 m and blur
            // the weapon itself during the transition); the aperture provides the smoothness: it starts closed (almost no blur).
            if (cam != null)
                dof.focusDistance.value = Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, focusTarget.position) + focusOffset);
            dof.aperture.value = Mathf.Lerp(32f, Mathf.Lerp(16f, 3.5f, blurStrength), t);
            dof.focalLength.value = Mathf.Lerp(12f, 40f, blurStrength);
            dof.kernelSize.value = blurQuality;
            focusVolume.weight = 1f;
            focusVolume.enabled = focusBlur && t > 0.001f;
        }

        bool CreateVolume()
        {
            var layer = cam != null ? cam.GetComponent<PostProcessLayer>() : null;
            if (layer == null)
            {
                Debug.LogWarning("RevolverInspect: no Post-process Layer on the camera, edges won't darken.");
                volumeFailed = true;
                return false;
            }
            int mask = layer.volumeLayer.value, layerIndex = 0;
            while (layerIndex < 32 && (mask & (1 << layerIndex)) == 0) layerIndex++;
            if (layerIndex >= 32) { volumeFailed = true; return false; }

            vignette = ScriptableObject.CreateInstance<Vignette>();
            vignette.enabled.Override(true);
            vignette.mode.Override(VignetteMode.Classic);
            vignette.color.Override(Color.black);
            vignette.center.Override(new Vector2(0.5f, 0.5f));
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(vignetteSmoothness);
            vignette.roundness.Override(vignetteRoundness);

            dof = ScriptableObject.CreateInstance<DepthOfField>();
            dof.enabled.Override(true);
            dof.focusDistance.Override(0.3f);
            dof.aperture.Override(16f);
            dof.focalLength.Override(12f);
            dof.kernelSize.Override(blurQuality);

            // Higher priority than the scene's main Volume and the aim Volume (100): stronger darkening than usual
            volume = PostProcessManager.instance.QuickVolume(layerIndex, 110f, vignette);
            volume.weight = 0f;
            focusVolume = PostProcessManager.instance.QuickVolume(layerIndex, 111f, dof);
            focusVolume.enabled = false;
            return true;
        }

        void OnDisable()
        {
            if (inspecting) SetInspecting(false);
        }

        void OnDestroy()
        {
            if (volume != null) RuntimeUtilities.DestroyVolume(volume, true, true);
            if (focusVolume != null) RuntimeUtilities.DestroyVolume(focusVolume, true, true);
        }

        // ---------- Test buttons (Play Mode) ----------
        [Button("Тест: войти / выйти из осмотра")]
        void TestToggle()
        {
            if (!Application.isPlaying || context == null) return;
            if (!inspecting && !fire.IsOpenIdle) { Debug.Log("Open the cylinder first (R)"); return; }
            SetInspecting(!inspecting);
        }
    }
}
