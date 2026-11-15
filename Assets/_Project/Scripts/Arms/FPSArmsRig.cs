using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using NaughtyAttributes;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Pistol.Weapons.Revolver;

namespace Pistol.Arms
{
    // The arms hold the weapon through Animation Rigging (Two Bone IK).
    // Goes on FPS_Arms (the arms root, next to the Animator).
    // 1) "Build rig" button (outside Play Mode) creates the RigBuilder, IK for both arms,
    //    hand and elbow targets on the weapon, and the grip profile.
    // 2) In Play Mode the arms grab the weapon. The grip is tuned in the Grip Profile, live while playing.
    [RequireComponent(typeof(Animator))]
    public class FPSArmsRig : MonoBehaviour
    {
        [BoxGroup("Оружие"), Required("Перетащите сюда Revolver")]
        public Transform weapon;

        [BoxGroup("Оружие"), Expandable, Tooltip("Как руки держат оружие. Раскройте, чтобы править")]
        public HandGripProfile gripProfile;

        [BoxGroup("Оружие"), Tooltip("Вкл: цели рук каждый кадр берутся из профиля (правка цифрами).\n" +
                                      "Выкл: цели можно двигать мышкой в Scene, потом нажать «Записать положение целей в профиль».")]
        [ValidateInput("IsLive", "Выключено: изменения в Grip Profile сейчас НЕ применяются. Включите, чтобы править хват цифрами.")]
        public bool liveFromProfile = true;

        bool IsLive(bool value) => value;

        [BoxGroup("Положение рук"), Tooltip("Где корень рук относительно камеры. Применяется кнопкой «Собрать риг»")]
        public Vector3 armsLocalPosition = new Vector3(0f, -0.28f, -0.08f);

        [Foldout("Служебное (заполняется кнопкой)"), ReadOnly] public TwoBoneIKConstraint rightIK;
        [Foldout("Служебное (заполняется кнопкой)"), ReadOnly] public TwoBoneIKConstraint leftIK;
        [Foldout("Служебное (заполняется кнопкой)"), ReadOnly] public Transform rightTarget;
        [Foldout("Служебное (заполняется кнопкой)"), ReadOnly] public Transform leftTarget;
        [Foldout("Служебное (заполняется кнопкой)"), ReadOnly] public Transform rightHint;
        [Foldout("Служебное (заполняется кнопкой)"), ReadOnly] public Transform leftHint;

        [System.Serializable]
        class FingerJoint
        {
            public Transform bone;
            public Quaternion restLocal;
            public Vector3 axis;     // bend axis in the bone's local space
            public bool right;
            public int finger;       // 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky
        }
        [SerializeField, HideInInspector] List<FingerJoint> joints = new List<FingerJoint>();

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        // Initial hand directions in weapon space (X right, Y up, Z forward)
        static readonly Vector3 RightFingerDir = new Vector3(-0.15f, -0.35f, 0.92f);
        static readonly Vector3 RightPalmDir   = new Vector3(-0.95f, 0.10f, 0.25f);  // palm faces left, onto the grip
        static readonly Vector3 LeftFingerDir  = new Vector3(0.45f, -0.35f, 0.80f);
        static readonly Vector3 LeftPalmDir    = new Vector3(0.90f, 0.30f, -0.20f);  // palm faces right, onto the right hand

        [ShowNativeProperty] public bool LeftHandOnGrip => leftTarget01 > 0.5f;

        float leftBlend = 1f;      // 1 = left hand on the grip, 0 = moved away
        float leftTarget01 = 1f;

        // ================= Runtime =================
        void Start()
        {
            var revolver = weapon != null ? weapon.GetComponent<RevolverFire>() : null;
            if (revolver != null)
            {
                revolver.CylinderOpening += () => SetLeftHandOnGrip(false);
                revolver.CylinderClosing += () => SetLeftHandOnGrip(true);
            }
        }

        // Smoothly move the left hand away (false) or back onto the grip (true)
        public void SetLeftHandOnGrip(bool onGrip) => leftTarget01 = onGrip ? 1f : 0f;

        void Update()
        {
            float time = gripProfile != null ? gripProfile.leftHandMoveTime : 0.45f;
            leftBlend = Mathf.MoveTowards(leftBlend, leftTarget01, Time.deltaTime / Mathf.Max(time, 0.01f));
            if (liveFromProfile) ApplyProfileToTargets();
        }

        float LeftT => Mathf.SmoothStep(0f, 1f, leftBlend);

        void LateUpdate()
        {
            ApplyFingers();
        }

