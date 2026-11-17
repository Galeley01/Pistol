using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Cameras;
using Pistol.Core;
using Pistol.Items;

namespace Pistol.Weapons.Revolver
{
    // Closing the open cylinder with a motion instead of a plain clip. Goes on Revolver (next to RevolverFire).
    //
    // When RevolverFire is asked to close (R), it calls Begin: a way of closing is picked at random
    // from the CylinderCloseProfile (by weight, never the same one more than N times in a row) and its
    // phases play: the weapon turns around the wrist on the grip (a pose layer), the crane snaps shut in
    // the phase marked for it (RevolverFire speeds the close clip up so the latch lands at the end of that
    // phase), the left palm can follow the cylinder and the left hand goes back to the grip.
    // At the latch: sound, a small weapon kick and a camera jolt. Shooting stays blocked until the latch.
    // R in inspect mode (Alt): inspect is left first, and once the weapon is back from the inspect pose
    // the profile's "from inspect" motion plays instead of a random one.
    [DefaultExecutionOrder(-10)]   // before ItemGrip, so the left palm target is fresh in the same frame
    [RequireComponent(typeof(RevolverFire))]
    public class RevolverClose : MonoBehaviour, IPoseLayer
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: способы закрытия"), SerializeField, Expandable]
        [Tooltip("Способы закрытия барабана и их веса")]
        CylinderCloseProfile profile;

        [ShowNativeProperty] public bool IsPlaying => motion != null;
        [ShowNativeProperty] public bool WaitingForInspectExit => waitingInspect;
        [ShowNativeProperty] public string Current => motion != null ? motion.name : (lastMotion != null ? "(было) " + lastMotion.name : "—");

        public int PoseOrder => Items.PoseOrder.Hand;

        RevolverFire fire;
        RevolverCylinder cylinder;
        HeldItem held;
        ItemGrip grip;
        RevolverInspect inspect;
        bool waitingInspect;          // R in inspect: the motion waits until the weapon is out of the inspect pose
        CameraMotion cameraFx;
        GameAudio gameAudio;
        DebugSettings debug;

        CylinderCloseMotion motion, lastMotion;
        int phase;
        float phaseTime, holdTime;
        bool craneStarted, latched;
        bool reopened;                // the cylinder was opened again while the tail of this motion still plays
        Vector3 rotation, offset;     // current weapon pose from the phases
        float palm;                   // current left palm weight
        float kickTime = -1f;
        float soundIn = -1f;          // seconds until the close sound starts (-1 = not waiting)
        bool soundPlanned;            // this close plays its sound timed to the latch
        int lastPick = -1, pickRun;
        float slowedFrom = -1f;       // time scale before the debug slow motion (-1 = not slowed)
        readonly System.Collections.Generic.List<float> weights = new System.Collections.Generic.List<float>();

        const float MaxLatchWait = 0.5f;   // how long a crane phase may wait for a late latch

        public void Bind(ItemContext context)
        {
            cameraFx = context.Camera != null ? context.Camera.GetComponent<CameraMotion>() : null;
            gameAudio = context.Audio;
            debug = context.Debug;
        }

        // Out of the hands mid-close: finish at once, so the revolver isn't left half closed
        public void Unbind()
        {
            if (motion != null) Finish();   // closes the crane at once if its phase hasn't come yet
            kickTime = -1f;
            soundIn = -1f;
            soundPlanned = false;
            cameraFx = null;
            gameAudio = null;
            debug = null;
        }

        void Start()
        {
            fire = GetComponent<RevolverFire>();
            cylinder = GetComponent<RevolverCylinder>();
            held = GetComponent<HeldItem>();
            grip = GetComponent<ItemGrip>();
            inspect = GetComponent<RevolverInspect>();
            fire.CylinderLatched += OnLatched;
            if (profile == null) Debug.LogWarning("RevolverClose: no profile assigned, the cylinder closes with the plain clip.", this);
        }

        // Called by RevolverFire when the cylinder starts closing. False = nothing to play, close the plain way.
        // fromInspect: R was pressed in inspect mode, play the profile's "from inspect" motion
        public bool Begin(bool fromInspect)
        {
            fromInspect = fromInspect && profile != null && profile.fromInspect != null;
            var picked = fromInspect ? profile.fromInspect : Pick();   // the inspect close doesn't count in the random run
            if (picked == null || picked.phases.Count == 0) return false;
            motion = lastMotion = picked;
            phase = 0;
            phaseTime = holdTime = 0f;
            craneStarted = latched = reopened = false;
            if (debug != null && debug.WeaponEvents) Debug.Log("Cylinder close: " + picked.name + (fromInspect ? " (from inspect)" : ""), this);
            waitingInspect = fromInspect;
            if (!waitingInspect || InspectPoseLeft()) StartMotion();
            return true;
        }

