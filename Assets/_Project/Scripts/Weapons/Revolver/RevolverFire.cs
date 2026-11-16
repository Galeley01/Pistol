using UnityEngine;
using NaughtyAttributes;
using Pistol.Items;

namespace Pistol.Weapons.Revolver
{
    using Hash = RevolverAnimatorIds.Hash;

    // Goes on the Revolver root (next to the Animator and HeldItem).
    // Intents come from HeldItem (ItemInput reads the keys):
    // Use (LMB) = fire (cylinder closed) or spin the cylinder (cylinder open).
    // Firing: the cylinder advances one chamber (RevolverCylinder), the hammer falls (OnShot),
    // and if there is a live round the bullet leaves the muzzle (RevolverShooter), otherwise a dry click.
    // Reload (R) = open / close the cylinder. While it is open the weapon can't aim and the left hand lets go.
    // OnShot / OnSpinStart / OnCylinderClosed are called from the animations (Animation Events).
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(HeldItem))]
    public class RevolverFire : MonoBehaviour
    {
        [BoxGroup("Стрельба"), MinValue(0.05f), Tooltip("Минимальная пауза между выстрелами, сек")]
        public float fireCooldown = 0.35f;

        [BoxGroup("Анимации"), Expandable, Tooltip("Углы, тайминги и скорости анимаций. Раскройте, чтобы править")]
        public RevolverAnimationSettings animationSettings;

        [BoxGroup("Отладка")] public bool logEvents = true;

        [ShowNativeProperty] public bool IsOpen => isOpen;
        // Cylinder is open and still (not opening, not spinning): it can be inspected
        public bool IsOpenIdle => isOpen && animator != null && InState(Hash.OpenIdleState);

        Animator animator;
        HeldItem held;
        RevolverCylinder cylinder;
        RevolverShooter shooter;
        readonly System.Collections.Generic.HashSet<int> animParams = new System.Collections.Generic.HashSet<int>();
        bool isOpen;
        float nextShotTime;

        void Start()
        {
            animator = GetComponent<Animator>();
            foreach (var p in animator.parameters) animParams.Add(p.nameHash);
            cylinder = GetComponent<RevolverCylinder>();
            shooter = GetComponent<RevolverShooter>();
            held = GetComponent<HeldItem>();
            held.UseRequested += OnUse;
            held.ReloadRequested += ToggleCylinder;
        }

        // LMB: fire with the cylinder closed, spin it when it is open
        void OnUse()
        {
            if (isOpen) Spin();
            else Fire();
        }

        // Speeds come from the animation settings every frame, so they can be tuned live
        float FireSpeed => animationSettings != null ? animationSettings.fireSpeed : 1f;

        void ApplySpeeds()
        {
            var s = animationSettings;
            SetSpeed(Hash.FireSpeed, FireSpeed);
            SetSpeed(Hash.OpenSpeed, s != null ? s.openSpeed : 1f);
            SetSpeed(Hash.CloseSpeed, s != null ? s.closeSpeed : 1f);
            SetSpeed(Hash.SpinSpeed, s != null ? s.spinSpeed : 1f);
        }

        void SetSpeed(int param, float value)
        {
            if (animParams.Contains(param)) animator.SetFloat(param, value);
        }

        void Update() => ApplySpeeds();

        // ---------- Actions ----------
        public void Fire()
        {
            if (isOpen || Time.time < nextShotTime || !InState(Hash.IdleState)) return;
            nextShotTime = Time.time + fireCooldown;
            animator.SetTrigger(Hash.Fire);

            // Double action: while the trigger is pulled the cylinder turns one chamber
            float cock = (animationSettings != null ? animationSettings.fireCockTime : 0.117f) / Mathf.Max(FireSpeed, 0.01f);
            if (cylinder != null) cylinder.AdvanceForShot(cock);
        }

        public void ToggleCylinder()
        {
            if (!isOpen && InState(Hash.IdleState))
            {
                isOpen = true;
                animator.SetTrigger(Hash.Open);
                held.SetCanAim(false);
                held.SetLeftHandFree(true);
            }
            else if (isOpen && (InState(Hash.OpenIdleState) || InState(Hash.SpinState)))
            {
                isOpen = false;
                animator.ResetTrigger(Hash.Spin);
                animator.SetTrigger(Hash.Close);
                held.SetCanAim(true);
                held.SetLeftHandFree(false);
            }
        }

        public void Spin()
        {
            if (isOpen && InState(Hash.OpenIdleState)) animator.SetTrigger(Hash.Spin);
        }

        bool InState(int stateHash) => animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash && !animator.IsInTransition(0);

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
        [Button("Проверить Animator")]
        void ValidateAnimator()
        {
#if UNITY_EDITOR
            var missing = RevolverAnimationSettings.FindMissing(GetComponent<Animator>().runtimeAnimatorController);
            if (missing.Count == 0) Debug.Log("Revolver Animator: all parameters and states are in place.", this);
            else Debug.LogWarning("Revolver Animator is missing: " + string.Join(", ", missing), this);
#endif
        }

        [Button("Создать недостающие анимации")]
        void CreateMissingAnimations()
        {
#if UNITY_EDITOR
            if (animationSettings == null) animationSettings = RevolverAnimationSettings.GetOrCreateDefault();
            animationSettings.Generate(false, this);
#endif
        }

        [Button("Перезаписать анимации из настроек")]
        void OverwriteAnimations()
        {
#if UNITY_EDITOR
            if (animationSettings == null) animationSettings = RevolverAnimationSettings.GetOrCreateDefault();
            animationSettings.OverwriteButton(this);
#endif
        }
    }
}
