using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.PostFx;

namespace Pistol.EditorTools
{
    // Puts concrete on the white blocks of the range (the walls, the platform and the box: scene objects named
    // "Cube..." with Unity's default material): a material on the triplanar concrete shader with the SaltFinish set
    // of the concrete pack, tinted grey like the floor. LookSwitcher gets the list, so key 4 turns them white again.
    // Menu: Pistol > Apply Range Concrete. Save the scene afterwards.
    public static class RangeConcreteSetup
    {
        const string MaterialPath = "Assets/_Project/Environment/M_Range_Concrete.mat";
        const string Tex = "Assets/ThirdParty/YughuesFreeConcreteMaterials/Textures/T_YFCM_SaltFinish_";

        [MenuItem("Pistol/Apply Range Concrete")]
        static void Apply()
        {
            if (Application.isPlaying) { Debug.LogWarning("Apply outside Play Mode."); return; }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Pistol/ConcreteTriplanar");
                if (shader == null) { Debug.LogError("Shader Pistol/ConcreteTriplanar not found (compile error?)."); return; }
                material = new Material(shader) { name = "M_Range_Concrete" };
                material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "d.tga"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "n.tga"));
                material.SetTexture("_SpecGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "s.tga"));
                material.SetColor("_Color", new Color(0.6f, 0.6f, 0.6f));   // the floor is tinted to 0.56
                material.SetFloat("_TileSize", 2.5f);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            var defaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            var blocks = new List<Renderer>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.gameObject.name.StartsWith("Cube")) continue;
                    if (r.sharedMaterial != defaultMaterial && r.sharedMaterial != material) continue;
                    Undo.RecordObject(r, "Range concrete");
                    r.sharedMaterial = material;
                    blocks.Add(r);
                }

            var look = Object.FindFirstObjectByType<LookSwitcher>();   // editor tool, once
            if (look != null)
            {
                Undo.RecordObject(look, "Range concrete");
                look.SetRestyled(blocks.ToArray(), defaultMaterial);
                EditorUtility.SetDirty(look);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"Concrete on {blocks.Count} blocks. Key 4 turns them white again. Save the scene (re-bake the light for the best look).");
        }
    }
}
