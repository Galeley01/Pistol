using UnityEngine;
using NaughtyAttributes;
using Pistol.Arms;

namespace Pistol.Items
{
    // How the hands hold this item: places the ItemRig grip points (wrists, elbow hints) from the HandGripProfile
    // and gives the arms the finger curls. When the item frees the left hand (HeldItem.LeftHandFree, e.g. the
    // revolver cylinder is open), the left hand moves away along an arc and relaxes its fingers.
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
        bool warnedLegacy;

        public HandGripProfile Profile => profile;
        [ShowNativeProperty] public bool LeftHandOnGrip => leftTarget01 > 0.5f;

        float LeftT => Mathf.SmoothStep(0f, 1f, leftBlend);

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
            return right ? angle : Mathf.Lerp(profile.leftAwayFingerCurl, angle, LeftT);
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