        bool InspectPoseLeft() => inspect == null || inspect.Amount <= profile.fromInspectStartAt;

        void StartMotion()
        {
            waitingInspect = false;
            PlanSound();
            StartSlowMotion();
            EnterPhase();
        }

        // The click inside the sound must land on the latch: start the sound that much earlier,
        // or right away with its start skipped when the motion is shorter than the lead-in
        void PlanSound()
        {
            soundIn = -1f;
            soundPlanned = false;
            if (profile == null || profile.latchSound == null) return;
            float toLatch = 0f;
            bool hasCrane = false;
            foreach (var p in motion.phases)
            {
                toLatch += p.duration;
                if (p.craneCloses) { hasCrane = true; break; }
            }
            if (!hasCrane) return;   // no crane phase: the latch comes from the plain clip, OnLatched plays the click
            soundPlanned = true;
            float delay = toLatch - profile.latchSoundClickTime;
            if (delay > 0f) soundIn = delay;
            else PlaySound(-delay);
        }

        void PlaySound(float startAt)
        {
            if (profile == null || profile.latchSound == null) return;
            if (gameAudio == null) { Debug.LogWarning("RevolverClose: no GameAudio in the item context, the close sound is silent.", this); return; }
            if (!profile.latchSound.HasClips) { Debug.LogWarning($"RevolverClose: {profile.latchSound.name} has no audio clip.", profile.latchSound); return; }
            float strength = motion != null ? motion.latchStrength : 1f;
            if (debug != null && debug.WeaponEvents) Debug.Log($"Cylinder close sound: {profile.latchSound.name}, from {startAt:0.000}s, volume x{strength:0.##}", this);
            Transform at = cylinder != null && cylinder.CylinderTransform != null ? cylinder.CylinderTransform : transform;
            gameAudio.Play(profile.latchSound, at, strength, startAt);
        }

        CylinderCloseMotion Pick()
        {
            if (profile == null || profile.options.Count == 0) return null;
            weights.Clear();
            foreach (var o in profile.options) weights.Add(o.motion != null ? o.weight : 0f);
            int i = WeightedPick.Pick(weights, lastPick, pickRun, profile.maxSameInRow, Random.value);
            if (i < 0) return null;
            pickRun = WeightedPick.NextRun(i, lastPick, pickRun);
            lastPick = i;
            return profile.options[i].motion;
        }

        void EnterPhase()
        {
            var p = motion.phases[phase];
            if (debug != null && debug.ClosePauseAtPhase == phase) PauseEditor($"phase {phase} \"{p.name}\"");
            if (reopened) return;   // only the weapon pose plays out; the hand and the crane belong to the open cylinder now
            if (p.leftHandToGrip) held.SetLeftHandFree(false);
            if (p.craneCloses && !craneStarted)
            {
                craneStarted = true;
                fire.SnapCraneShut(p.duration);
            }
        }

        void Update()
        {
            if (soundIn >= 0f)
            {
                soundIn -= Time.deltaTime;
                if (soundIn <= 0f) { PlaySound(-soundIn); soundIn = -1f; }
            }

            if (kickTime >= 0f)
            {
                kickTime += Time.deltaTime;
                if (lastMotion == null || kickTime >= lastMotion.latchKickTime) kickTime = -1f;
            }

            if (motion == null) return;
            if (waitingInspect)
            {
                if (!InspectPoseLeft()) return;
                StartMotion();
            }

            var p = motion.phases[phase];
            phaseTime += Time.deltaTime;
            Evaluate();

            if (phaseTime < p.duration) return;
            // The crane phase waits at its end pose for the latch (in case the clip runs late)
            if (p.craneCloses && !latched && holdTime < MaxLatchWait) { holdTime += Time.deltaTime; return; }

            phase++;
            phaseTime = holdTime = 0f;
            if (phase >= motion.phases.Count) { Finish(); return; }
            EnterPhase();
        }

