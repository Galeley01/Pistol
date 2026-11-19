using UnityEngine;
using NaughtyAttributes;
using Pistol.Arms;

namespace Pistol.Items
{
    // How the hands hold this item: places the ItemRig grip points (wrists, elbow hints) from the HandGripProfile
    // and gives the arms the finger curls. When the item frees the left hand (HeldItem.LeftHandFree, e.g. the
    // revolver cylinder is open), the left hand moves away along an arc and relaxes its fingers.
    // The item may also lead the left hand to its own target for a moment (SetLeftHandOverride), e.g. the palm
    // that pushes the cylinder shut. For debugging live fingers the left hand can also move aside (SetLeftHandLive).
    // The grip belongs to the item, so any arms skin holds it the same way.
    //
    // Goes on the item root, next to ItemRig and HeldItem.
    [RequireComponent(typeof(ItemRig), typeof(HeldItem))]
    [DisallowMultipleComponent]
    public class ItemGrip : MonoBehaviour
    {
        [BoxGroup("Хват"), Required("Назначьте профиль хвата"), SerializeField]
        [Tooltip("Как руки держат этот предмет. Кликните по полю, чтобы открыть ассет")]
        HandGripProfile profile;

        [BoxGroup("Хват"), Tooltip("Вкл: точки хвата каждый кадр берутся из профиля (правка цифрами).\n" +
                                    "Выкл: точки можно двигать мышкой в Scene, потом нажать «Записать положение точек в профиль».")]
        [ValidateInput("IsLive", "Выключено: изменения в профиле хвата сейчас НЕ применяются. Включите, чтобы править хват цифрами.")]
        public bool liveFromProfile = true;

        bool IsLive(bool value) => value;

        // Start hand directions in item space (X right, Y up, Z forward): fingers and palm
        static readonly Vector3 RightFingerDir = new Vector3(-0.15f, -0.35f, 0.92f);
        static readonly Vector3 RightPalmDir   = new Vector3(-0.95f, 0.10f, 0.25f);  // palm faces left, onto the grip
        static readonly Vector3 LeftFingerDir  = new Vector3(0.45f, -0.35f, 0.80f);
        static readonly Vector3 LeftPalmDir    = new Vector3(0.90f, 0.30f, -0.20f);  // palm faces right, onto the right hand

        ItemRig rig;
        float leftBlend = 1f;      // 1 = left hand on the grip, 0 = moved away
        float leftTarget01 = 1f;
        float liveBlend;           // 1 = left hand in the live-fingers pose (debug)
        bool liveWanted;
        Quaternion liveWrist = Quaternion.identity;   // camera space, anatomical
        Vector3 liveOffset;                           // camera space, meters
        float liveWristWeight;
        Transform liveCamera;
        bool warnedLegacy;

        public HandGripProfile Profile => profile;
        [ShowNativeProperty] public bool LeftHandOnGrip => leftTarget01 > 0.5f;

        float LeftT => Mathf.SmoothStep(0f, 1f, leftBlend);

        // Temporary target for the left hand from the item itself (e.g. the palm pushing the revolver cylinder shut)
        float overrideWeight;
        Vector3 overridePalmPoint;          // item-root space
        Quaternion overrideRotation = Quaternion.identity;   // anatomical hand rotation, item-root space
        float overrideCurl;

        // weight 0..1 blends the left hand from its usual pose to this target.
        // palmPoint: where the palm centre (HandGripProfile.leftPalmCenter) touches, item-root space, m;
        // handRotation: anatomical hand rotation in item-root space (Z to the fingers, Y out of the palm);
        // fingerCurl: degrees per joint
        public void SetLeftHandOverride(float weight, Vector3 palmPoint, Quaternion handRotation, float fingerCurl)
        {
            overrideWeight = Mathf.Clamp01(weight);
            overridePalmPoint = palmPoint;
            overrideRotation = handRotation;
            overrideCurl = fingerCurl;
        }

        public void ClearLeftHandOverride() => overrideWeight = 0f;

