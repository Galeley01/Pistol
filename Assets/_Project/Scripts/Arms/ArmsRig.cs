using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using NaughtyAttributes;
using Pistol.Items;

namespace Pistol.Arms
{
    // Arms of one skin holding the item in the hands, through Animation Rigging (Two Bone IK).
    // Works only with bone roles: the references below are filled by the "Build arms prefab" button
    // of the ArmsDefinition (from the bone names, once, in the editor).
    // At runtime the arms take the grip points of the held item (ItemRig) and the finger curls of its ItemGrip;
    // they know nothing about any particular weapon.
    //
    // Goes on the root of an arms prefab (next to the Animator and the RigBuilder). PlayerArms creates it.
    [RequireComponent(typeof(Animator), typeof(RigBuilder))]
    [DisallowMultipleComponent]
    public class ArmsRig : MonoBehaviour
    {
        [System.Serializable]
        public class Arm
        {
            public Transform upperArm, forearm, hand;
            [Tooltip("Поворот «анатомической» кисти (пальцы вперёд, ладонь — ось Y) в осях кости кисти этого скелета")]
            public Quaternion handFrame = Quaternion.identity;
            public TwoBoneIKConstraint ik;
        }

        [System.Serializable]
        public class FingerJoint
        {
            public Transform bone;
            public Quaternion restLocal;
            public Vector3 axis;     // bend axis in the bone's local space
            public bool right;
            public int finger;       // 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky
        }

        [Foldout("Риг (заполняется кнопкой в ArmsDefinition)"), ReadOnly, SerializeField] ArmsDefinition definition;
        [Foldout("Риг (заполняется кнопкой в ArmsDefinition)"), SerializeField] Arm right = new Arm();
        [Foldout("Риг (заполняется кнопкой в ArmsDefinition)"), SerializeField] Arm left = new Arm();
        [SerializeField, HideInInspector] List<FingerJoint> joints = new List<FingerJoint>();

        RigBuilder rigBuilder;
        ItemGrip grip;
        Transform rightTarget, leftTarget;   // children of the item's grip points: anatomical hand -> this skin's hand bone

        public ArmsDefinition Definition => definition;
        [ShowNativeProperty] public string HoldingItem => grip != null ? grip.name : "—";

        void Awake() => rigBuilder = GetComponent<RigBuilder>();

        // Holds the given item; null lets go
        public void Attach(HeldItem item)
        {
            Detach();
            if (item == null) return;

            var rig = item.GetComponent<ItemRig>();
            grip = item.GetComponent<ItemGrip>();
            if (rig == null || rig.RightHandGrip == null || rig.LeftHandGrip == null)
            {
                Debug.LogWarning($"ArmsRig: {item.name} has no ItemRig hand grips, the arms won't hold it.", item);
                return;
            }
            if (right.ik == null || left.ik == null)
            {
                Debug.LogError("ArmsRig: the rig is not built. Press \"Build arms prefab\" in the ArmsDefinition.", this);
                return;
            }

            rightTarget = CreateTarget(rig.RightHandGrip, right, "ArmsTarget_R");
            leftTarget = CreateTarget(rig.LeftHandGrip, left, "ArmsTarget_L");
            Point(right.ik, rightTarget, rig.RightElbowHint);
            Point(left.ik, leftTarget, rig.LeftElbowHint);

            // The IK binds its transforms when the rig is built
            if (rigBuilder.enabled) rigBuilder.Build();
            else rigBuilder.enabled = true;
        }

        public void Detach()
        {
            if (rigBuilder != null) rigBuilder.enabled = false;
            if (rightTarget != null) Destroy(rightTarget.gameObject);
            if (leftTarget != null) Destroy(leftTarget.gameObject);
            rightTarget = leftTarget = null;
            grip = null;
        }

        // The grip point holds the anatomical hand; this child turns it into the hand bone of this skeleton
        static Transform CreateTarget(Transform gripPoint, Arm arm, string targetName)
        {
            var t = new GameObject(targetName).transform;
            t.SetParent(gripPoint, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Inverse(arm.handFrame);
            return t;
        }

        static void Point(TwoBoneIKConstraint ik, Transform target, Transform hint)
        {
            ik.weight = 1f;
            ik.data.target = target;
            ik.data.hint = hint;
            ik.data.targetPositionWeight = 1f;
            ik.data.targetRotationWeight = 1f;
            ik.data.hintWeight = hint != null ? 1f : 0f;
        }

        // Finger curls from the item's grip, after animation
        void LateUpdate()
        {
            if (grip == null) return;
            foreach (var j in joints)
            {
                if (j.bone == null) continue;
                float angle = grip.FingerCurl(j.right, j.finger);
                if (definition != null) angle += definition.FingerCurlOffset(j.right, j.finger);   // this skin's correction
                j.bone.localRotation = j.restLocal * Quaternion.AngleAxis(angle, j.axis);
            }
        }

        void OnDestroy() => Detach();

        // Editor only: called by the "Build arms prefab" button
        public void SetBuiltRig(ArmsDefinition source, Arm rightArm, Arm leftArm, List<FingerJoint> fingerJoints)
        {
            definition = source;
            right = rightArm;
            left = leftArm;
            joints = fingerJoints;
        }

        // Draw the hand targets in the Scene view
        void OnDrawGizmos()
        {
            DrawTarget(rightTarget, new Color(0.2f, 1f, 0.3f));
            DrawTarget(leftTarget, new Color(0.3f, 0.6f, 1f));
        }

        static void DrawTarget(Transform t, Color c)
        {
            if (t == null) return;
            Gizmos.color = c;
            Gizmos.DrawWireSphere(t.position, 0.012f);
            Gizmos.DrawLine(t.position, t.position + t.forward * 0.04f);
        }
    }
}
