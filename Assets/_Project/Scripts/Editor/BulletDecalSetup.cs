using System.Linq;
using UnityEditor;
using UnityEngine;
using Pistol.Ballistics;

namespace Pistol.EditorTools
{
    // Sets up the bullet hole styles in concrete and lists them on BulletSystem for the comparison
    // (DebugSettings > Bullet Hole Style):
    //   Photo — flat decal, an atlas of 5 photos (ActionVFX), normal map derived from them;
    //   Scan flat entry / exit — flat decals baked from the concrete scan (real relief), one per side of the hole;
    //     the exit one is the default mark;
    //   Scan crater entry / exit — 3D crater meshes cut from the scan: a stencil mask opens the wall, the crater is
    //     drawn inside it, the scan decal around it gives the dust and chips.
    // Texture and model import is set here too. Existing assets are kept (they may be tuned).
    // Menu: Pistol > Setup Bullet Decals. Save the scene afterwards (BulletSystem gets the style list).
    public static class BulletDecalSetup
    {
        const string Dir = "Assets/_Project/Weapons/Shared/FX/Decals/";
        const string TablePath = "Assets/_Project/Settings/SurfaceSounds.asset";

        [MenuItem("Pistol/Setup Bullet Decals")]
        static void Setup()
        {
            if (Application.isPlaying) { Debug.LogWarning("Set up the decals outside Play Mode."); return; }

            // A: the photo atlas
            var photo = FlatDecal("Concrete", "Concrete", 4, 2, 5, new Color(0.72f, 0.71f, 0.69f), 1.2f,
                d => { d.sizeScale = 3.5f; d.sizeJitter = 0.3f; });
            // C: flat decals baked from the scan (square side in metres = what was baked)
            var flatEntry = FlatDecal("ScanEntry", "ScanEntry", 1, 1, 1, new Color(0.85f, 0.85f, 0.84f), 1f,
                d => { d.sizeInMeters = true; d.meters = 0.112f; d.sizeJitter = 0.2f; });
            var flatExit = FlatDecal("ScanExit", "ScanExit", 1, 1, 1, new Color(0.85f, 0.85f, 0.84f), 1f,
                d => { d.sizeInMeters = true; d.meters = 0.154f; d.sizeJitter = 0.2f; });
            // B: 3D craters
            var mask = MaterialAt(Dir + "M_BulletHole_Mask.mat", "Pistol/BulletHoleMask", null);
            var craterEntry = CraterDecal("Entry", mask, flatEntry.material);
            var craterExit = CraterDecal("Exit", mask, flatExit.material);

            var table = AssetDatabase.LoadAssetAtPath<SurfaceTable>(TablePath);
            if (table != null && table.defaultDecal == null)
            {
                Undo.RecordObject(table, "Default bullet decal");
                table.defaultDecal = flatExit;   // the scan exit decal looked best in Play
                EditorUtility.SetDirty(table);
            }

            var bullets = Object.FindFirstObjectByType<BulletSystem>();   // editor tool, once
            if (bullets != null)
            {
                Undo.RecordObject(bullets, "Bullet hole styles");
                bullets.SetHoleStyles(new[] { photo, flatEntry, flatExit, craterEntry, craterExit });
                EditorUtility.SetDirty(bullets);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bullets.gameObject.scene);
            }
            else Debug.LogWarning("No BulletSystem in the open scene: the style list isn't filled.");

            AssetDatabase.SaveAssets();
            Debug.Log("Bullet hole styles set up: photo, scan flat entry/exit (exit is the default), scan crater entry/exit. " +
                      "Compare: DebugSettings > Bullet Hole Style. Save the scene.");
        }

        // ---------- Helpers ----------
        static ImpactDecal FlatDecal(string asset, string tex, int cols, int rows, int variants, Color tint, float bump,
                                     System.Action<ImpactDecal> setup)
        {
            string albedo = Dir + $"T_BulletHole_{tex}_Albedo.png", normal = Dir + $"T_BulletHole_{tex}_Normal.png";
            ImportTexture(albedo, false);
            ImportTexture(normal, true);
            var material = MaterialAt(Dir + $"M_BulletHole_{asset}.mat", "Pistol/BulletDecal", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                m.SetColor("_Color", tint);
                m.SetFloat("_BumpScale", bump);
                m.SetFloat("_Glossiness", 0.12f);
            });
            string path = Dir + $"Decal_{asset}.asset";
            var d = AssetDatabase.LoadAssetAtPath<ImpactDecal>(path);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<ImpactDecal>();
            d.kind = ImpactDecal.Kind.Flat;
            d.material = material;
            d.columns = cols; d.rows = rows; d.variants = variants;
            setup(d);
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        static ImpactDecal CraterDecal(string side, Material mask, Material dust)
        {
            string model = Dir + $"BulletCrater_{side}.fbx";
            if (AssetImporter.GetAtPath(model) is ModelImporter mi)
            {
                mi.bakeAxisConversion = true;   // the mesh itself in Unity axes: +Z out of the surface
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                mi.SaveAndReimport();
            }
            else { Debug.LogWarning("Crater model not found: " + model); return null; }

            string albedo = Dir + $"T_BulletHole_Crater{side}_Albedo.png", normal = Dir + $"T_BulletHole_Crater{side}_Normal.png";
            ImportTexture(albedo, false);
            ImportTexture(normal, true);
            var cavity = MaterialAt(Dir + $"M_BulletHole_Crater{side}.mat", "Pistol/BulletHoleCavity", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                m.SetColor("_Color", new Color(0.85f, 0.85f, 0.84f));
            });

            // The model's parts come in the order of its materials (Cavity, Dust, Mask...): match ours by name
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            var renderer = go != null ? go.GetComponentInChildren<MeshRenderer>() : null;
            var filter = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
            if (renderer == null || filter == null) { Debug.LogWarning("Crater model has no mesh: " + model); return null; }
            var mats = renderer.sharedMaterials.Select(m =>
                m == null ? mask : m.name.StartsWith("Cavity") ? cavity : m.name.StartsWith("Dust") ? dust : mask).ToArray();

            string path = Dir + $"Decal_Crater{side}.asset";
            var d = AssetDatabase.LoadAssetAtPath<ImpactDecal>(path);
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<ImpactDecal>();
                d.kind = ImpactDecal.Kind.Mesh;
                d.sizeInMeters = true; d.meters = 1f; d.sizeJitter = 0.15f;
                AssetDatabase.CreateAsset(d, path);
            }
            d.meshes = new[] { filter.sharedMesh };
            d.meshMaterials = mats;
            d.material = cavity;
            EditorUtility.SetDirty(d);
            return d;
        }

        static Material MaterialAt(string path, string shaderName, System.Action<Material> setup)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogError($"Shader {shaderName} not found (compile error?)."); return null; }
            m = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            setup?.Invoke(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void ImportTexture(string path, bool normal)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("Texture not found: " + path); return; }
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType == type && ti.wrapMode == TextureWrapMode.Clamp) return;
            ti.textureType = type;
            ti.sRGBTexture = !normal;
            ti.alphaIsTransparency = !normal;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }
    }
}