        void ApplyProfileToTargets()
        {
            if (gripProfile == null || rightTarget == null || leftTarget == null) return;
            SetLocal(rightTarget, gripProfile.right.wristPosition, gripProfile.right.wristRotation);
            if (rightHint) rightHint.localPosition = gripProfile.right.elbowHint;

            // Left hand: blend between "on grip" and "away", arcing outwards halfway
            var p = gripProfile;
            float t = LeftT;
            Vector3 arc = new Vector3(-1f, 0.3f, -0.5f).normalized * (Mathf.Sin(t * Mathf.PI) * p.leftHandArc);
            Quaternion gripRot = Quaternion.Euler(p.left.wristRotation);
            Quaternion awayRot = gripRot * Quaternion.Euler(p.leftAwayRotationOffset);
            leftTarget.localPosition = Vector3.Lerp(p.leftAwayPosition, p.left.wristPosition, t) + arc;
            leftTarget.localRotation = Quaternion.Slerp(awayRot, gripRot, t);
            if (leftHint) leftHint.localPosition = Vector3.Lerp(p.leftAwayElbowHint, p.left.elbowHint, t);
        }

        static Vector3 Normalize(Vector3 e) =>
            new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));

        static void SetLocal(Transform t, Vector3 pos, Vector3 euler)
        {
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
        }

        void ApplyFingers()
        {
            if (gripProfile == null || !Application.isPlaying) return;
            foreach (var j in joints)
            {
                if (j.bone == null) continue;
                var pose = j.right ? gripProfile.right : gripProfile.left;
                float angle = j.finger switch
                {
                    0 => pose.thumb, 1 => pose.index, 2 => pose.middle, 3 => pose.ring, _ => pose.pinky
                };
                if (!j.right) angle = Mathf.Lerp(gripProfile.leftAwayFingerCurl, angle, LeftT);
                j.bone.localRotation = j.restLocal * Quaternion.AngleAxis(angle, j.axis);
            }
        }

        // ================= Buttons =================
        [Button("1. Собрать риг рук (вне Play Mode)")]
        void BuildRig()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) { Debug.LogWarning("Build the rig outside Play Mode: the arms must be in their rest pose."); return; }
            if (weapon == null)
            {
                var gun = FindFirstObjectByType<RevolverFire>();
                if (gun != null) weapon = gun.transform;
            }
            if (weapon == null) { Debug.LogError("No weapon found. Drag the Revolver into the Weapon field."); return; }

            Undo.RegisterFullObjectHierarchyUndo(gameObject, "Build Arms Rig");
            transform.localPosition = armsLocalPosition;
            transform.localRotation = Quaternion.identity;

            // RigBuilder + Rig
            var rigBuilder = GetComponent<RigBuilder>();
            if (rigBuilder == null) rigBuilder = Undo.AddComponent<RigBuilder>(gameObject);
            var rigT = GetOrCreateChild(transform, "HandsRig");
            var rig = rigT.GetComponent<Rig>();
            if (rig == null) rig = Undo.AddComponent<Rig>(rigT.gameObject);
            bool hasLayer = false;
            foreach (var l in rigBuilder.layers) if (l.rig == rig) hasLayer = true;
            if (!hasLayer) rigBuilder.layers.Add(new RigLayer(rig, true));

            // Targets on the weapon
            rightTarget = GetOrCreateChild(weapon, "IK_RightHand");
            leftTarget  = GetOrCreateChild(weapon, "IK_LeftHand");
            rightHint   = GetOrCreateChild(weapon, "IK_RightElbow");
            leftHint    = GetOrCreateChild(weapon, "IK_LeftElbow");

            rightIK = SetupIK(rigT, "RightHandIK", "R", rightTarget, rightHint);
            leftIK  = SetupIK(rigT, "LeftHandIK", "L", leftTarget, leftHint);
            if (rightIK == null || leftIK == null) return;

            CaptureFingers();

            if (gripProfile == null)
            {
                gripProfile = CreateProfileAsset();
                FillStartRotations(gripProfile);
            }
            ApplyProfileToTargets();

            EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("Arms rig built. Press Play and the arms will grab the weapon.");
