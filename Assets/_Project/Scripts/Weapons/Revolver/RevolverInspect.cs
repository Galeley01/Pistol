using UnityEngine;
using NaughtyAttributes;
using Pistol.Items;
using Pistol.Player;
using Pistol.PostFx;

namespace Pistol.Weapons.Revolver
{
    // Cylinder inspect mode. Goes on Revolver (next to RevolverFire).
    //
    // Cylinder open (R) -> left Alt enters inspect mode:
    //   - the camera stops turning, the cursor appears, movement is disabled;
    //   - the weapon moves smoothly into the inspect pose (tuned in the SwayProfile inspect pose foldout;
    //     this component publishes how far we are in it as a pose layer, WeaponSway applies the pose);
    //   - the screen edges get darker and the eyes focus on the cylinder (distant objects blur slightly):
    //     a request to CameraEffects with the inspect ScreenEffectsProfile.
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

        [BoxGroup("Эффекты экрана"), SerializeField]
        [Tooltip("Затемнение краёв и фокус на барабане. Пусто — без эффектов")]
        ScreenEffectsProfile effects;
        [BoxGroup("Эффекты экрана"), Range(-0.2f, 0.5f), Tooltip("Сдвиг фокуса дальше (+) или ближе (−) от барабана, м")]
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

        CameraEffects cameraEffects;
        ScreenEffectsRequest effectsRequest;
        Transform focusTarget;

        public int PoseOrder => Items.PoseOrder.State;

        public void Bind(PoseContext pose)
        {
            context = pose.Player;
            cam = pose.Camera;
            cameraEffects = pose.Effects;
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
            UpdateEffects(Mathf.SmoothStep(0f, 1f, amount));
        }

        void SetInspecting(bool on)
        {
            if (inspecting == on) return;
            inspecting = on;
            if (context != null) context.SetMode(on ? ControlMode.Inspect : ControlMode.Gameplay);
            if (logEvents) Debug.Log(on ? "Cylinder inspect: on" : "Cylinder inspect: off");
            InspectChanged?.Invoke(on);
        }

        // ---------- Edge darkening and focus: a request to CameraEffects ----------
        void UpdateEffects(float t)
        {
            if (effectsRequest == null)
            {
                if (t <= 0.001f || effects == null || cameraEffects == null) return;
                effectsRequest = cameraEffects.CreateRequest(effects, "Inspect");
            }
            float focus = cam != null ? Vector3.Distance(cam.transform.position, focusTarget.position) + focusOffset : 0.3f;
            effectsRequest.Set(t, focus);
        }

        void OnDisable()
        {
            if (inspecting) SetInspecting(false);
        }

        void OnDestroy() => effectsRequest?.Release();

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