        // Debug: while the left fingers follow a hand tracking tool, the left hand moves aside from the item
        // (HandGripProfile → "Левая рука — живые пальцы") and comes back when turned off
        public void SetLeftHandLive(bool live) => liveWanted = live;

        // The tracked wrist for the live pose: rotation and offset in the camera's space, weight 0..1
        public void SetLeftHandLiveWrist(Transform camera, Quaternion wrist, Vector3 offset, float weight)
        {
            liveCamera = camera;
            liveWrist = wrist;
            liveOffset = offset;
            liveWristWeight = Mathf.Clamp01(weight);
        }
        public bool LeftHandLive => liveBlend > 0f;

        void Awake()
        {
            rig = GetComponent<ItemRig>();
            GetComponent<HeldItem>().LeftHandFreeChanged += free => leftTarget01 = free ? 0f : 1f;
        }

        void Update()
        {
            if (profile == null) return;
            if (profile.rotationSpace == 0 && !warnedLegacy)
            {
                warnedLegacy = true;
                Debug.LogWarning($"ItemGrip: {profile.name} still has hand rotations of the old arms. Open the ArmsDefinition of those arms (Convert Legacy Grips on) and press \"Build arms prefab\".", profile);
            }
            leftBlend = Mathf.MoveTowards(leftBlend, leftTarget01, Time.deltaTime / Mathf.Max(profile.leftHandMoveTime, 0.01f));
            liveBlend = Mathf.MoveTowards(liveBlend, liveWanted ? 1f : 0f, Time.deltaTime / Mathf.Max(profile.leftLiveMoveTime, 0.01f));
            if (liveFromProfile) ApplyProfileToPoints();
        }

        // Degrees of bend for each joint of a finger (0 thumb .. 4 pinky)
        public float FingerCurl(bool right, int finger)
        {
            if (profile == null) return 0f;
            var pose = right ? profile.right : profile.left;
            float angle = finger switch
            {
                0 => pose.thumb, 1 => pose.index, 2 => pose.middle, 3 => pose.ring, _ => pose.pinky
            };
            if (right) return angle;
            return Mathf.Lerp(Mathf.Lerp(profile.leftAwayFingerCurl, angle, LeftT), overrideCurl, overrideWeight);
        }

        void ApplyProfileToPoints()
        {
            if (rig.RightHandGrip == null || rig.LeftHandGrip == null) return;
            var p = profile;
            SetLocal(rig.RightHandGrip, p.right.wristPosition, p.right.wristRotation);
            if (rig.RightElbowHint) rig.RightElbowHint.localPosition = p.right.elbowHint;

            // Left hand: blend between "on grip" and "away", arcing outwards halfway
            float t = LeftT;
            Vector3 arc = new Vector3(-1f, 0.3f, -0.5f).normalized * (Mathf.Sin(t * Mathf.PI) * p.leftHandArc);
            Quaternion gripRot = Quaternion.Euler(p.left.wristRotation);
            Quaternion awayRot = gripRot * Quaternion.Euler(p.leftAwayRotationOffset);
            rig.LeftHandGrip.localPosition = Vector3.Lerp(p.leftAwayPosition, p.left.wristPosition, t) + arc;
            rig.LeftHandGrip.localRotation = Quaternion.Slerp(awayRot, gripRot, t);
            if (rig.LeftElbowHint) rig.LeftElbowHint.localPosition = Vector3.Lerp(p.leftAwayElbowHint, p.left.elbowHint, t);

            // Live fingers (debug): the left hand goes aside, along its own arc
            if (liveBlend > 0f)
            {
                float l = Mathf.SmoothStep(0f, 1f, liveBlend);
                Vector3 liveArc = new Vector3(-0.6f, 0.6f, -0.5f).normalized * (Mathf.Sin(l * Mathf.PI) * p.leftHandArc);
                Vector3 livePos = p.leftLivePosition;
                Quaternion liveRot = gripRot * Quaternion.Euler(p.leftLiveRotationOffset);
                // The tracked wrist: its turn as seen from the eyes, and how far the hand moved from the place above
                var parent = rig.LeftHandGrip.parent;
                if (liveCamera != null && liveWristWeight > 0f)
                {
                    Vector3 move = liveCamera.rotation * liveOffset;
                    if (parent != null) move = parent.InverseTransformVector(move);
                    Quaternion rot = liveCamera.rotation * liveWrist;
                    if (parent != null) rot = Quaternion.Inverse(parent.rotation) * rot;
                    livePos += move * liveWristWeight;
                    liveRot = Quaternion.Slerp(liveRot, rot, liveWristWeight);
                }
                rig.LeftHandGrip.localPosition = Vector3.Lerp(rig.LeftHandGrip.localPosition, livePos, l) + liveArc;
                rig.LeftHandGrip.localRotation = Quaternion.Slerp(rig.LeftHandGrip.localRotation, liveRot, l);
                if (rig.LeftElbowHint) rig.LeftElbowHint.localPosition = Vector3.Lerp(rig.LeftElbowHint.localPosition, p.leftLiveElbowHint, l);
            }

            // The item's own target on top (palm on the cylinder): the palm centre goes to the point and the wrist
            // is placed back from it; converted from item-root space to the grip point's parent
            if (overrideWeight > 0f)
            {
                var parent = rig.LeftHandGrip.parent;
                Vector3 wrist = overridePalmPoint - overrideRotation * p.leftPalmCenter;
                Vector3 target = parent != null ? parent.InverseTransformPoint(transform.TransformPoint(wrist)) : wrist;
                Quaternion targetRot = parent != null ? Quaternion.Inverse(parent.rotation) * transform.rotation * overrideRotation : overrideRotation;
                rig.LeftHandGrip.localPosition = Vector3.Lerp(rig.LeftHandGrip.localPosition, target, overrideWeight);
                rig.LeftHandGrip.localRotation = Quaternion.Slerp(rig.LeftHandGrip.localRotation, targetRot, overrideWeight);
            }
        }