#endif
        }

        [Button("Записать положение целей в профиль")]
        void SaveTargetsToProfile()
        {
            if (gripProfile == null || rightTarget == null) { Debug.LogWarning("Build the rig first."); return; }
            gripProfile.right.wristPosition = rightTarget.localPosition;
            gripProfile.right.wristRotation = Normalize(rightTarget.localEulerAngles);
            gripProfile.right.elbowHint = rightHint.localPosition;
            gripProfile.left.wristPosition = leftTarget.localPosition;
            gripProfile.left.wristRotation = Normalize(leftTarget.localEulerAngles);
            gripProfile.left.elbowHint = leftHint.localPosition;
#if UNITY_EDITOR
            EditorUtility.SetDirty(gripProfile);
            AssetDatabase.SaveAssets();
#endif
            Debug.Log("Target positions saved to profile " + gripProfile.name);
        }

        [Button("Сбросить хват к стартовому (вне Play Mode)")]
        void ResetProfile()
        {
#if UNITY_EDITOR
            if (Application.isPlaying || gripProfile == null || weapon == null) { Debug.LogWarning("Requires: outside Play Mode, rig built."); return; }
            if (!EditorUtility.DisplayDialog("Сбросить хват?", "Профиль вернётся к стартовым значениям.", "Сбросить", "Отмена")) return;
            Undo.RecordObject(gripProfile, "Reset Grip");
            var fresh = ScriptableObject.CreateInstance<HandGripProfile>();
            gripProfile.right = fresh.right; gripProfile.left = fresh.left;
            DestroyImmediate(fresh);
            FillStartRotations(gripProfile);
            ApplyProfileToTargets();
            EditorUtility.SetDirty(gripProfile);
#endif
        }

        // ================= Helpers =================
        Transform FindBone(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        static Transform GetOrCreateChild(Transform parent, string childName)
        {
            var t = parent.Find(childName);
            if (t != null) return t;
            t = new GameObject(childName).transform;
            t.SetParent(parent, false);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(t.gameObject, "Create " + childName);
#endif
            return t;
        }

        TwoBoneIKConstraint SetupIK(Transform rigT, string ikName, string side, Transform target, Transform hint)
        {
            var upper = FindBone("UpperArm_" + side);
            var fore = FindBone("Forearm_" + side);
            var hand = FindBone("Hand_" + side);
            if (upper == null || fore == null || hand == null)
            {
                Debug.LogError($"Arm bones for side {side} not found (UpperArm_{side}, Forearm_{side}, Hand_{side}). The script must be on FPS_Arms.");
                return null;
            }
            var t = GetOrCreateChild(rigT, ikName);
            var ik = t.GetComponent<TwoBoneIKConstraint>();
            if (ik == null) ik = t.gameObject.AddComponent<TwoBoneIKConstraint>();
            ik.weight = 1f;
            ik.data.root = upper;
            ik.data.mid = fore;
            ik.data.tip = hand;
            ik.data.target = target;
            ik.data.hint = hint;
            ik.data.targetPositionWeight = 1f;
            ik.data.targetRotationWeight = 1f;
            ik.data.hintWeight = 1f;
            ik.data.maintainTargetPositionOffset = false;
            ik.data.maintainTargetRotationOffset = false;
            return ik;
        }

        // Capture the finger rest pose and each joint's bend axis (arms in T-pose, palms down)
        void CaptureFingers()
        {
            joints.Clear();
            Vector3 palmRest = -transform.up;
            foreach (var side in new[] { "R", "L" })
            {
                for (int f = 0; f < FingerNames.Length; f++)
                {
                    Transform prev = null;
                    for (int k = 1; k <= 3; k++)
                    {
                        var bone = FindBone($"{FingerNames[f]}{k}_{side}");
                        if (bone == null) continue;
                        var next = FindBone($"{FingerNames[f]}{k + 1}_{side}");
                        Vector3 dir = next != null ? next.position - bone.position
                                    : prev != null ? bone.position - prev.position : Vector3.zero;
                        Vector3 axisWorld = Vector3.Cross(dir.normalized, palmRest);
                        prev = bone;
                        if (axisWorld.sqrMagnitude < 1e-6f) continue;
                        joints.Add(new FingerJoint
                        {
                            bone = bone,
                            restLocal = bone.localRotation,
                            axis = Quaternion.Inverse(bone.rotation) * axisWorld.normalized,
                            right = side == "R",
                            finger = f
                        });
                    }
                }
            }
        }

        // Compute the initial hand rotations from finger and palm directions
        void FillStartRotations(HandGripProfile p)
        {
            p.right.wristRotation = HandRotation("R", RightFingerDir, RightPalmDir).eulerAngles;
            p.left.wristRotation = HandRotation("L", LeftFingerDir, LeftPalmDir).eulerAngles;
        }

        Quaternion HandRotation(string side, Vector3 fingerDirLocal, Vector3 palmDirLocal)
        {
            var hand = FindBone("Hand_" + side);
            var mid = FindBone("Middle1_" + side);
            if (hand == null || mid == null) return Quaternion.identity;
            Quaternion restFrame = Quaternion.LookRotation((mid.position - hand.position).normalized, -transform.up);
            Quaternion wantFrame = Quaternion.LookRotation(weapon.TransformDirection(fingerDirLocal.normalized),
                                                           weapon.TransformDirection(palmDirLocal.normalized));
            Quaternion handWorld = wantFrame * Quaternion.Inverse(restFrame) * hand.rotation;
            return Quaternion.Inverse(weapon.rotation) * handWorld;
        }

#if UNITY_EDITOR
        HandGripProfile CreateProfileAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Pistol")) AssetDatabase.CreateFolder("Assets", "Pistol");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Pistol/Revolver_HandGrip.asset");
            var p = ScriptableObject.CreateInstance<HandGripProfile>();
            AssetDatabase.CreateAsset(p, path);
            AssetDatabase.SaveAssets();
            return p;
        }
#endif

        // Draw the hand targets in the Scene view
        void OnDrawGizmos()
        {
            DrawTarget(rightTarget, new Color(0.2f, 1f, 0.3f));
            DrawTarget(leftTarget, new Color(0.3f, 0.6f, 1f));
            Gizmos.color = Color.yellow;
            if (rightHint) Gizmos.DrawWireSphere(rightHint.position, 0.02f);
            if (leftHint) Gizmos.DrawWireSphere(leftHint.position, 0.02f);
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
