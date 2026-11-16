using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Items;
using Pistol.Player;

namespace Pistol.Weapons
{
    // "Alive" weapon in the hands.
    // - Weight and inertia: when the camera turns, the weapon stays put in the world and catches up with a delay
    //   (spring with mass), with a slight overshoot.
    // - Roll from turn speed: the faster the turn, the more the barrel rolls on Z towards the turn.
    // - Rotation around the wrist: the barrel travels noticeably more than the grip, so the wrists move, not the elbows.
    // - Free-Aim: the weapon rotates by FreeAimOffset from PlayerMovement (deadzone).
    // - Movement bob, random hand drift, jump/landing kicks, lowered weapon while sprinting, inspect pose.
    //
    // Pose layer for ItemHolderWithPose (hand step): adds its angles and offsets to the frame,
    // never writes the transform. Recoil is a separate layer (WeaponRecoil).
    //
    // Goes on the weapon root (Revolver).
    [RequireComponent(typeof(ItemRig))]
    public class WeaponSway : MonoBehaviour, IPoseLayer
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: раскачивание оружия"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        SwayProfile profile;
        public SwayProfile Profile => profile;

        [BoxGroup("Отладка"), Range(0f, 1f), Tooltip("Ставит позу осмотра без Alt — удобно подбирать ползунки в Play Mode (барабан при этом можно не открывать)")]
        public float inspectPreview = 0f;
        [BoxGroup("Отладка"), Tooltip("Маленькая точка там, куда смотрит ствол (удобно с Free-Aim)")]
        public bool showAimPoint = false;
        [BoxGroup("Отладка"), ShowIf("showAimPoint"), Range(2f, 12f)] public float aimPointSize = 4f;

        // Fallbacks when ItemRig has no Right Hand Grip / Rear Sight (weapon space)
        static readonly Vector3 DefaultWrist = new Vector3(0.015f, -0.03f, -0.07f);
        static readonly Vector3 DefaultRearSight = new Vector3(0f, 0.055f, 0.01f);

        Camera cam;
        Transform muzzle;
        ItemRig rig;
        Vector3 wristPoint;          // the wrist (right hand grip) in weapon space: rotation pivot from the hip
        Vector3 rearSightPoint;      // the rear sight in weapon space: rotation pivot while aiming
        PlayerMovement movement;
        CharacterController controller;

        public int PoseOrder => Items.PoseOrder.Hand;

        // Weapon lag behind the view: x = pitch (down +), y = yaw (right +), z = roll
        Vector3 lag, lagVel;
        Vector3 posSpring, posVel;
        Vector2 lastFreeAim;
        float worldYawRate;
        float bobPhase, runWeight, runPose, driftTime, moveNoiseTime, sprintStoppedTime;
        int lastStep;
        bool wasGrounded = true;
        float lastAirVelocity;
        float aimBreathPhase, aimShakeTime, instability, instabilityTime;

        // External kick (e.g. the weapon reached the eye): rotation velocity (deg/s) and position velocity (m/s)
        public void AddKick(Vector3 rotationVelocity, Vector3 positionVelocity)
        {
            lagVel += rotationVelocity;
            posVel += positionVelocity;
        }

        // Disturb the grip (0..1): the barrel wanders until the grip settles (like after turning while aiming)
        public void AddInstability(float amount) => instability = Mathf.Clamp01(instability + amount);

        [ShowNativeProperty] public float Instability => instability;

        void Awake()
        {
            if (profile == null) { Debug.LogError("WeaponSway: no profile assigned.", this); enabled = false; return; }
            rig = ItemRig.On(this);
            muzzle = rig != null ? rig.MuzzleOrRoot : transform;
        }

        public void Bind(ItemContext context)
        {
            cam = context.Camera;
            movement = context.Movement;
            controller = context.Controller;
        }

        void Start()
        {
            if (rig == null) return;
            if (rig.RightHandGrip == null) Debug.LogWarning("WeaponSway: ItemRig has no Right Hand Grip, using a default wrist point.", this);
            if (rig.RearSight == null) Debug.LogWarning("WeaponSway: ItemRig has no Rear Sight, using a default rear sight point.", this);
            wristPoint = rig.LocalPoint(rig.RightHandGrip, DefaultWrist);
            rearSightPoint = rig.LocalPoint(rig.RearSight, DefaultRearSight);
        }