        static void SetLocal(Transform t, Vector3 pos, Vector3 euler)
        {
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
        }

        static Vector3 Normalize(Vector3 e) =>
            new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));

        // ================= Buttons =================
        [Button("Записать положение точек в профиль")]
        void SaveTargetsToProfile()
        {
            var r = GetComponent<ItemRig>();
            if (profile == null || r == null || r.RightHandGrip == null || r.LeftHandGrip == null)
            {
                Debug.LogWarning("ItemGrip: needs a profile and the ItemRig hand grips.", this);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(profile, "Save Grip Points");
#endif
            profile.right.wristPosition = r.RightHandGrip.localPosition;
            profile.right.wristRotation = Normalize(r.RightHandGrip.localEulerAngles);
            if (r.RightElbowHint) profile.right.elbowHint = r.RightElbowHint.localPosition;
            profile.left.wristPosition = r.LeftHandGrip.localPosition;
            profile.left.wristRotation = Normalize(r.LeftHandGrip.localEulerAngles);
            if (r.LeftElbowHint) profile.left.elbowHint = r.LeftElbowHint.localPosition;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(profile);
#endif
            Debug.Log("ItemGrip: grip points saved to " + profile.name, this);
        }

        [Button("Сбросить хват к стартовому")]
        void ResetProfile()
        {
#if UNITY_EDITOR
            if (profile == null) return;
            if (!UnityEditor.EditorUtility.DisplayDialog("Сбросить хват?", "Профиль вернётся к стартовым значениям.", "Сбросить", "Отмена")) return;
            UnityEditor.Undo.RecordObject(profile, "Reset Grip");
            var fresh = ScriptableObject.CreateInstance<HandGripProfile>();
            profile.right = fresh.right;
            profile.left = fresh.left;
            DestroyImmediate(fresh);
            profile.right.wristRotation = Normalize(Quaternion.LookRotation(RightFingerDir, RightPalmDir).eulerAngles);
            profile.left.wristRotation = Normalize(Quaternion.LookRotation(LeftFingerDir, LeftPalmDir).eulerAngles);
            profile.rotationSpace = 1;
            UnityEditor.EditorUtility.SetDirty(profile);
#endif
        }
    }
}
