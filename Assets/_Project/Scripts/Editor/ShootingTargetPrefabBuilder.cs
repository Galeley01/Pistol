using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pistol.EditorTools
{
    // Splits ShootingTargetCollection.fbx (third-party "Shooting Target Set", CC BY 4.0) into prefabs:
    // one prefab per target. Each prefab has an empty root at the bottom centre of the
    // main part, the FBX parts inside, and colliders:
    // BoxCollider on parts that can move later (plates, swingers), MeshCollider on the rest.
    // Meshes stay references to the FBX, so re-exporting the FBX updates the prefabs.
    // Menu: Pistol > Build Shooting Target Prefabs. Existing prefabs are skipped (they may have
    // hand-made changes); delete a prefab to rebuild it.
    public static class ShootingTargetPrefabBuilder
    {
        const string Root = "Assets/ThirdParty/ShootingTargetSet/";
        const string FbxPath = Root + "Models/ShootingTargetCollection.fbx";
        const string PrefabDir = Root + "Prefabs/";

        enum Pivot { Bottom, Top }

        class Group
        {
            public string name;
            public string anchor;      // part whose position gives the root X/Z
            public string[] parts;
            public string[] moving;    // parts that get a BoxCollider
            public Pivot pivot;
        }

        static string[] Seq(string prefix, int count)
        {
            var r = new string[count];
            for (int i = 0; i < count; i++) r[i] = prefix + (i + 1).ToString("00");
            return r;
        }

        static string[] Join(params object[] items)
        {
            var list = new List<string>();
            foreach (var it in items)
            {
                if (it is string s) list.Add(s);
                else list.AddRange((string[])it);
            }
            return list.ToArray();
        }

        static Group G(string name, string anchor, string[] parts, string[] moving = null, Pivot pivot = Pivot.Bottom)
            => new Group { name = name, anchor = anchor, parts = parts, moving = moving ?? new string[0], pivot = pivot };

        static readonly Group[] Groups =
        {
            G("DuelTree", "DuelTree_Stand", Join("DuelTree_Stand", Seq("DuelTree_Target_", 6)), Seq("DuelTree_Target_", 6)),
            G("HostageSteel", "HostageSteel", Join("HostageSteel", "HostageSteel_Target"), Join("HostageSteel_Target")),
            G("PlateRack", "PlateRack_Stand", Join("PlateRack_Stand", "PlateRack_ResetArm", Seq("PlateRack_Plate_", 6)),
                Seq("PlateRack_Plate_", 6)),   // the reset arm's box would cover the plates: it gets a MeshCollider
            G("SpinningPlateRack", "SpinningPlateRack_Stand",
                Join("SpinningPlateRack_Stand", "SpinningPlateRack_Rack", Seq("SpinningPlateRack_Plate_", 8)),
                Seq("SpinningPlateRack_Plate_", 8)),
            G("TexasStar", "TexasStar_Stand", Join("TexasStar_Stand", "TexasStar_Arm", Seq("TexasStar_Plate_", 5)),
                Seq("TexasStar_Plate_", 5)),   // the arm's box would cover the plates: it gets a MeshCollider
            G("GongTarget", "GongTarget_Frame", Join("GongTarget_Frame", "GongTarget_Plate"), Join("GongTarget_Plate")),
            G("PaperTarget", "PaperTarget_Stand", Join("PaperTarget_Stand", "PaperTarget_Card")),
            G("Steel10Inch", "Steel10Inch", Join("Steel10Inch")),
            G("Steel66", "Steel66", Join("Steel66")),
        };

        // Box colliders that must not cover the whole mesh: the gong plate hangs on chains,
        // the box covers only the steel plate (local space of the part, metres; pivot = hang point).
        static readonly Dictionary<string, Bounds> BoxOverrides = new Dictionary<string, Bounds>
        {
            { "GongTarget_Plate", new Bounds(new Vector3(0f, -0.654f, 0.0105f), new Vector3(0.915f, 0.942f, 0.01f)) },
        };

        [MenuItem("Pistol/Build Shooting Target Prefabs")]
        static void Build()
        {
            if (Application.isPlaying) { Debug.LogWarning("Build the prefabs outside Play Mode."); return; }
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) { Debug.LogError("FBX not found: " + FbxPath); return; }

            // A plain copy (not a prefab instance), so parts can be moved between parents.
            var source = Object.Instantiate(fbx);
            try
            {
                var byName = new Dictionary<string, Transform>();
                foreach (Transform t in source.GetComponentsInChildren<Transform>(true))
                    if (t != source.transform) byName[t.name] = t;

                var used = new HashSet<string>();
                int built = 0, skipped = 0;
                foreach (var g in Groups)
                {
                    foreach (var p in g.parts) used.Add(p);
                    string path = PrefabDir + g.name + ".prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    {
                        Debug.Log($"Skipped {g.name}: prefab already exists (delete it to rebuild).");
                        skipped++;
                        continue;
                    }
                    if (BuildGroup(g, byName, path)) built++;
                }

                foreach (var name in byName.Keys)
                    if (!used.Contains(name)) Debug.LogWarning("FBX part not in any prefab: " + name);

                AssetDatabase.SaveAssets();
                Debug.Log($"Shooting target prefabs: {built} built, {skipped} skipped. Folder: {PrefabDir}");
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        static bool BuildGroup(Group g, Dictionary<string, Transform> byName, string path)
        {
            var parts = new List<Transform>();
            foreach (var p in g.parts)
            {
                if (!byName.TryGetValue(p, out var t)) { Debug.LogError($"{g.name}: part {p} not found in the FBX."); return false; }
                parts.Add(t);
            }
            var anchor = byName[g.anchor];

            // Root at the anchor's X/Z, at the lowest (or highest) point of the whole group.
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var t in parts)
            {
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
            var a = anchor.position;
            float y = !hasBounds ? a.y : g.pivot == Pivot.Top ? bounds.max.y : bounds.min.y;

            var root = new GameObject(g.name);
            try
            {
                root.transform.position = new Vector3(a.x, y, a.z);
                var moving = new HashSet<string>(g.moving);
                foreach (var t in parts)
                {
                    t.SetParent(root.transform, true);
                    if (t.GetComponent<MeshFilter>() == null) continue;
                    if (moving.Contains(t.name))
                    {
                        var box = t.gameObject.AddComponent<BoxCollider>();
                        if (BoxOverrides.TryGetValue(t.name, out var b)) { box.center = b.center; box.size = b.size; }
                    }
                    else t.gameObject.AddComponent<MeshCollider>();
                }
                root.transform.position = Vector3.zero;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"Built {g.name} ({parts.Count} parts): {path}", prefab);
                return prefab != null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
