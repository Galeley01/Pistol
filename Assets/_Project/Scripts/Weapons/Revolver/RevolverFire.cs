using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Pistol.Weapons.Revolver
{
    // Goes on the Revolver root (next to the Animator).
    // LMB = fire (cylinder closed) or spin the cylinder (cylinder open).
    // Firing: the cylinder advances one chamber (RevolverCylinder), the hammer falls (OnShot),
    // and if there is a live round the bullet leaves the muzzle (RevolverShooter), otherwise a dry click.
    // R = open / close the cylinder.
    // OnShot / OnSpinStart / OnCylinderClosed are called from the animations (Animation Events).
    [RequireComponent(typeof(Animator))]
    public class RevolverFire : MonoBehaviour
    {
        [BoxGroup("Стрельба"), MinValue(0.05f), Tooltip("Минимальная пауза между выстрелами, сек")]
        public float fireCooldown = 0.35f;

        [BoxGroup("Стрельба"), Tooltip("Точка дула (дочерний объект Muzzle)")]
        public Transform muzzle;

        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float fireSpeed = 1f;
        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float openSpeed = 1f;
        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float closeSpeed = 1f;
        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float spinSpeed = 1f;

        [BoxGroup("Анимации"), Expandable, Tooltip("Углы и тайминги анимаций. Раскройте, чтобы править")]
        public RevolverAnimationSettings animationSettings;

        [BoxGroup("Отладка")] public bool logEvents = true;

        [ShowNativeProperty] public bool IsOpen => isOpen;
        // Cylinder is open and still (not opening, not spinning): it can be inspected
        public bool IsOpenIdle => isOpen && animator != null && InState("OpenIdle");

        // The arms (FPSArmsRig) subscribe to move the left hand away / back
        public event System.Action CylinderOpening;
        public event System.Action CylinderClosing;

        Animator animator;
        RevolverCylinder cylinder;
        RevolverShooter shooter;
        readonly System.Collections.Generic.HashSet<string> animParams = new System.Collections.Generic.HashSet<string>();
        bool isOpen;
        float nextShotTime;

        void Start()
        {
            animator = GetComponent<Animator>();
            if (muzzle == null) muzzle = transform.Find("Muzzle");
            foreach (var p in animator.parameters) animParams.Add(p.name);
            cylinder = GetComponent<RevolverCylinder>();
            shooter = GetComponent<RevolverShooter>();
        }

        void ApplySpeeds()
        {
            SetSpeed("FireSpeed", fireSpeed); SetSpeed("OpenSpeed", openSpeed);
            SetSpeed("CloseSpeed", closeSpeed); SetSpeed("SpinSpeed", spinSpeed);
        }

        void SetSpeed(string param, float value)
        {
            if (animParams.Contains(param)) animator.SetFloat(param, value);
        }

        void Update()
        {
            ApplySpeeds();

            // While the cursor is not captured (Esc pressed), a click only recaptures it
            if (Cursor.lockState != CursorLockMode.Locked) return;

            if (ReadToggleCylinder()) ToggleCylinder();

            if (ReadFire())
            {
                if (isOpen) Spin();
                else Fire();
            }
        }

        // ---------- Actions ----------
        public void Fire()
        {
            if (isOpen || Time.time < nextShotTime || !InState("Idle")) return;
            nextShotTime = Time.time + fireCooldown;
            animator.SetTrigger("Fire");

            // Double action: while the trigger is pulled the cylinder turns one chamber
            float cock = (animationSettings != null ? animationSettings.fireCockTime : 0.117f) / Mathf.Max(fireSpeed, 0.01f);
            if (cylinder != null) cylinder.AdvanceForShot(cock);
        }

        public void ToggleCylinder()
        {
            if (!isOpen && InState("Idle"))
            {
                isOpen = true;
                animator.SetTrigger("Open");
                CylinderOpening?.Invoke();
            }
            else if (isOpen && (InState("OpenIdle") || InState("Spin")))
            {
                isOpen = false;
                animator.ResetTrigger("Spin");
                animator.SetTrigger("Close");
                CylinderClosing?.Invoke();
            }
        }

        public void Spin()
        {
            if (isOpen && InState("OpenIdle")) animator.SetTrigger("Spin");
        }

        bool InState(string state) => animator.GetCurrentAnimatorStateInfo(0).IsName(state) && !animator.IsInTransition(0);

        // ---------- Animation Events ----------
        // The hammer fell on the chamber under it
        public void OnShot()
        {
            if (cylinder == null)
            {
                // No ammo tracking: always fire
                if (shooter != null) shooter.Fire(null);
                if (logEvents) Debug.Log("BANG!");
                return;
            }

            int chamber = cylinder.Model.UnderHammer;
            var state = cylinder.Model.GetState(chamber);
            if (cylinder.TryFire(out var ammo))
            {
                if (shooter != null) shooter.Fire(ammo);
                if (logEvents) Debug.Log($"BANG! (chamber {chamber})  {cylinder.Model.Describe()}");
            }
            else if (logEvents)
            {
                Debug.Log($"Click: chamber {chamber} holds {(state == RoundState.Spent ? "a spent case" : "nothing")}  {cylinder.Model.Describe()}");
            }
            // Flash, sound and recoil are handled by RevolverFeedback (subscribed to CylinderModel.Fired)
        }

        public void OnSpinStart()
        {
            if (logEvents) Debug.Log("Cylinder spun");
            // TODO: cylinder ratchet sound
        }

        public void OnCylinderClosed()
        {
            if (logEvents) Debug.Log("Click: cylinder closed");
            // TODO: cylinder latch sound
        }

        // ---------- Inspector test buttons (Play Mode) ----------
        [Button("Тест: выстрел")] void TestFire() { if (Application.isPlaying) Fire(); }
        [Button("Тест: открыть / закрыть")] void TestToggle() { if (Application.isPlaying) ToggleCylinder(); }
        [Button("Тест: раскрутить")] void TestSpin() { if (Application.isPlaying) Spin(); }

        // ---------- Animation buttons (editor only) ----------
        [Button("Создать недостающие анимации")]
        void CreateMissingAnimations()
        {
#if UNITY_EDITOR
            if (animationSettings == null) animationSettings = RevolverAnimationSettings.GetOrCreateDefault();
            animationSettings.Generate(false);
#endif
        }

        [Button("Перезаписать анимации из настроек")]
        void OverwriteAnimations()
        {
#if UNITY_EDITOR
            if (animationSettings == null) animationSettings = RevolverAnimationSettings.GetOrCreateDefault();
            animationSettings.OverwriteButton();
#endif
        }

        // ---------- Input ----------
#if ENABLE_INPUT_SYSTEM
        bool ReadFire()           => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool ReadToggleCylinder() => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        bool ReadFire()           => Input.GetMouseButtonDown(0);
        bool ReadToggleCylinder() => Input.GetKeyDown(KeyCode.R);
#endif
    }
}