        public void ApplyPose(ref PoseFrame frame)
        {
            float dt = frame.DeltaTime;
            float aim = frame.Aim;
            float ip = Mathf.SmoothStep(0f, 1f, Mathf.Max(frame.Inspect, inspectPreview));   // inspect pose
            float k = profile.masterIntensity * Mathf.Lerp(1f, profile.aimMultiplier, aim)       // bob, drift
                    * Mathf.Lerp(1f, profile.inspectSwayMultiplier, ip);
            float kLag = profile.masterIntensity * Mathf.Lerp(1f, profile.aimLagMultiplier, aim); // inertia and lag

            // ---------- Player movement ----------
            Vector3 localVel = Vector3.zero;
            bool grounded = true;
            if (controller != null)
            {
                localVel = controller.transform.InverseTransformDirection(controller.velocity);
                grounded = controller.isGrounded;
            }
            float hSpeed = new Vector2(localVel.x, localVel.z).magnitude;
            float walk = movement != null ? movement.Profile.walkSpeed : 3f;
            float run = movement != null ? movement.Profile.runSpeed : 6f;
            bool running = movement != null && movement.IsRunning;
            float breathless = movement != null ? movement.Breathless : 0f;

            // ---------- Where the weapon should point (Free-Aim) and how the view turned ----------
            Vector2 freeAim = movement != null ? movement.FreeAimOffset : Vector2.zero;
            Vector2 camDelta = movement != null ? movement.AppliedLookDelta : Vector2.zero;
            Vector2 aimDelta = camDelta + (freeAim - lastFreeAim);   // total world rotation of the "target" this frame
            lastFreeAim = freeAim;

            // ---------- 1. Weight: the weapon stays put in the world, the target moves ahead ----------
            lag.y -= aimDelta.x;
            lag.x += aimDelta.y;
            lag.x = Mathf.Clamp(lag.x, -profile.maxLag, profile.maxLag);
            lag.y = Mathf.Clamp(lag.y, -profile.maxLag, profile.maxLag);

            // ---------- 2. Roll from turn speed (towards the turn) ----------
            float yawRate = aimDelta.x / dt + lagVel.y;   // how fast the barrel turns in the world
            worldYawRate = Mathf.Lerp(worldYawRate, yawRate, 1f - Mathf.Exp(-12f * dt));
            float rollTarget = Mathf.Clamp(-worldYawRate * profile.rollPerTurnSpeed, -profile.maxRoll, profile.maxRoll);
            float dipTarget = Mathf.Min(Mathf.Abs(worldYawRate) * profile.dipPerTurnSpeed, profile.maxRoll * 0.5f);

            // ---------- 3. Spring targets from movement ----------
            Vector3 lagTarget = new Vector3(dipTarget + localVel.z * profile.forwardDip, 0f, rollTarget - localVel.x * profile.strafeRoll);
            Vector3 posTarget = -localVel * profile.moveLag;

            // ---------- 4. Jump and landing ----------
            if (movement != null && movement.JumpedThisFrame)
            {
                posVel += new Vector3(0f, -0.2f, 0f) * profile.jumpKick;
                lagVel += new Vector3(-40f, 0f, 10f) * profile.jumpKick;
            }
            if (controller != null)
            {
                if (!grounded) lastAirVelocity = controller.velocity.y;
                if (grounded && !wasGrounded && lastAirVelocity < -2f)
                {
                    float f = Mathf.Min(-lastAirVelocity * profile.landKick, 1.5f);
                    posVel += new Vector3(0f, -0.5f, -0.08f) * f;
                    lagVel += new Vector3(70f, Random.Range(-15f, 15f), Random.Range(-25f, 25f)) * f;
                }
            }
            wasGrounded = grounded;

            // ---------- Springs with mass ----------
            float stiffness = profile.responsiveness / Mathf.Max(profile.weaponWeight, 0.05f);
            float damping = 2f * profile.dampingRatio * Mathf.Sqrt(stiffness);
            Spring.Step(ref lag, ref lagVel, lagTarget, stiffness, damping, dt);
            Spring.Step(ref posSpring, ref posVel, posTarget, stiffness, damping, dt);

            // ---------- 5. Chaotic movement bob ----------
            // Strength follows speed smoothly: a calm walk is weak, running is strong.
            float speed01 = grounded ? Mathf.Clamp01(hSpeed / Mathf.Max(run, 0.1f)) : 0f;
            float intensity = Mathf.Pow(speed01, profile.speedCurve);
            runWeight = Mathf.Lerp(runWeight, running && grounded ? 1f : 0f, 1f - Mathf.Exp(-5f * dt));
            float rate = hSpeed < walk ? profile.walkStepRate * Mathf.Lerp(0.75f, 1f, hSpeed / walk)
                                       : Mathf.Lerp(profile.walkStepRate, profile.runStepRate, Mathf.InverseLerp(walk, run, hSpeed));
            bobPhase += Mathf.PI * rate * dt * Mathf.Clamp01(hSpeed / 0.3f);

            // Every step is a random kick into the spring: direction and strength differ each time,
            // and the spring turns them into lively, uneven hand motion
            int step = Mathf.FloorToInt(bobPhase / Mathf.PI);
            if (step != lastStep && intensity > 0.01f)
            {
                float side = (step & 1) == 0 ? 1f : -1f;          // steps alternate: left / right foot
                float a = profile.stepKickPosition * intensity;
                float r = profile.stepKickRotation * intensity;
                posVel += new Vector3(side * Random.Range(0.2f, 1f) * a * 0.6f,
                                      -Random.Range(0.5f, 1.2f) * a,
                                      Random.Range(-0.4f, 0.4f) * a);
                lagVel += new Vector3(Random.Range(0.3f, 1.2f) * r,
                                      side * Random.Range(0.1f, 0.8f) * r * 0.6f + Random.Range(-0.3f, 0.3f) * r,
                                      side * Random.Range(0.3f, 1.2f) * r);
            }
            lastStep = step;

            // Uneven "floating": frequency and strength grow with speed
            moveNoiseTime += dt * Mathf.Lerp(0.5f, 2.4f, speed01);
            Vector3 bobPos = Noise.Perlin3(moveNoiseTime, 80f) * profile.moveNoisePosition * intensity * k;
            Vector3 bobRot = Noise.Perlin3(moveNoiseTime, 120f) * profile.moveNoiseRotation * intensity * k;

            // ---------- 6. Random hand drift ----------
            float driftMul = Mathf.Lerp(1f, profile.breathlessDrift, breathless) * k;
            driftTime += dt * profile.driftSpeed * Mathf.Lerp(1f, 2.2f, breathless);
            Vector3 driftPos = Noise.Perlin3(driftTime, 0f) * profile.driftPosition * driftMul;
            Vector3 driftRot = Noise.Perlin3(driftTime, 40f) * profile.driftRotation * driftMul;

            // ---------- 7. Weapon lowered while sprinting ----------
            bool sprintPose = profile.lowerWhileRunning && running && grounded && aim < 0.5f;
            if (sprintPose) sprintStoppedTime = 0f; else sprintStoppedTime += dt;
            if (sprintPose)
                runPose = Mathf.MoveTowards(runPose, 1f, dt / Mathf.Max(profile.sprintPoseInTime, 0.01f));
            else if (sprintStoppedTime >= profile.sprintPoseHold || aim > 0f)
                runPose = Mathf.MoveTowards(runPose, 0f, dt / Mathf.Max(aim > 0f ? profile.sprintPoseInTime : profile.sprintPoseOutTime, 0.01f));
            float rp = Mathf.SmoothStep(0f, 1f, runPose);

            // ---------- Assembly ----------
            Vector3 aimRot = new Vector3(-freeAim.y, freeAim.x, 0f) * (1f - ip);   // Free-Aim is exact; centered while inspecting
            // ---------- 8. Aiming: rotate around the rear sight, front sight wanders ----------
            // Fatigue: 0 = rested, 1 = exhausted
            float fatigue = movement != null ? movement.Fatigue : 0f;

            // Breathing: a slow figure eight, faster and wider when tired
            aimBreathPhase += dt * Mathf.Lerp(1.6f, 5.5f, fatigue);
            float breathAmp = Mathf.Lerp(profile.aimBreathCalm, profile.aimBreathTired, fatigue) * aim;
            Vector3 aimBreath = new Vector3(Mathf.Sin(aimBreathPhase) * breathAmp,
                                            Mathf.Sin(aimBreathPhase * 0.5f + 0.7f) * breathAmp * 0.7f, 0f);

            // Chaotic tremor: faster and stronger when tired
            aimShakeTime += dt * Mathf.Lerp(0.6f, 2.6f, fatigue);
            Vector3 aimShake = (Noise.Perlin3(aimShakeTime, 200f) + Noise.Perlin3(aimShakeTime * 2.7f, 260f) * 0.4f)
                             * Mathf.Lerp(profile.aimShakeCalm, profile.aimShakeTired, fatigue) * aim;

            // Barrel settling: turning while aiming disturbs the grip, the barrel wanders until it settles
            instability = Mathf.Clamp01(instability + Mathf.Abs(worldYawRate) * profile.instabilityGain * aim * dt);
            instability = Mathf.MoveTowards(instability, 0f, dt / Mathf.Lerp(profile.stabilizeTime, profile.tiredStabilizeTime, fatigue));
            instabilityTime += dt * 3.5f;
            Vector3 aimInstab = Noise.Perlin3(instabilityTime, 320f) * profile.instabilityAmount * instability * aim;

            Vector3 aimSway = aimBreath + aimShake + aimInstab;

            // ---------- Assembly ----------
            Vector3 euler = aimRot + lag * kLag + bobRot + driftRot + aimSway + profile.sprintRotation * rp + profile.inspectRotation * ip;

            // The hand shifts slightly after the rotation (as if leading with the wrist)
            Vector3 shift = new Vector3((aimRot.y + lag.y * kLag) * profile.handShift, -(aimRot.x + lag.x * kLag) * profile.handShift, 0f);
            Vector3 offset = posSpring * k + bobPos + driftPos + profile.sprintPosition * rp + profile.inspectPosition * ip + shift;

            // From the hip rotate around the wrist (the barrel travels more than the grip);
            // while aiming, around the rear sight: it stays at the eye while the front sight wanders (sights misalign)
            frame.HandEuler += euler;
            frame.HandOffset += offset;
            frame.HandPivot = Vector3.Lerp(wristPoint, rearSightPoint, aim);
        }

        void OnGUI()
        {
            if (!showAimPoint || cam == null) return;
            Transform from = muzzle != null ? muzzle : transform;
            Vector3 sp = cam.WorldToScreenPoint(from.position + transform.forward * 30f);
            if (sp.z <= 0f) return;
            float s = aimPointSize;
            var r = new Rect(sp.x - s * 0.5f, Screen.height - sp.y - s * 0.5f, s, s);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
