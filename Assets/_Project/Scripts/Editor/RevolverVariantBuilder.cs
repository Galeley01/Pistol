using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.Items;

namespace Pistol.EditorTools
{
    // Builds Revolver_Stainless: a prefab variant of Revolver with polished stainless steel materials
    // (same mechanics, other look), and puts it into the hands in the open scene (ItemHolderWithPose > Start Item).
    // Menu: Pistol > Build Revolver Stainless. Running it again rebuilds the variant with the same materials.
    public static class RevolverVariantBuilder
    {
        const string BasePrefab = "Assets/_Project/Weapons/Revolver/Revolver.prefab";
        const string VariantPath = "Assets/_Project/Weapons/Revolver/Revolver_Stainless.prefab";
        const string Materials = "Assets/_Project/Weapons/Revolver/Materials/";

        // Which material goes on which part of the model
        static readonly Dictionary<string, string> PartMaterials = new Dictionary<string, string>
        {
            { "Barrel", "M_Revolver_Polished" },
            { "Frame", "M_Revolver_Polished" },
            { "Cylinder", "M_Revolver_Polished" },
            { "Crane", "M_Revolver_Polished" },
            { "Ejector", "M_Revolver_Polished" },
            { "EjectorRod", "M_Revolver_Polished" },
            { "Hammer", "M_Revolver_Satin" },
            { "Trigger", "M_Revolver_Satin" },
            { "Grip", "M_Revolver_Rubber" },
        };

        [MenuItem("Pistol/Build Revolver Stainless")]
        static void Build()
        {
            if (Application.isPlaying) { Debug.LogWarning("Build the variant outside Play Mode."); return; }
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
            if (basePrefab == null) { Debug.LogError("Revolver prefab not found: " + BasePrefab); return; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                int painted = 0;
                foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!PartMaterials.TryGetValue(r.gameObject.name, out var matName)) continue;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(Materials + matName + ".mat");
                    if (mat == null) { Debug.LogError("Material not found: " + matName); return; }
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    painted++;
                }
                instance.name = "Revolver_Stainless";
                var variant = PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
                Debug.Log($"Revolver_Stainless built ({VariantPath}), {painted} parts repainted.", variant);

                // Put it into the hands in the open scene
                var holder = Object.FindFirstObjectByType<ItemHolderWithPose>();
                if (holder != null && variant != null)
                {
                    var so = new SerializedObject(holder);
                    so.FindProperty("startItem").objectReferenceValue = variant.GetComponent<HeldItem>();
                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(holder.gameObject.scene);
                    Debug.Log("ItemHolderWithPose > Start Item = Revolver_Stainless. Save the scene (Cmd+S) to keep it.", holder);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
