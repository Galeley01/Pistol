using System.IO;
using UnityEditor;
using UnityEngine;
using Pistol.Ballistics;
using Pistol.Weapons.Revolver;

namespace Pistol.EditorTools
{
    // Menu: Tools > Revolver (see the MenuItem attribute below)
    // Finds Cartridge_357.fbx in the project and in one click:
    //  - sets up texture import (normal map, sRGB off, alpha from texture),
    //  - creates the M_Cartridge357 material (Standard: Albedo, Metallic+Smoothness, Normal, Occlusion),
    //  - assigns it to the model,
    //  - creates the Ammo_357 cartridge asset and assigns it to the RevolverCylinder in the scene.
    public static class AmmoSetup
    {
        [MenuItem("Tools/Revolver/Настроить патрон .357")]
        static void Setup()
        {
            var guids = AssetDatabase.FindAssets("Cartridge_357 t:Model");
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Патрон .357", "Не найден Cartridge_357.fbx. Положите папку Ammo357 в Assets.", "ОК");
                return;
            }
            string fbxPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');

            // ---------- Textures ----------
            var baseCol = Texture(dir + "/Bullet357_BaseColor.png", srgb: true, normal: false, alphaFromInput: false);
            var normal  = Texture(dir + "/Bullet357_Normal.png", srgb: false, normal: true, alphaFromInput: false);
            var ms      = Texture(dir + "/Bullet357_MetallicSmoothness.png", srgb: false, normal: false, alphaFromInput: true);
            var ao      = Texture(dir + "/Bullet357_Occlusion.png", srgb: false, normal: false, alphaFromInput: false);

            // ---------- Material (Built-in, Standard) ----------
            string matPath = dir + "/M_Cartridge357.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_MainTex", baseCol);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            if (ms != null)
            {
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);   // Smoothness from the Metallic map alpha
                mat.SetFloat("_GlossMapScale", 1f);
            }
            if (ao != null) mat.SetTexture("_OcclusionMap", ao);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            // ---------- Model: scale 1 (already in meters) and our material ----------
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is Material m)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
            mi.SaveAndReimport();

            // ---------- Cartridge asset ----------
            string ammoPath = dir + "/Ammo_357.asset";
            var ammo = AssetDatabase.LoadAssetAtPath<AmmoType>(ammoPath);
            if (ammo == null)
            {
                ammo = ScriptableObject.CreateInstance<AmmoType>();
                AssetDatabase.CreateAsset(ammo, ammoPath);
            }
            ammo.livePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            ammo.modelRotation = Vector3.zero;
            ammo.modelScale = 0.95f;
            EditorUtility.SetDirty(ammo);
            AssetDatabase.SaveAssets();

            // ---------- Assign to the cylinder in the scene ----------
            var cyl = Object.FindFirstObjectByType<RevolverCylinder>();
            if (cyl != null)
            {
                Undo.RecordObject(cyl, "Assign Ammo");
                cyl.ammoType = ammo;
                if (Mathf.Abs(cyl.chamberRadius - 0.0102f) < 0.00001f) cyl.chamberRadius = 0.0100f;
                EditorUtility.SetDirty(cyl);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cyl.gameObject.scene);
            }

            Selection.activeObject = ammo;
            Debug.Log("Cartridge .357 set up: material, model and Ammo_357" + (cyl != null ? " (assigned to the cylinder)." : ". Assign it to RevolverCylinder manually."));
        }

        static Texture2D Texture(string path, bool srgb, bool normal, bool alphaFromInput)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("Texture not found: " + path); return null; }
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.alphaSource = alphaFromInput ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = false;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
