using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
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
    // Closing: with RevolverClose on the revolver a close motion plays (swing, palm push...) and snaps the crane
    // shut in its own phase; shooting and aiming come back only at the latch.
    // OnShot / OnSpinStart / OnCylinderClosed are called from the animations (Animation Events).
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(HeldItem))]
    public class RevolverFire : MonoBehaviour, IItemContextUser
    {
        [BoxGroup("Стрельба"), MinValue(0.05f), Tooltip("Минимальная пауза между выстрелами, сек")]
        public float fireCooldown = 0.35f;

        [BoxGroup("Анимации"), Expandable, Tooltip("Углы, тайминги и скорости анимаций. Раскройте, чтобы править")]
        public RevolverAnimationSettings animationSettings;

        [ShowNativeProperty] public bool IsOpen => isOpen;
        // Closing was asked for but the crane hasn't latched yet: no shooting, no aiming
        [ShowNativeProperty] public bool IsClosing => closing;
        // The crane latched shut (Animation Event in the close clip)
        public event System.Action CylinderLatched;
        // Cylinder is open and still (not opening, not spinning): it can be inspected
        public bool IsOpenIdle => isOpen && animator != null && InState(Hash.OpenIdleState);

        Animator animator;
        HeldItem held;
        DebugSettings debug;

        public void Bind(ItemContext context) => debug = context.Debug;
        public void Unbind() => debug = null;
        bool LogEvents => debug != null && debug.WeaponEvents;
        RevolverCylinder cylinder;
        RevolverShooter shooter;
        readonly System.Collections.Generic.HashSet<int> animParams = new System.Collections.Generic.HashSet<int>();
        RevolverClose closer;
        bool isOpen, closing;
        float nextShotTime;
        float closeSpeedOverride;    // > 0 while a close motion plays the close clip faster
        float latchClipTime;         // when the latch event comes in the close clip, sec at speed 1
        AnimationClip closeClip;     // the clip with the latch event

        void Start()
        {
            animator = GetComponent<Animator>();
            // A put away revolver keeps its mechanics as they were (an open cylinder stays open),
            // otherwise the Animator would restart from Idle and disagree with IsOpen
            animator.keepAnimatorStateOnDisable = true;
            foreach (var p in animator.parameters) animParams.Add(p.nameHash);
            cylinder = GetComponent<RevolverCylinder>();
            shooter = GetComponent<RevolverShooter>();
            closer = GetComponent<RevolverClose>();
            latchClipTime = FindLatchTime();
            held = GetComponent<HeldItem>();
            held.UseRequested += OnUse;
            held.ReloadRequested += ToggleCylinder;
            held.InspectReloadRequested += CloseFromInspect;
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
            SetSpeed(Hash.CloseSpeed, closeSpeedOverride > 0f ? closeSpeedOverride : (s != null ? s.closeSpeed : 1f));
            SetSpeed(Hash.SpinSpeed, s != null ? s.spinSpeed : 1f);
        }

        void SetSpeed(int param, float value)
        {
            if (animParams.Contains(param)) animator.SetFloat(param, value);
        }

        void Update()
        {
            ApplySpeeds();
            // Safety net: a close clip without the latch event still ends the closing once back in Idle
            if (closing && InState(Hash.IdleState)) { closing = false; held.SetCanAim(true); }
        }

        // ---------- Actions ----------
        public void Fire()
        {
            if (isOpen || closing || Time.time < nextShotTime || !InState(Hash.IdleState)) return;
            nextShotTime = Time.time + fireCooldown;
            animator.SetTrigger(Hash.Fire);

            // Double action: while the trigger is pulled the cylinder turns one chamber
            float cock = (animationSettings != null ? animationSettings.fireCockTime : 0.117f) / Mathf.Max(FireSpeed, 0.01f);
            if (cylinder != null) cylinder.AdvanceForShot(cock);
        }

        public void ToggleCylinder() => ToggleCylinder(false);

        // R in inspect mode: inspect has just been left, the cylinder closes the "from inspect" way
        void CloseFromInspect() => ToggleCylinder(true);

        void ToggleCylinder(bool fromInspect)
        {
            if (!isOpen && !closing && InState(Hash.IdleState))
            {
                // Reopened while a close motion still plays its tail: it must not take the left hand back
                if (closer != null) closer.CylinderReopened();
                isOpen = true;
                closeSpeedOverride = 0f;
                animator.SetTrigger(Hash.Open);
                held.SetCanAim(false);
                held.SetLeftHandFree(true);
            }
            else if (isOpen && (InState(Hash.OpenIdleState) || InState(Hash.SpinState)))
            {
                isOpen = false;
                closing = true;
                animator.ResetTrigger(Hash.Spin);
                // A close motion snaps the crane shut itself, in its own phase (SnapCraneShut)
                if (closer != null && closer.isActiveAndEnabled && closer.Begin(fromInspect)) return;
                SnapCraneShut(0f);
                held.SetLeftHandFree(false);
            }
        }

        // Starts the close clip. latchIn > 0: play it faster so the latch comes in latchIn seconds;
        // 0: at the speed from the animation settings
        public void SnapCraneShut(float latchIn)
        {
            if (isOpen) return;
            closeSpeedOverride = latchIn > 0f && latchClipTime > 0f ? latchClipTime / latchIn : 0f;
            ApplySpeeds();
            animator.SetTrigger(Hash.Close);
        }

        // The latch time is read from the close clip itself, so a hand-edited clip still lines up
        float FindLatchTime()
        {
            var ctrl = animator.runtimeAnimatorController;
            if (ctrl != null)
                foreach (var clip in ctrl.animationClips)
                    foreach (var e in clip.events)
                        if (e.functionName == nameof(OnCylinderClosed)) { closeClip = clip; return e.time; }
            return animationSettings != null ? animationSettings.closeDuration * RevolverAnimationSettings.CloseLatchFraction : 0f;
        }

        // The Animator moves the crane later in the frame than Update, so whoever reads the cylinder position
        // in Update (the left palm following it) would be a frame behind, and the sped-up crane moves tens of
        // degrees per frame. This puts the crane where the close clip will have it this frame; the Animator then
        // writes the same pose. Only while the close clip plays.
        public void PoseCraneForThisFrame()
        {
            if (closeClip == null || animator == null || animator.IsInTransition(0)) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != Hash.CloseState) return;
            float clipTime = state.normalizedTime * closeClip.length + Time.deltaTime * state.speed * state.speedMultiplier;
            closeClip.SampleAnimation(gameObject, Mathf.Min(clipTime, closeClip.length));
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
                if (LogEvents) Debug.Log("BANG!");
                return;
            }

            int chamber = cylinder.Model.UnderHammer;
            var state = cylinder.Model.GetState(chamber);
            if (cylinder.TryFire(out var ammo, debug != null && debug.InfiniteAmmo))
            {
                if (shooter != null) shooter.Fire(ammo);
                if (LogEvents) Debug.Log($"BANG! (chamber {chamber})  {cylinder.Model.Describe()}");
            }
            else if (LogEvents)
            {
                Debug.Log($"Click: chamber {chamber} holds {(state == RoundState.Spent ? "a spent case" : "nothing")}  {cylinder.Model.Describe()}");
            }
            // Flash, sound and recoil are handled by RevolverFeedback (subscribed to CylinderModel.Fired)
        }

        public void OnSpinStart()
        {
            if (LogEvents) Debug.Log("Cylinder spun");
            // TODO: cylinder ratchet sound
        }

        public void OnCylinderClosed()
        {
            if (LogEvents) Debug.Log("Click: cylinder closed");
            closing = false;
            held.SetCanAim(true);
            CylinderLatched?.Invoke();   // sound and kick: RevolverClose
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
