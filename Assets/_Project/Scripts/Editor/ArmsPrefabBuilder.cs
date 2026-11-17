using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using NaughtyAttributes.Editor;
using Pistol.Arms;

namespace Pistol.EditorTools
{
    // Inspector of ArmsDefinition: the usual one plus the "Build arms prefab" button
    [CustomEditor(typeof(ArmsDefinition))]
    public class ArmsDefinitionEditor : NaughtyInspector
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Собрать префаб рук", GUILayout.Height(28)))
                    ArmsPrefabBuilder.Build((ArmsDefinition)target);
            }
            if (Application.isPlaying) EditorGUILayout.HelpBox("Собирается вне Play Mode.", MessageType.Info);
        }
    }

    // Builds the arms prefab of a skin from its ArmsDefinition: finds the bones by name (only here),
    // adds the IK rig (RigBuilder, Rig, Two Bone IK per arm) and an ArmsRig with direct references,
    // captures the finger rest pose and bend axes and the anatomical hand frame of each hand,
    // and saves everything as a prefab variant of the model.
    public static class ArmsPrefabBuilder
    {
        // Used only when the definition is not saved as an asset yet; normally the prefab goes next to it
        const string FallbackFolder = "Assets/_Project/Arms";
        static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };

        public static void Build(ArmsDefinition def)
        {
            if (def.model == null) { Debug.LogError($"{def.name}: assign the arms model first.", def); return; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(def.model);
            try
            {
                var root = instance.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;

                var bones = new Dictionary<string, Transform>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (!bones.ContainsKey(t.name)) bones.Add(t.name, t);

                var missing = new List<string>();
                Transform Bone(string boneName, string role)
                {
                    if (!string.IsNullOrEmpty(boneName) && bones.TryGetValue(boneName, out var t)) return t;
                    missing.Add($"{role} ({(string.IsNullOrEmpty(boneName) ? "no name" : boneName)})");
                    return null;
                }

                // ---------- Arms ----------
                var arms = new ArmsRig.Arm[2];
                for (int s = 0; s < 2; s++)
                {
                    bool right = s == 0;
                    var names = def.Side(right);
                    string side = right ? "right" : "left";
                    arms[s] = new ArmsRig.Arm
                    {
                        upperArm = Bone(names.upperArm, side + " upper arm"),
                        forearm = Bone(names.forearm, side + " forearm"),
                        hand = Bone(names.hand, side + " hand"),
                    };
                }

                // ---------- Fingers: joint bones ----------
                var fingerBones = new Transform[2, 5, 3];
                for (int s = 0; s < 2; s++)
                    for (int f = 0; f < 5; f++)
                        for (int j = 0; j < 3; j++)
                            fingerBones[s, f, j] = Bone(def.Side(s == 0).Finger(f)[j], $"{(s == 0 ? "right" : "left")} {FingerNames[f]} {j + 1}");

                if (missing.Count > 0)
                {
                    Debug.LogError($"{def.name}: bones not found in {def.model.name}: {string.Join(", ", missing)}. Check the names in the ArmsDefinition.", def);
                    return;
                }

                // ---------- Anatomical hand frame (Z to the fingers, Y where the palm faces) ----------
                var palm = new Vector3[2];
                for (int s = 0; s < 2; s++)
                {
                    bool right = s == 0;
                    var hand = arms[s].hand;
                    Vector3 toFingers = (fingerBones[s, 2, 0].position - hand.position).normalized;            // middle 1
                    Vector3 across = (fingerBones[s, 1, 0].position - fingerBones[s, 4, 0].position).normalized; // index 1 - pinky 1
                    palm[s] = (right ? Vector3.Cross(toFingers, across) : Vector3.Cross(across, toFingers)).normalized;
                    Quaternion frame = Quaternion.LookRotation(toFingers, palm[s]);
                    arms[s].handFrame = Quaternion.Inverse(hand.rotation) * frame;
                }

                // ---------- Fingers: rest pose and bend axes ----------
                var joints = new List<ArmsRig.FingerJoint>();
                for (int s = 0; s < 2; s++)
                {
                    Vector3 palmRest = def.palmsDownInRest ? -root.up : palm[s];
                    for (int f = 0; f < 5; f++)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            var bone = fingerBones[s, f, j];
                            var next = j < 2 ? fingerBones[s, f, j + 1] : null;
                            var prev = j > 0 ? fingerBones[s, f, j - 1] : null;
                            Vector3 dir = next != null ? next.position - bone.position
                                        : prev != null ? bone.position - prev.position : Vector3.zero;
                            Vector3 axisWorld = Vector3.Cross(dir.normalized, palmRest);
                            if (axisWorld.sqrMagnitude < 1e-6f) continue;
                            joints.Add(new ArmsRig.FingerJoint
                            {
                                bone = bone,
                                restLocal = bone.localRotation,
                                axis = Quaternion.Inverse(bone.rotation) * axisWorld.normalized,
                                right = s == 0,
                                finger = f
                            });
                        }
                    }
                }

                // ---------- IK rig ----------
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // the IK must run even when the arms are off screen
                // IK moves the bones far from the bind pose: keep the skin bounds updated so the arms never vanish
                foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
                var rigBuilder = instance.GetComponent<RigBuilder>();
                if (rigBuilder == null) rigBuilder = instance.AddComponent<RigBuilder>();
                rigBuilder.enabled = false;   // ArmsRig enables it once it has an item to hold
                rigBuilder.layers.Clear();

                var rigGo = new GameObject("HandsRig");
                rigGo.transform.SetParent(root, false);
                var rig = rigGo.AddComponent<Rig>();
                rigBuilder.layers.Add(new RigLayer(rig, true));

                for (int s = 0; s < 2; s++)
                {
                    var ikGo = new GameObject(s == 0 ? "RightHandIK" : "LeftHandIK");
                    ikGo.transform.SetParent(rigGo.transform, false);
                    var ik = ikGo.AddComponent<TwoBoneIKConstraint>();
                    ik.weight = 1f;
                    ik.data.root = arms[s].upperArm;
                    ik.data.mid = arms[s].forearm;
                    ik.data.tip = arms[s].hand;
                    ik.data.targetPositionWeight = 1f;
                    ik.data.targetRotationWeight = 1f;
                    ik.data.hintWeight = 1f;
                    ik.data.maintainTargetPositionOffset = false;
                    ik.data.maintainTargetRotationOffset = false;
                    arms[s].ik = ik;
                }

                var armsRig = instance.GetComponent<ArmsRig>();
                if (armsRig == null) armsRig = instance.AddComponent<ArmsRig>();
                armsRig.SetBuiltRig(def, arms[0], arms[1], joints);

                // ---------- Save ----------
                // The arms prefab lives next to its definition: one folder per arms skin
                string defPath = AssetDatabase.GetAssetPath(def);
                string folder = string.IsNullOrEmpty(defPath) ? FallbackFolder : System.IO.Path.GetDirectoryName(defPath).Replace('\\', '/');
                EnsureFolder(folder);
                string path = $"{folder}/{def.name}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                if (saved == null) { Debug.LogError($"{def.name}: failed to save {path}.", def); return; }

                Undo.RecordObject(def, "Build Arms Prefab");
                def.prefab = saved;
                EditorUtility.SetDirty(def);

                if (def.convertLegacyGrips) ConvertLegacyGrips(arms[0].handFrame, arms[1].handFrame);
                AssetDatabase.SaveAssets();
                Debug.Log($"{def.name}: arms prefab built ({path}), {joints.Count} finger joints.", saved);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // Old grip profiles stored the rotation of this skin's hand bone; now it is the anatomical hand:
        // anatomical = bone * handFrame. The hand ends up exactly where it was.
        static void ConvertLegacyGrips(Quaternion rightFrame, Quaternion leftFrame)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:HandGripProfile"))
            {
                var p = AssetDatabase.LoadAssetAtPath<HandGripProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null || p.rotationSpace != 0) continue;
                Undo.RecordObject(p, "Convert Grip");
                p.right.wristRotation = Normalize((Quaternion.Euler(p.right.wristRotation) * rightFrame).eulerAngles);
                p.left.wristRotation = Normalize((Quaternion.Euler(p.left.wristRotation) * leftFrame).eulerAngles);
                p.leftAwayRotationOffset = Normalize((Quaternion.Inverse(leftFrame) * Quaternion.Euler(p.leftAwayRotationOffset) * leftFrame).eulerAngles);
                p.rotationSpace = 1;
                EditorUtility.SetDirty(p);
                Debug.Log($"Grip profile {p.name} converted to the anatomical hand format.", p);
            }
        }

        static Vector3 Normalize(Vector3 e) =>
            new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
