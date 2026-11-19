using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.PostFx;

namespace Pistol.EditorTools
{
    // Puts concrete on the white blocks of the range (the walls, the platform and the box: scene objects named
    // "Cube..."), with Unity's Standard shader and the "cement arcing pattern" set (colour, normal, ambient occlusion).
    // The blocks are stretched cubes, so each gets its own copy of the cube mesh with UVs in metres (box mapping
    // by the block's size): the texture keeps its real size on every face, nothing is stretched. The lightmap UVs
    // of the cube are kept. Meshes are saved in Environment/Meshes/RangeBlock_<name>.asset; run the menu again after
    // resizing a block. LookSwitcher gets the list, so key 4 turns them white again.
    // The material's tint and smoothness are kept once tuned. Re-bake the light afterwards.
    // Menu: Pistol > Apply Range Concrete. Save the scene afterwards.
    public static class RangeConcreteSetup
    {
        const string MaterialPath = "Assets/_Project/Environment/M_Range_Concrete.mat";
        const string MeshDir = "Assets/_Project/Environment/Meshes";
        const string Tex = "Assets/_Project/Environment/Concrete/cement_arcing_pattern1_";
        const float TileSize = 2f;   // metres of wall per texture repeat

        [MenuItem("Pistol/Apply Range Concrete")]
        static void Apply()
        {
            if (Application.isPlaying) { Debug.LogWarning("Apply outside Play Mode."); return; }

            Import(Tex + "albedo.png", TextureImporterType.Default, true);
            Import(Tex + "Normal-ogl.png", TextureImporterType.NormalMap, false);
            Import(Tex + "ao.png", TextureImporterType.Default, false);

            var material = SetUpMaterial();
            if (material == null) return;

            var defaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            var blocks = new List<Renderer>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.gameObject.name.StartsWith("Cube")) continue;
                    if (r.sharedMaterial != defaultMaterial && r.sharedMaterial != material) continue;
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null) continue;
                    Undo.RecordObject(r, "Range concrete");
                    Undo.RecordObject(filter, "Range concrete");
                    filter.sharedMesh = BlockMesh(r.gameObject.name, filter.sharedMesh, r.transform.lossyScale);
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
            Debug.Log($"Concrete on {blocks.Count} blocks (Standard shader, UVs in metres). Key 4 turns them white again. " +
                      "Save the scene and re-bake the light (Pistol > Bake Lighting).");
        }

        // Standard, not metal (concrete), smoothness from the material, the texture repeat every TileSize metres
        static Material SetUpMaterial()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) { Debug.LogError("Standard shader not found."); return null; }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(shader) { name = "M_Range_Concrete" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else Undo.RecordObject(material, "Range concrete");

            float smoothness = material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.2f;
            Color tint = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            material.shader = shader;
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "albedo.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Normal-ogl.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "ao.png"));
            material.SetTexture("_MetallicGlossMap", null);   // its alpha would read as full smoothness in Standard
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", fresh ? 0.2f : smoothness);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetColor("_Color", fresh ? Color.white : tint);
            material.mainTextureScale = Vector2.one / TileSize;   // UVs are in metres
            material.mainTextureOffset = Vector2.zero;
            EditorUtility.SetDirty(material);
            return material;
        }

        // A copy of the cube with UVs in metres: each face is mapped by its own two axes, scaled by the block's size.
        // The source is read from the built-in cube when the block already has its own mesh (re-run after a resize).
        static Mesh BlockMesh(string objectName, Mesh current, Vector3 scale)
        {
            var source = current != null && AssetDatabase.GetAssetPath(current).StartsWith(MeshDir)
                ? Resources.GetBuiltinResource<Mesh>("Cube.fbx")
                : current;
            if (source == null) return current;

            var verts = source.vertices;
            var normals = source.normals;
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = Vector3.Scale(verts[i], scale);
                Vector3 n = normals[i];
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                uv[i] = ax >= ay && ax >= az ? new Vector2(p.z * Mathf.Sign(n.x), p.y)
                      : ay >= az ? new Vector2(p.x, p.z * Mathf.Sign(n.y))
                      : new Vector2(-p.x * Mathf.Sign(n.z), p.y);
            }

            EditorAssets.EnsureFolder(MeshDir);
            string safe = string.Join("_", objectName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "").Replace("(", "_").Replace(")", "");
            string path = $"{MeshDir}/RangeBlock_{safe}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh();
            mesh.Clear();
            mesh.name = $"RangeBlock_{safe}";
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.tangents = source.tangents;
            mesh.uv = uv;
            mesh.uv2 = source.uv2 != null && source.uv2.Length == verts.Length ? source.uv2 : source.uv;   // lightmap UVs of the cube
            mesh.triangles = source.triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();   // the UVs changed, so the normal map needs new tangents
            if (fresh) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void Import(string path, TextureImporterType type, bool srgb) =>
            EditorAssets.ImportTexture(path, type, srgb, wrap: TextureWrapMode.Repeat);
    }
}
