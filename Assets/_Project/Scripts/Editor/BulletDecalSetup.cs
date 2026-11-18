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
    //     drawn inside it, the scan decal around it gives the dust and chips (looked flat: no depth order, no shadow);
    //   Scan depth entry / exit — the scan decals with a height map baked from the same scan (Pistol/BulletDecalDepth):
    //     a flat quad on the wall that shows the crater sunk in, with the rim's shadow inside it;
    //     the exit one has angled-hit variants (ScanExit45/65/80): elongated along the bullet's travel.
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
            // Scan with depth: same textures + height, same size; depth as a fraction of the size = 2.5 cm / side
            var depthEntry = DepthDecal("Entry", flatEntry, 0.025f / 0.112f);
            var depthExit = DepthDecal("Exit", flatExit, 0.025f / 0.154f);
            // Angled hits on the exit style: the scan crater deformed for 45 / 65 / 80 degrees (stretched along the
            // travel, shallower, pit pushed downrange, hole filled); picked from 35 / 55 / 72 degrees
            if (depthExit != null && (depthExit.angleVariants == null || depthExit.angleVariants.Length == 0 ||
                                      depthExit.angleVariants.Any(v => v == null || v.decal == null)))   // empty or deleted: make again
            {
                var a45 = AngledDecal("ScanExit45", 0.223f, 0.025f / 0.223f);
                var a65 = AngledDecal("ScanExit65", 0.27f, 0.025f / 0.308f);
                var a80 = AngledDecal("ScanExit80", 0.28f, 0.025f / 0.400f);
                depthExit.angleVariants = new[]
                {
                    new ImpactDecal.AngleVariant { fromAngle = 35f, decal = a45 },
                    new ImpactDecal.AngleVariant { fromAngle = 55f, decal = a65 },
                    new ImpactDecal.AngleVariant { fromAngle = 72f, decal = a80 },
                };
                EditorUtility.SetDirty(depthExit);
            }

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
                bullets.SetHoleStyles(new[] { photo, flatEntry, flatExit, craterEntry, craterExit, depthEntry, depthExit });
                EditorUtility.SetDirty(bullets);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bullets.gameObject.scene);
            }
            else Debug.LogWarning("No BulletSystem in the open scene: the style list isn't filled.");

            AssetDatabase.SaveAssets();
            Debug.Log("Bullet hole styles set up: photo, scan flat entry/exit (exit is the default), scan crater entry/exit, scan depth entry/exit. " +
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

        // The scan decal of that side with its height map: a copy of the flat decal's settings, the depth shader
        static ImpactDecal DepthDecal(string side, ImpactDecal flat, float parallax)
        {
            string albedo = Dir + $"T_BulletHole_Scan{side}_Albedo.png", normal = Dir + $"T_BulletHole_Scan{side}_Normal.png";
            string height = Dir + $"T_BulletHole_Scan{side}_Height.png";
            if (!ImportHeight(height)) return null;

            var material = MaterialAt(Dir + $"M_BulletHole_ScanDepth{side}.mat", "Pistol/BulletDecalDepth", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                m.SetTexture("_HeightMap", AssetDatabase.LoadAssetAtPath<Texture2D>(height));
                m.SetColor("_Color", new Color(0.85f, 0.85f, 0.84f));
                m.SetFloat("_Parallax", parallax);
            });
            FillMissing(material, albedo, normal, height);
            string path = Dir + $"Decal_ScanDepth{side}.asset";
            var d = AssetDatabase.LoadAssetAtPath<ImpactDecal>(path);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<ImpactDecal>();
            d.kind = ImpactDecal.Kind.Flat;
            d.material = material;
            d.columns = 1; d.rows = 1; d.variants = 1;
            d.sizeInMeters = true; d.meters = flat != null ? flat.meters : 0.15f; d.sizeJitter = 0.2f;
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        // A material made in the same run as its textures' first import can come out without them: fill the empty slots
        static void FillMissing(Material m, string albedo, string normal, string height)
        {
            if (m == null) return;
            bool changed = false;
            foreach (var (prop, path) in new[] { ("_MainTex", albedo), ("_BumpMap", normal), ("_HeightMap", height) })
            {
                if (m.GetTexture(prop) != null) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) { Debug.LogWarning($"{m.name}: texture not loaded yet: {path}. Run the menu again."); continue; }
                m.SetTexture(prop, tex);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(m);
        }

        // Heights: linear, single channel, uncompressed (no blocky steps in the parallax)
        static bool ImportHeight(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter hi)) { Debug.LogWarning("Height map not found: " + path); return false; }
            if (!hi.sRGBTexture && hi.wrapMode == TextureWrapMode.Clamp && hi.textureCompression == TextureImporterCompression.Uncompressed) return true;
            hi.textureType = TextureImporterType.Default;
            hi.sRGBTexture = false;
            hi.alphaSource = TextureImporterAlphaSource.None;
            hi.wrapMode = TextureWrapMode.Clamp;
            hi.mipmapEnabled = true;
            hi.textureCompression = TextureImporterCompression.Uncompressed;
            hi.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, format = TextureImporterFormat.R8, maxTextureSize = 512 });
            hi.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "WebGL", overridden = true, format = TextureImporterFormat.R8, maxTextureSize = 512 });
            hi.SaveAndReimport();
            return true;
        }

        // An angled-hit mark (T_BulletHole_<tag>_*): depth shader, laid along the bullet's travel
        static ImpactDecal AngledDecal(string tag, float meters, float parallax)
        {
            string albedo = Dir + $"T_BulletHole_{tag}_Albedo.png", normal = Dir + $"T_BulletHole_{tag}_Normal.png";
            string height = Dir + $"T_BulletHole_{tag}_Height.png";
            ImportTexture(albedo, false);
            ImportTexture(normal, true);
            if (!ImportHeight(height)) return null;
            var material = MaterialAt(Dir + $"M_BulletHole_{tag}.mat", "Pistol/BulletDecalDepth", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                m.SetTexture("_HeightMap", AssetDatabase.LoadAssetAtPath<Texture2D>(height));
                m.SetColor("_Color", new Color(0.85f, 0.85f, 0.84f));
                m.SetFloat("_Parallax", parallax);
            });
            FillMissing(material, albedo, normal, height);
            string path = Dir + $"Decal_{tag}.asset";
            var d = AssetDatabase.LoadAssetAtPath<ImpactDecal>(path);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<ImpactDecal>();
            d.kind = ImpactDecal.Kind.Flat;
            d.material = material;
            d.columns = 1; d.rows = 1; d.variants = 1;
            d.sizeInMeters = true; d.meters = meters; d.sizeJitter = 0.15f;
            d.randomRotation = false;
            d.alongTravel = true;
            AssetDatabase.CreateAsset(d, path);
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
