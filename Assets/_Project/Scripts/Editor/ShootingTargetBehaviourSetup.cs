using System;
using UnityEditor;
using UnityEngine;
using Pistol.Targets;

namespace Pistol.EditorTools
{
    // Puts the target behaviours onto the shooting target prefabs (built by ShootingTargetPrefabBuilder):
    // hinged plates with their starting axes and angles, steel targets, the shared impact sound.
    // Menu: Pistol > Setup Shooting Target Behaviours. A part that already has its component is skipped,
    // so settings tuned by hand stay; remove a component to set it up again.
    public static class ShootingTargetBehaviourSetup
    {
        const string PrefabDir = "Assets/ThirdParty/ShootingTargetSet/Prefabs/";
        const string ImpactPath = "Assets/_Project/Targets/Steel_Impact.asset";

        class PlateRule
        {
            public string prefix;
            public HingedPlate.Kind kind;
            public Vector3 axis;
            public float angle;
            public HingedPlate.Direction direction;
        }

        static readonly PlateRule[] Plates =
        {
            new PlateRule { prefix = "PlateRack_Plate_", kind = HingedPlate.Kind.Fall, axis = Vector3.right, angle = 85f, direction = HingedPlate.Direction.TowardStand },
            new PlateRule { prefix = "HostageSteel_Target", kind = HingedPlate.Kind.Fall, axis = Vector3.forward, angle = 90f, direction = HingedPlate.Direction.OverTheTop },
            new PlateRule { prefix = "DuelTree_Target_", kind = HingedPlate.Kind.Flip, axis = Vector3.forward, angle = 138f, direction = HingedPlate.Direction.OverTheTop },
            new PlateRule { prefix = "SpinningPlateRack_Plate_", kind = HingedPlate.Kind.Spin, axis = Vector3.right, angle = 180f, direction = HingedPlate.Direction.PushedByBullet },
        };

        // Parts whose box collider covers more than the part itself: the plate rack reset arm's box stood
        // in front of all six plates and caught every bullet, the Texas star arm's box covers its plates. They get a collider of their real shape.
        static readonly string[] MeshColliderParts = { "PlateRack_ResetArm", "TexasStar_Arm" };

        // One-piece steel targets: the component goes on the root
        static readonly string[] SteelRoots = { "Steel10Inch", "Steel66" };
        // Parts that for now only ring (the gong plate swings later, step 4)
        static readonly string[] SteelParts = { "GongTarget_Plate" };

        [MenuItem("Pistol/Setup Shooting Target Behaviours")]
        static void Setup()
        {
            if (Application.isPlaying) { Debug.LogWarning("Set up the targets outside Play Mode."); return; }
            var impact = AssetDatabase.LoadAssetAtPath<ImpactSoundProfile>(ImpactPath);
            if (impact == null) Debug.LogWarning("Impact sound profile not found: " + ImpactPath + " (targets will be silent).");

            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int before = added;
                    foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                        if (Array.IndexOf(MeshColliderParts, part.name) >= 0 && part.GetComponent<BoxCollider>() != null)
                        {
                            UnityEngine.Object.DestroyImmediate(part.GetComponent<BoxCollider>());
                            part.gameObject.AddComponent<MeshCollider>();
                            added++;
                            Debug.Log($"Target behaviours: {part.name} box collider replaced with a mesh collider");
                        }

                    if (Array.IndexOf(SteelRoots, root.name) >= 0)
                        added += Add<SteelTarget>(root, impact, null);
                    foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                        if (Array.IndexOf(SteelParts, part.name) >= 0)
                            added += Add<SteelTarget>(part.gameObject, impact, null);

                    foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                        foreach (var rule in Plates)
                            if (part.name.StartsWith(rule.prefix, StringComparison.Ordinal))
                                added += Add<HingedPlate>(part.gameObject, impact,
                                    p => p.Configure(rule.kind, rule.axis, rule.angle, rule.direction));

                    if (added > before)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log($"Target behaviours: {path} (+{added - before})");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Debug.Log($"Target behaviours: {added} components added. Existing ones were left as they are.");
        }

        static int Add<T>(GameObject go, ImpactSoundProfile impact, Action<T> configure) where T : ShootingTarget
        {
            if (go.GetComponent<T>() != null) return 0;
            var c = go.AddComponent<T>();
            configure?.Invoke(c);
            var so = new SerializedObject(c);
            so.FindProperty("impactSound").objectReferenceValue = impact;
            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }
    }
}
