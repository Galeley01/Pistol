using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.PostFx;

namespace Pistol.EditorTools
{
    // Puts concrete on the white blocks of the range (the walls, the platform and the box: scene objects named
    // "Cube..."): a material on the triplanar concrete shader with the "cement arcing pattern" set (colour, normal,
    // ambient occlusion, metallic). LookSwitcher gets the list, so key 4 turns them white again.
    // The material's textures are set every run (its tint, tile size and smoothness are kept once tuned).
    // Menu: Pistol > Apply Range Concrete. Save the scene afterwards.
    public static class RangeConcreteSetup
    {
        const string MaterialPath = "Assets/_Project/Environment/M_Range_Concrete.mat";
        const string Tex = "Assets/_Project/Environment/Concrete/cement_arcing_pattern1_";

        [MenuItem("Pistol/Apply Range Concrete")]
        static void Apply()
        {
            if (Application.isPlaying) { Debug.LogWarning("Apply outside Play Mode."); return; }

            Import(Tex + "albedo.png", TextureImporterType.Default, true);
            Import(Tex + "Normal-ogl.png", TextureImporterType.NormalMap, false);
            Import(Tex + "ao.png", TextureImporterType.Default, false);
            Import(Tex + "Metallic.psd", TextureImporterType.Default, false);

            var shader = Shader.Find("Pistol/ConcreteTriplanar");
            if (shader == null) { Debug.LogError("Shader Pistol/ConcreteTriplanar not found (compile error?)."); return; }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(shader) { name = "M_Range_Concrete" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "albedo.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Normal-ogl.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "ao.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Metallic.psd"));
            if (fresh || material.GetColor("_Color").r < 0.8f)   // the old PrecastD4 tint was a cold grey
            {
                material.SetColor("_Color", Color.white);
                material.SetFloat("_TileSize", 2f);
                material.SetFloat("_Glossiness", 0.2f);
            }
            EditorUtility.SetDirty(material);

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
            if (look != null && blocks.Count > 0)
            {
                Undo.RecordObject(look, "Range concrete");
                look.SetRestyled(blocks.ToArray(), defaultMaterial);
                EditorUtility.SetDirty(look);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"Concrete on {blocks.Count} blocks. Key 4 turns them white again. Save the scene (re-bake the light for the best look).");
        }

        static void Import(string path, TextureImporterType type, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("Texture not found: " + path); return; }
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.wrapMode == TextureWrapMode.Repeat) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }
}
