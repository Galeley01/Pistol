using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Items;

namespace Pistol.Weapons
{
    // Shot recoil of the weapon in the hands. Pose layer for ItemHolderWithPose.
    // How hard each shot kicks is decided by the caller (RevolverFeedback from RecoilProfile).
    //
    // Every shot plays one RecoilShape (rise, nod below the start, slow settle); shots overlap and add up.
    //   - The barrel flips around the wrist on the grip and nods once below the start on the way back.
    //   - The whole arm follows a moment later (up and back on the rise, lower on the nod),
    //     so the muzzle goes up and comes back along different paths: a loop, not one arc.
    //   - While the gun is levelled it makes one short move to the side.
    //
    // Goes on the weapon root (Revolver).
    [RequireComponent(typeof(ItemRig))]
    public class WeaponRecoil : MonoBehaviour, IPoseLayer
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: отдача"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        RecoilProfile profile;
        public RecoilProfile Profile => profile;

        [BoxGroup("Геометрия модели"), Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.1f)]
        [Tooltip("Точка, вокруг которой подлетает ствол при отдаче: от кисти на рукояти. Ниже/назад — рукоять сильнее упирается, ствол сильнее подлетает")]
        public Vector3 recoilPivotOffset = Vector3.zero;

        const int MaxShots = 12;   // older shots are long settled by then

        struct Shot
        {
            public float Time;
            public Vector3 Rotation;   // peak degrees: x- = up, y = side, z = roll
            public Vector3 Position;   // peak meters of the whole arm: y = up, z- = back
            public float SettleSide;   // degrees of the one side move while levelling
            public RecoilShape Shape;
        }

        readonly List<Shot> shots = new List<Shot>(MaxShots);
        ItemRig rig;
        Vector3 wristPoint;   // right hand grip in weapon space
        float clock;

        public int PoseOrder => Items.PoseOrder.Recoil;

        [ShowNativeProperty] public int ActiveShots => shots.Count;

        // Shot recoil: how many degrees the barrel flips (x- = up, y = side, z = roll)
        // and how many meters the arm moves (y = up, z- = back towards the face). These are peaks, not velocities.
        // strain 0..1 (tired, running): a deeper nod and a slower settle.
        public void AddRecoil(Vector3 peakRotation, Vector3 peakPosition, float strain = 0f)
        {
            if (profile == null) return;
            strain = Mathf.Clamp01(strain);

            var shape = new RecoilShape(
                profile.recoilRiseTime,
                profile.recoilReturnTime,
                profile.settleTime * Mathf.Lerp(1f, profile.strainSettleMultiplier, strain),
                profile.nodDepth * Mathf.Lerp(1f, profile.strainNodMultiplier, strain));

            // The side move scales with how hard this shot flipped (aim, fatigue, variation)
            float flipShare = profile.flipAngle > 0.01f ? Mathf.Abs(peakRotation.x) / profile.flipAngle : 1f;
            float side = profile.settleSideAngle * flipShare * Random.Range(0.5f, 1f) * (Random.value < 0.5f ? -1f : 1f);

            if (shots.Count >= MaxShots) shots.RemoveAt(0);
            shots.Add(new Shot { Time = clock, Rotation = peakRotation, Position = peakPosition, SettleSide = side, Shape = shape });
        }

        void Awake()
        {
            if (profile == null) { Debug.LogError("WeaponRecoil: no profile assigned.", this); enabled = false; return; }
            rig = ItemRig.On(this);
        }

        void Start()
        {
            var hand = rig != null ? rig.RightHandGrip : null;
            if (hand != null) wristPoint = transform.InverseTransformPoint(hand.position);
            else Debug.LogWarning("WeaponRecoil: ItemRig has no Right Hand Grip, recoil rotates around the weapon root.", this);
        }

        public void Bind(ItemContext context) { }

        // Out of the hands: an unfinished kick must not continue when the weapon is taken again
        public void Unbind()
        {
            shots.Clear();
            clock = 0f;
        }

        public void ApplyPose(ref PoseFrame frame)
        {
            // The clock only runs while something plays, so it never grows big enough to lose precision
            if (shots.Count == 0) { clock = 0f; frame.RecoilPivot = wristPoint + recoilPivotOffset; return; }
            clock += frame.DeltaTime;
            float armLag = profile.armLag;
            Vector3 rot = Vector3.zero, pos = Vector3.zero;

            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                float age = clock - s.Time;
                if (age >= s.Shape.Duration + armLag) { shots.RemoveAt(i); continue; }

                // Wrist: the barrel flips and nods; side and roll come back without swinging over
                float flip = s.Shape.WithNod(age);
                float settle = s.Shape.WithoutNod(age);
                rot.x += s.Rotation.x * flip;
                rot.y += s.Rotation.y * settle + s.SettleSide * s.Shape.SettleBump(age);
                rot.z += s.Rotation.z * settle;

                // Arm: the same motion a moment later, up on the rise and lower on the nod
                float armAge = age - armLag;
                pos.y += s.Position.y * s.Shape.WithNod(armAge);
                pos.x += s.Position.x * s.Shape.WithoutNod(armAge);
                pos.z += s.Position.z * s.Shape.WithoutNod(armAge);
            }

            frame.RecoilEuler += rot;
            frame.RecoilOffset += pos;
            frame.RecoilPivot = wristPoint + recoilPivotOffset;
        }
    }
}
