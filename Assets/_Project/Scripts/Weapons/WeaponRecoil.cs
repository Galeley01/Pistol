using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;
using Pistol.Items;

namespace Pistol.Weapons
{
    // Shot recoil of the weapon in the hands: a stiff spring that flips the barrel around the wrist on the grip
    // (the grip stays in the hand) and pushes the weapon back. Pose layer for ItemHolderWithPose.
    // How hard each shot kicks is decided by the caller (RevolverFeedback from RecoilProfile).
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

        ItemRig rig;
        Vector3 wristPoint;   // right hand grip in weapon space
        Vector3 recoilRot, recoilRotVel, recoilPos, recoilPosVel;

        public int PoseOrder => Items.PoseOrder.Recoil;

        // Shot recoil: how many degrees the barrel flips (x- = up, y = side, z = roll)
        // and how many meters the weapon moves (z- = back towards the face). These are peaks, not velocities.
        public void AddRecoil(Vector3 peakRotation, Vector3 peakPosition)
        {
            if (profile == null) return;
            float k = RecoilSpring.ImpulseFor(1f, profile.recoilRiseTime);
            recoilRotVel += peakRotation * k;
            recoilPosVel += peakPosition * k;
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
        public void Unbind() => recoilRot = recoilRotVel = recoilPos = recoilPosVel = Vector3.zero;

        public void ApplyPose(ref PoseFrame frame)
        {
            float dt = frame.DeltaTime;
            RecoilSpring.Step(ref recoilRot, ref recoilRotVel, profile.recoilRiseTime, profile.recoilReturnTime, profile.recoilReturnDamping, dt);
            RecoilSpring.Step(ref recoilPos, ref recoilPosVel, profile.recoilRiseTime, profile.recoilReturnTime, profile.recoilReturnDamping, dt);
            frame.RecoilEuler += recoilRot;
            frame.RecoilOffset += recoilPos;
            frame.RecoilPivot = wristPoint + recoilPivotOffset;
        }
    }
}
