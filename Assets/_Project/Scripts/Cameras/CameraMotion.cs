using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Player;
using Pistol.Items;

namespace Pistol.Cameras
{
    // Goes on the Main Camera, which sits inside CameraRoot.
    // Camera motion: head bob, footstep impacts, running shake,
    // jump and landing jolts, breathing and post-sprint panting,
    // turn inertia, strafe tilt, Q/E leaning (smaller while crouched).
    [RequireComponent(typeof(Camera))]
    public class CameraMotion : MonoBehaviour
    {
        // ===================== References =====================
        [BoxGroup("Ссылки"), Required("Назначьте CharacterController игрока")] public CharacterController controller;
        [BoxGroup("Ссылки"), Required("Назначьте PlayerMovement игрока")] public PlayerMovement movement;
        [BoxGroup("Ссылки"), Required("Назначьте ItemHolderWithPose (предмет в руках)"), Tooltip("Откуда брать, насколько игрок целится: в прицеле голову трясёт меньше")]
        public ItemHolderWithPose holder;

        [BoxGroup("Профиль"), Required("Назначьте профиль: движение камеры"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        CameraMotionProfile profile;
        public CameraMotionProfile Profile => profile;


        Camera cam;
        float bobPhase, bobWeight, runWeight, tiredWeight;
        int lastStep;
        float breathPhase, shakeTime, impactTime;
        float impact, jumpKick;
        float stepImpact, stepImpactTime;
        Vector3 shakeFiltered;
        Vector3 sway;
        float strafe, landing, lean;
        float leanLimitLeft, leanLimitRight;
        bool wasGrounded = true;
        float lastAirVelocity;
        Vector3 posCurrent, rotCurrent;
        Vector2 nod, nodVel;          // nod spring: x = forward/back, y = roll
        Vector3 recoilCam, recoilCamVel;   // recoil: x = up, z = roll, degrees
        float bodyRun;

        void Start()
        {
            if (profile == null) { Debug.LogError("CameraMotion: no profile assigned.", this); enabled = false; return; }
            cam = GetComponent<Camera>();
            if (controller == null || movement == null) { Debug.LogError("CameraMotion: assign Controller and Movement.", this); enabled = false; return; }

            cam.fieldOfView = profile.fieldOfView;
            cam.nearClipPlane = 0.01f;
            leanLimitLeft = leanLimitRight = profile.leanOffset;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || controller == null) return;

            Vector3 localVel = controller.transform.InverseTransformDirection(controller.velocity);
            float hSpeed = new Vector2(localVel.x, localVel.z).magnitude;
            bool grounded = controller.isGrounded;

            float walk      = movement != null ? movement.Profile.walkSpeed : 3f;
            float run       = movement != null ? movement.Profile.runSpeed : 6f;
            bool running    = movement != null && movement.IsRunning;
            bool exhausted  = movement != null && movement.IsExhausted;
            float crouch    = movement != null ? movement.CrouchAmount : 0f;
            float breathless = movement != null ? movement.Breathless : 0f;
            float preFatigue = movement != null ? movement.PreFatigue : 0f;

            // ---------- Head bob ----------
            float moveFactor = grounded ? Mathf.Clamp01(hSpeed / walk) : 0f;
            bobWeight = Smoothing.Damp(bobWeight, moveFactor, 8f, dt);
            runWeight = Smoothing.Damp(runWeight, running && grounded ? 1f : 0f, 5f, dt);
            tiredWeight = Smoothing.Damp(tiredWeight, running && exhausted ? 1f : 0f, 3f, dt);

            // Fatigue build-up (seconds 6 to 8 of sprinting): the run gradually gets heavier.
            // pre = build-up strength, fade = removes the extra effects once exhaustion kicks in,
            // so the exhausted run stays exactly as it was.
            float pre = preFatigue * runWeight;
            float fade = 1f - tiredWeight;
            float tiredMix = Mathf.Max(tiredWeight, pre * profile.preFatigueBobBlend);

            // Step rate: walking and running use a realistic wide stride; the exhausted run keeps the old rate
            float normalRate = hSpeed < walk
                ? profile.walkStepRate * Mathf.Lerp(0.75f, 1f, hSpeed / walk)
                : Mathf.Lerp(profile.walkStepRate, profile.runStepRate, Mathf.InverseLerp(walk, run, hSpeed));
            float tiredRate = hSpeed / Mathf.Max(profile.tiredStepLength, 0.1f);
            float stepRate = Mathf.Lerp(normalRate, tiredRate, tiredMix);
            bobPhase += Mathf.PI * stepRate * dt * Mathf.Clamp01(hSpeed / 0.3f);   // pi per step

            // Bob profile: walk -> run -> exhausted run
            float v  = Mathf.Lerp(Mathf.Lerp(profile.walkBobVertical,   profile.runBobVertical,   runWeight), profile.tiredBobVertical,   tiredMix);
            float h  = Mathf.Lerp(Mathf.Lerp(profile.walkBobHorizontal, profile.runBobHorizontal, runWeight), profile.tiredBobHorizontal, tiredMix);
            float r  = Mathf.Lerp(Mathf.Lerp(profile.walkBobRoll,       profile.runBobRoll,       runWeight), profile.tiredBobRoll,       tiredMix);
            float p  = Mathf.Lerp(Mathf.Lerp(profile.walkBobPitch,      profile.runBobPitch,      runWeight), profile.tiredBobPitch,      tiredMix);

            // Vertical bob shape: a soft wave when walking, the old sharp "dip" when exhausted
            float sharpness = Mathf.Max(runWeight * 0.5f, tiredMix);
            float vShape = Mathf.Lerp(Mathf.Cos(bobPhase * 2f), 1f - 2f * Mathf.Abs(Mathf.Sin(bobPhase)), sharpness);

            float aimCalm = Mathf.Lerp(1f, profile.aimCameraShake, holder != null ? holder.Aim : 0f);
            float amp = bobWeight * Mathf.Lerp(1f, profile.crouchBobMultiplier, crouch) * Mathf.Lerp(1f, 0.6f, 1f - aimCalm);
            Vector3 bobPos = new Vector3(Mathf.Sin(bobPhase) * h, vShape * v, 0f) * amp;
            Vector3 bobRot = new Vector3(Mathf.Sin(bobPhase * 2f) * p, 0f, Mathf.Sin(bobPhase) * r) * amp;

            // ---------- Body while running: forward lean and torso twist from step to step ----------
            float bodyMask = profile.bodyEffectsWhenTired ? 1f : 1f - tiredWeight;
            bodyRun = Smoothing.Damp(bodyRun, running && grounded ? 1f : 0f, 3f, dt);
            float twist = Mathf.Lerp(profile.walkTorsoTwist, profile.runTorsoTwist, runWeight) * bobWeight * bodyMask
                        * Mathf.Lerp(1f, profile.crouchBobMultiplier, crouch);
            bobRot += new Vector3(profile.runForwardLean * bodyRun * bodyMask, Mathf.Sin(bobPhase) * twist, 0f);
            bobPos += new Vector3(Mathf.Sin(bobPhase) * profile.runTorsoShift * runWeight * bobWeight * bodyMask, 0f, 0f);

            // ---------- Nod when braking / lean back when accelerating (spring) ----------
            Vector3 acc = movement != null ? movement.LocalAcceleration : Vector3.zero;
            Vector2 nodTarget = new Vector2(Mathf.Clamp(-acc.z * profile.accelerationNod, -profile.maxNod, profile.maxNod),
                                            Mathf.Clamp(-acc.x * profile.accelerationRoll, -profile.maxNod, profile.maxNod));
            nodVel += ((nodTarget - nod) * 90f - nodVel * 11f) * dt;
            nod += nodVel * dt;
            bobRot += new Vector3(nod.x, 0f, nod.y);

            // ---------- Impact of each step ----------
            int step = Mathf.FloorToInt(bobPhase / Mathf.PI);
            if (step != lastStep && grounded && bobWeight > 0.4f)
                stepImpact = Mathf.Min(1f, stepImpact
                    + (Mathf.Lerp(profile.stepImpactWalk, profile.stepImpactRun, runWeight) + profile.preFatigueStepImpact * pre * fade)
                      * Mathf.Lerp(1f, 0.5f, crouch));
            lastStep = step;

            // ---------- Breathing and panting ----------
            float idle = 1f - bobWeight;
            breathPhase += dt * Mathf.Lerp(profile.breathSpeed, profile.heavyBreathSpeed, breathless);
            float breath = Mathf.Sin(breathPhase);
            Vector3 breathPos = new Vector3(0f, breath * (profile.breathAmount * idle + profile.heavyBreathAmount * breathless), 0f);
            Vector3 breathRot = new Vector3(breath * (profile.breathPitch * idle + profile.heavyBreathPitch * breathless), 0f, 0f);

            // ---------- Mouse turn inertia ----------
            // Actual camera rotation (Free-Aim aware): while the weapon moves inside the deadzone, the camera doesn't sway
            Vector2 look = movement != null ? movement.AppliedLookDelta : Vector2.zero;
            Vector3 swayTarget = new Vector3(
                Mathf.Clamp(look.y * profile.swayAmount, -profile.swayMaxAngle, profile.swayMaxAngle),
                Mathf.Clamp(-look.x * profile.swayAmount * 0.5f, -profile.swayMaxAngle, profile.swayMaxAngle),
                Mathf.Clamp(-look.x * profile.swayAmount, -profile.swayMaxAngle, profile.swayMaxAngle));
            sway = Vector3.Lerp(sway, swayTarget, 1f - Mathf.Exp(-profile.swaySmooth * dt));

            // ---------- Strafe tilt ----------
            strafe = Smoothing.Damp(strafe, -Mathf.Clamp(localVel.x / walk, -1f, 1f) * profile.strafeTilt, 6f, dt);

            // ---------- Jump ----------
            if (movement != null && movement.JumpedThisFrame)
            {
                AddImpact(profile.jumpImpact);
                jumpKick = 1f;
            }
            jumpKick = Smoothing.Damp(jumpKick, 0f, 5f, dt);

            // ---------- Landing ----------
            if (!grounded) lastAirVelocity = controller.velocity.y;
            if (grounded && !wasGrounded && lastAirVelocity < -2f)
            {
                landing = Mathf.Min(profile.landingMax, -lastAirVelocity * profile.landingStrength);
                AddImpact(-lastAirVelocity * profile.landingImpact);
            }
            wasGrounded = grounded;
            landing = Smoothing.Damp(landing, 0f, profile.landingRecover, dt);
            Vector3 landPos = new Vector3(0f, -landing, 0f);
            Vector3 landRot = new Vector3(landing * 20f - jumpKick * profile.jumpKickAngle, 0f, 0f);

            // ---------- Q / E leaning (smaller while crouched) ----------
            float leanMul = Mathf.Lerp(1f, profile.crouchLeanMultiplier, crouch);
            float maxLean = profile.leanOffset * leanMul;
            float leanInput = movement != null ? movement.LeanInput : 0f;
            UpdateLeanLimits(maxLean);
            lean = Smoothing.Damp(lean, leanInput, profile.leanSpeed, dt);
            float limit = lean > 0f ? leanLimitRight : leanLimitLeft;
            float limitRatio = maxLean > 0.001f ? limit / maxLean : 0f;
            float leanDist = lean * limit;
            Vector3 leanPos = new Vector3(leanDist, -Mathf.Abs(leanDist) * 0.15f, 0f);
            Vector3 leanRot = new Vector3(0f, 0f, -lean * profile.leanAngle * leanMul * limitRatio);

            // ---------- Smoothed part ----------
            Vector3 targetPos = bobPos + breathPos + landPos + leanPos;
            Vector3 targetRot = bobRot + breathRot + sway + landRot + leanRot + new Vector3(0f, 0f, strafe);
            posCurrent = Vector3.Lerp(posCurrent, targetPos, 1f - Mathf.Exp(-profile.smoothing * dt));
            rotCurrent = Vector3.Lerp(rotCurrent, targetRot, 1f - Mathf.Exp(-profile.smoothing * dt));

            // ---------- Shake (added without smoothing so it stays sharp) ----------
            // 1) handheld: stronger when walking, even stronger when running and panting
            float shakeAmt = profile.shakeIdle
                           + (profile.shakeMoving - profile.shakeIdle) * bobWeight
                           + (profile.shakeRunning - profile.shakeMoving) * runWeight * bobWeight
                           + profile.breathlessShake * breathless
                           + profile.preFatigueShake * pre * fade;
            shakeTime += dt * profile.shakeSpeed * (1f + breathless * 1.5f + runWeight * profile.runShakeSpeedBoost);
            Vector3 shakeRot = Noise.Perlin3(shakeTime, 0f) * shakeAmt;

            // 2) fine jitter while running
            Vector3 jitterRot = Noise.Perlin3(Time.time * profile.runJitterSpeed, 50f)
                              * (profile.runJitter * runWeight + profile.preFatigueJitter * pre * fade) * bobWeight;

            // 3) soft footstep impacts
            stepImpact = Mathf.Max(0f, stepImpact - profile.stepImpactDecay * dt);
            stepImpactTime += dt * profile.stepImpactSpeed;
            Vector3 stepRot = Noise.Perlin3(stepImpactTime, 300f) * profile.stepImpactAngle * stepImpact;

            // While running and not yet exhausted, the shake is filtered so it doesn't jerk.
            // Once exhausted the filter is off and the shake is sharp again.
            // From second 6 the filter gradually weakens and the shake gets sharper.
            float calmRun = exhausted ? 0f : runWeight * (1f - preFatigue);
            float filterSpeed = Mathf.Lerp(60f, profile.runShakeSmoothing, calmRun);
            shakeFiltered = Vector3.Lerp(shakeFiltered, shakeRot + jitterRot + stepRot,
                                         1f - Mathf.Exp(-filterSpeed * dt));

            // 4) sharp jolts from jumping and landing (no smoothing)
            impact = Mathf.Max(0f, impact - profile.impactDecay * dt);
            impactTime += dt * profile.impactSpeed;
            float imp = impact * impact;
            Vector3 impactRot = Noise.Perlin3(impactTime, 100f) * profile.impactMaxAngle * imp;
            Vector3 impactPos = Noise.Perlin3(impactTime, 200f) * profile.impactMaxOffset * imp;

            // 5) recoil: sharp and unsmoothed, otherwise it "floats"
            RecoilSpring.Step(ref recoilCam, ref recoilCamVel, profile.recoilRiseTime, profile.recoilReturnTime, 1f, dt);
            Vector3 recoilRot = new Vector3(-recoilCam.x, 0f, recoilCam.z);

            transform.localPosition = posCurrent + impactPos;
            transform.localRotation = Quaternion.Euler(rotCurrent + shakeFiltered * aimCalm + impactRot + recoilRot);
        }

        void AddImpact(float amount) => impact = Mathf.Min(1f, impact + amount);

        // ---------- Recoil and other external jolts ----------
        // Sharp camera shake (0..1)
        public void AddShake(float amount) => AddImpact(amount);
        // Nod kick: throws the camera up (deg/s) and rolls it slightly (deg/s), then the spring returns it
        public void AddRecoilKick(float pitchUpVelocity, float rollVelocity) => nodVel += new Vector2(-pitchUpVelocity, rollVelocity);
        // Short sharp head kick from a shot: peak in degrees (up, roll)
        public void AddRecoilPunch(float pitchUpDegrees, float rollDegrees)
            => recoilCamVel += new Vector3(pitchUpDegrees, 0f, rollDegrees) * RecoilSpring.ImpulseFor(1f, profile.recoilRiseTime);

        // Keep the lean from going through walls
        void UpdateLeanLimits(float maxLean)
        {
            Transform root = transform.parent != null ? transform.parent : transform;
            leanLimitRight = CastLean(root, root.right, maxLean);
            leanLimitLeft = CastLean(root, -root.right, maxLean);
        }

        float CastLean(Transform root, Vector3 dir, float maxLean)
        {
            const float radius = 0.15f;
            if (Physics.SphereCast(root.position, radius, dir, out RaycastHit hit, maxLean,
                    profile.leanCollisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider != controller)
                return Mathf.Max(0f, hit.distance - 0.05f);
            return maxLean;
        }
    }
}