        // Weapon pose and palm weight inside the current phase: from the previous phase's end to this one's
        void Evaluate()
        {
            var p = motion.phases[phase];
            var prev = phase > 0 ? motion.phases[phase - 1] : null;
            float t = CylinderCloseMotion.Apply(p.ease, phaseTime / Mathf.Max(p.duration, 0.001f));
            rotation = Vector3.LerpUnclamped(prev != null ? prev.rotation : Vector3.zero, p.rotation, t);
            offset = Vector3.LerpUnclamped(prev != null ? prev.offset : Vector3.zero, p.offset, t);
            float tPalm = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / Mathf.Max(p.duration, 0.001f)));
            palm = Mathf.Lerp(prev != null ? prev.leftPalmOnCylinder : 0f, p.leftPalmOnCylinder, tPalm);
            SendPalm();
        }

        void SendPalm()
        {
            if (grip == null) return;
            if (palm <= 0f || motion == null || reopened) { grip.ClearLeftHandOverride(); return; }
            fire.PoseCraneForThisFrame();   // the cylinder where it will be this frame, not where it was last frame
            Vector3 center = cylinder != null && cylinder.CylinderTransform != null
                ? transform.InverseTransformPoint(cylinder.CylinderTransform.position)
                : Vector3.zero;
            // The chosen part of the palm touches the cylinder: shift the palm centre forward/back along the fingers
            Quaternion hand = motion.PalmRotation();
            Vector3 palmCentre = center + motion.palmContact - hand * new Vector3(0f, 0f, motion.contactAlongFingers);
            grip.SetLeftHandOverride(palm, palmCentre, hand, motion.palmFingerCurl);
        }

        void Finish()
        {
            // A motion without a crane phase (or cut short) still has to shut the crane, or the revolver stays stuck closing
            if (!craneStarted && !reopened && fire != null) { craneStarted = true; fire.SnapCraneShut(0f); }
            StopSlowMotion();
            waitingInspect = false;
            motion = null;
            rotation = offset = Vector3.zero;
            palm = 0f;
            if (grip != null) grip.ClearLeftHandOverride();
            if (held != null && !reopened) held.SetLeftHandFree(false);   // in case no phase sent the left hand back
            reopened = false;
        }

        // RevolverFire opens the cylinder again: a motion still playing its tail lets go of the hand and the palm
        public void CylinderReopened()
        {
            if (motion == null) return;
            reopened = true;
            soundIn = -1f;
            palm = 0f;
            if (grip != null) grip.ClearLeftHandOverride();
        }

        // The crane latched (Animation Event in the close clip, raised by RevolverFire)
        void OnLatched()
        {
            latched = true;
            if (motion != null && debug != null && debug.ClosePauseAtLatch) PauseEditor("latch");
            // A close without a timed sound (plain clip) plays just the click, from its place in the file
            if (!soundPlanned && profile != null) PlaySound(profile.latchSoundClickTime);
            soundPlanned = false;
            var m = motion;
            if (m == null) return;   // plain close: no kick or jolt
            float s = m.latchStrength;
            if (cameraFx != null && m.latchCameraShake > 0f) cameraFx.AddShake(m.latchCameraShake * s);
            kickTime = 0f;
        }

        // ---------- Debug: slow motion and editor pauses (DebugSettings, editor and Development builds only) ----------
        void StartSlowMotion()
        {
            float slow = debug != null ? debug.CloseSlowMotion : 1f;
            if (slow >= 1f || slowedFrom >= 0f) return;
            slowedFrom = Time.timeScale;
            Time.timeScale = slowedFrom * slow;
            if (debug.WeaponEvents) Debug.Log($"Cylinder close: slow motion x{slow:0.##}", this);
        }

        void StopSlowMotion()
        {
            if (slowedFrom < 0f) return;
            Time.timeScale = slowedFrom;
            slowedFrom = -1f;
        }

        void OnDisable() => StopSlowMotion();

        void PauseEditor(string where)
        {
            Debug.Log("Cylinder close: editor paused at " + where + " (Step — next frame, Pause — continue)", this);
            Debug.Break();
        }

        public void ApplyPose(ref PoseFrame frame)
        {
            Vector3 euler = rotation;
            if (kickTime >= 0f && lastMotion != null)
            {
                float u = kickTime / Mathf.Max(lastMotion.latchKickTime, 0.001f);
                euler += lastMotion.latchKick * (lastMotion.latchStrength * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)));
            }
            frame.HandEuler += euler;
            frame.HandOffset += offset;
        }

        // ---------- Test buttons (Play Mode) ----------
        [Button("Тест: открыть / закрыть")] void TestToggle() { if (Application.isPlaying && fire != null) fire.ToggleCylinder(); }
    }
}
