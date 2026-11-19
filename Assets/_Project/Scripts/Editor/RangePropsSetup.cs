using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pistol.EditorTools
{
    // Props for the range: a concrete box (next to the old white "Cube", same size and place), a T-wall barrier and a car.
    // For each one: texture and model import settings, materials on the built-in Standard shaders, a prefab with a
    // mesh collider on every part, and one instance in the scene (only if the scene has none with that name yet,
    // so moved props stay where they are). The old Cube is switched off when the new box is placed; toggle the two
    // in the Hierarchy to compare. Sizes are measured from the imported meshes and scaled to the sizes below.
    // The car gets its damage set-up (metal surfaces, glass panes, probe lighting) while its prefab is built: CarDamageSetup.
    // Menu: Pistol > Add Range Props. Save the scene afterwards, then bake (Pistol > Bake Lighting).
    public static class RangePropsSetup
    {
        const string Dir = "Assets/_Project/Environment/Props/";
        // Unity's built-in "Standard (Roughness setup)": separate metallic and roughness maps. It is listed under this name.
        const string Roughness = "Autodesk Interactive";

        const float BoxSize = 1f;        // longest side, like the old 1 m cube
        const float TWallHeight = 2f;
        const float CarLength = 3.4f;    // a small jeep

        [MenuItem("Pistol/Add Range Props")]
        static void Add()
        {
            if (Application.isPlaying) { Debug.LogWarning("Add props outside Play Mode."); return; }

            var oldCube = EditorAssets.FindRoot("Cube");
            var cubeMaterial = oldCube != null && oldCube.TryGetComponent<Collider>(out var c) ? c.sharedMaterial : null;

            var box = BuildBox(cubeMaterial);
            var wall = BuildTWall(cubeMaterial);
            var car = BuildCar(CarDamageSetup.Prepare());
            AssetDatabase.SaveAssets();

            // Scene: the box takes the old cube's place (bottom of the cube = bottom of the box), the others next to it
            Vector3 cubeCentre = oldCube != null ? oldCube.transform.position : Vector3.zero;
            float cubeBottom = oldCube != null ? cubeCentre.y - oldCube.transform.lossyScale.y * 0.5f : 0f;
            var origin = new Vector3(cubeCentre.x, cubeBottom, cubeCentre.z);
            if (Place(box, "Box", origin) && oldCube != null)
            {
                Undo.RecordObject(oldCube, "Range props");
                oldCube.SetActive(false);
            }
            Place(wall, "TWall", new Vector3(origin.x, 0f, origin.z + 2.5f));
            Place(car, "Car", new Vector3(origin.x + 4f, 0f, origin.z + 4f));

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Range props: Box (old Cube switched off), TWall and Car are in the scene. Save the scene and re-bake the light.");
        }

        // ---------- Props ----------
        static GameObject BuildBox(PhysicsMaterial surface)
        {
            string d = Dir + "Box/";
            ImportModel(d + "Box.fbx");
            Tex(d + "T_Box_albedo.jpeg", TextureImporterType.Default, true, 2048);
            Tex(d + "T_Box_normal.png", TextureImporterType.NormalMap, false, 2048);
            foreach (var map in new[] { "AO", "metallic", "roughness" }) Tex(d + $"T_Box_{map}.jpeg", TextureImporterType.Default, false, 2048);
            var material = RoughnessMaterial(d + "M_Box.mat", d + "T_Box_albedo.jpeg", d + "T_Box_normal.png",
                d + "T_Box_metallic.jpeg", d + "T_Box_roughness.jpeg", d + "T_Box_AO.jpeg");
            return BuildPrefab(d + "Box.fbx", d + "Box.prefab", "Box", b => BoxSize / Mathf.Max(b.x, b.y, b.z),
                part => (material, surface));
        }

        static GameObject BuildTWall(PhysicsMaterial surface)
        {
            string d = Dir + "TWall/";
            ImportModel(d + "TWall.fbx");
            Tex(d + "T_TWall_albedo.png", TextureImporterType.Default, true, 1024);
            Tex(d + "T_TWall_normal.jpeg", TextureImporterType.NormalMap, false, 1024);
            var material = EditorAssets.LoadOrCreateMaterial(d + "M_TWall.mat", "Standard", switchShader: true);
            if (material == null) return null;
            material.SetTexture("_MainTex", EditorAssets.Texture(d + "T_TWall_albedo.png"));
            material.SetTexture("_BumpMap", EditorAssets.Texture(d + "T_TWall_normal.jpeg"));
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.15f);
            EditorUtility.SetDirty(material);
            return BuildPrefab(d + "TWall.fbx", d + "TWall.prefab", "TWall", b => TWallHeight / b.y,
                part => (material, surface));
        }

        // Each part gets its own texture set (CarDamageSetup.Parts, from the .blend); "Circle" is a 67 m render backdrop
        // and is dropped. Metal parts get the car's metal surface; the glass is split into panes by CarDamageSetup.Decorate.
        static GameObject BuildCar(CarDamageSetup.Kit kit)
        {
            string d = Dir + "Car/";
            ImportModel(d + "Car.fbx");
            var materials = new Dictionary<string, Material>();
            foreach (var (_, (set, _)) in CarDamageSetup.Parts)
            {
                if (materials.ContainsKey(set)) continue;
                string t = d + "T_Car_" + set + "_";
                Tex(t + "BaseColor.png", TextureImporterType.Default, true, 2048);
                Tex(t + "Normal.png", TextureImporterType.NormalMap, false, 2048);
                Tex(t + "Metallic.png", TextureImporterType.Default, false, 2048);
                Tex(t + "Roughness.png", TextureImporterType.Default, false, 2048);
                var m = RoughnessMaterial(d + "M_Car_" + set + ".mat", t + "BaseColor.png", t + "Normal.png",
                    t + "Metallic.png", t + "Roughness.png", null);
                if (m == null) return null;
                if (set == "metal") m.SetFloat("_Metallic", 1f);   // no metallic map in this set
                if (set == CarDamageSetup.GlassSet) MakeGlass(m);
                materials[set] = m;
            }
            return BuildPrefab(d + "Car.fbx", d + "Car.prefab", "Car", b => CarLength / Mathf.Max(b.x, b.z),
                part => CarDamageSetup.Parts.TryGetValue(part, out var p) ? (materials[p.set], p.metal ? kit.metal : null) : (null, null),
                root => CarDamageSetup.Decorate(root, kit));
        }

        // ---------- Prefab ----------
        // Root (scale 1, pivot at the bottom centre) > "Model" (the unpacked model, scaled). Parts the callback
        // returns no material for are removed; the rest get the material and a mesh collider. decorate: a last
        // step on the finished root before it is saved.
        static GameObject BuildPrefab(string modelPath, string prefabPath, string name, System.Func<Vector3, float> scaleFor,
            System.Func<string, (Material, PhysicsMaterial)> partSetup, System.Action<GameObject> decorate = null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) { Debug.LogError("Model not found: " + modelPath); return null; }

            var root = new GameObject(name);
            var inner = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inner, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inner.name = "Model";
            inner.transform.SetParent(root.transform, false);

            foreach (var r in inner.GetComponentsInChildren<MeshRenderer>(true))
            {
                var (material, surface) = partSetup(EditorAssets.PartName(r.gameObject.name));
                if (material == null) { Object.DestroyImmediate(r.gameObject); continue; }
                r.sharedMaterials = Fill(material, r.sharedMaterials.Length);
                var collider = r.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                collider.sharedMaterial = surface;
            }

            EditorAssets.RendererBounds(inner.GetComponentsInChildren<Renderer>(true), out var bounds);
            float scale = scaleFor(bounds.size);
            inner.transform.localScale *= scale;
            EditorAssets.RendererBounds(inner.GetComponentsInChildren<Renderer>(true), out bounds);
            inner.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            decorate?.Invoke(root);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            var size = bounds.size;
            Debug.Log($"{name}: {size.x:0.00} x {size.y:0.00} x {size.z:0.00} m (model x{scale:0.###}) -> {prefabPath}");
            return prefab;
        }

        static bool Place(GameObject prefab, string name, Vector3 position)
        {
            if (prefab == null || EditorAssets.FindRoot(name) != null) return false;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            instance.transform.position = position;
            Undo.RegisterCreatedObjectUndo(instance, "Range props");
            return true;
        }

        static Material[] Fill(Material m, int count)
        {
            var all = new Material[Mathf.Max(1, count)];
            for (int i = 0; i < all.Length; i++) all[i] = m;
            return all;
        }

        // ---------- Materials ----------
        // Autodesk Interactive (Standard, roughness setup): metallic and roughness as separate maps, as the models come
        static Material RoughnessMaterial(string path, string albedo, string normal, string metallic, string roughness, string ao)
        {
            var m = EditorAssets.LoadOrCreateMaterial(path, Roughness, switchShader: true);
            if (m == null) return null;
            m.SetTexture("_MainTex", EditorAssets.Texture(albedo));
            m.SetTexture("_BumpMap", EditorAssets.Texture(normal));
            var metal = EditorAssets.Texture(metallic);
            m.SetTexture("_MetallicGlossMap", metal);
            m.SetTexture("_SpecGlossMap", EditorAssets.Texture(roughness));
            m.SetTexture("_OcclusionMap", ao != null ? EditorAssets.Texture(ao) : null);
            m.SetFloat("_Metallic", 0f);
            Keyword(m, "_NORMALMAP", m.GetTexture("_BumpMap") != null);
            Keyword(m, "_METALLICGLOSSMAP", metal != null);
            Keyword(m, "_SPECGLOSSMAP", m.GetTexture("_SpecGlossMap") != null);
            EditorUtility.SetDirty(m);
            return m;
        }

        // The glass texture has no alpha: see-through by the colour's alpha (Fade), very smooth
        static void MakeGlass(Material m)
        {
            m.SetTexture("_SpecGlossMap", null);   // the roughness map would make it dull
            m.DisableKeyword("_SPECGLOSSMAP");
            EditorAssets.MakeFade(m, new Color(1f, 1f, 1f, 0.35f), 0.95f);
        }

        static void Keyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }

        // ---------- Import settings ----------
        static void ImportModel(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { Debug.LogWarning("Model not found: " + path); return; }
            if (mi.generateSecondaryUV && mi.materialImportMode == ModelImporterMaterialImportMode.None && !mi.importAnimation) return;
            mi.generateSecondaryUV = true;   // lightmap UVs for the bake
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.SaveAndReimport();
        }

        static void Tex(string path, TextureImporterType type, bool srgb, int maxSize) => EditorAssets.ImportTexture(path, type, srgb, maxSize);
    }
}
