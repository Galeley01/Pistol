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
    // Menu: Pistol > Add Range Props. Save the scene afterwards, then bake (Pistol > Bake Lighting).
    public static class RangePropsSetup
    {
        const string Dir = "Assets/_Project/Environment/Props/";
        const string SteelPath = "Assets/_Project/Targets/PM_Steel.physicMaterial";
        // Unity's built-in "Standard (Roughness setup)": separate metallic and roughness maps. It is listed under this name.
        const string Roughness = "Autodesk Interactive";

        const float BoxSize = 1f;        // longest side, like the old 1 m cube
        const float TWallHeight = 2f;
        const float CarLength = 3.4f;    // a small jeep

        [MenuItem("Pistol/Add Range Props")]
        static void Add()
        {
            if (Application.isPlaying) { Debug.LogWarning("Add props outside Play Mode."); return; }

            var oldCube = FindRoot("Cube");
            // The car's own metal surface once Setup Car Damage made it, the target steel before that
            var steel = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(CarDamageSetup.CarMetalPath)
                        ?? AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(SteelPath);
            var cubeMaterial = oldCube != null && oldCube.TryGetComponent<Collider>(out var c) ? c.sharedMaterial : null;

            var box = BuildBox(cubeMaterial);
            var wall = BuildTWall(cubeMaterial);
            var car = BuildCar(steel);
            AssetDatabase.SaveAssets();
            if (car != null) CarDamageSetup.SetUpCarPrefab();   // the rebuilt car gets its glass panes again

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
            Tex(d + "T_Box_AO.jpeg", TextureImporterType.Default, false, 2048);
            Tex(d + "T_Box_metallic.jpeg", TextureImporterType.Default, false, 2048);
            Tex(d + "T_Box_roughness.jpeg", TextureImporterType.Default, false, 2048);
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
            var material = LoadOrCreate(d + "M_TWall.mat", "Standard");
            material.SetTexture("_MainTex", Load(d + "T_TWall_albedo.png"));
            material.SetTexture("_BumpMap", Load(d + "T_TWall_normal.jpeg"));
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.15f);
            EditorUtility.SetDirty(material);
            return BuildPrefab(d + "TWall.fbx", d + "TWall.prefab", "TWall", b => TWallHeight / b.y,
                part => (material, surface));
        }

        // Each part gets its own texture set (from the .blend); "Circle" is a 67 m render backdrop and is dropped.
        // Metal surface on the body, wheels and metal parts (PM_CarMetal, see CarDamageSetup); the glass is split into panes there.
        static GameObject BuildCar(PhysicsMaterial steel)
        {
            string d = Dir + "Car/";
            ImportModel(d + "Car.fbx");
            var parts = new Dictionary<string, (string set, bool metal)>
            {
                { "Plane23", ("willys", true) }, { "Object27", ("disks", true) }, { "Cylinder53", ("metal", true) },
                { "Cylinder54", ("tieres", false) }, { "Cylinder58", ("inside", false) }, { "Object19", ("glass", false) },
            };
            var materials = new Dictionary<string, Material>();
            foreach (var set in new[] { "willys", "disks", "metal", "tieres", "inside", "glass" })
            {
                string t = d + "T_Car_" + set + "_";
                Tex(t + "BaseColor.png", TextureImporterType.Default, true, 2048);
                Tex(t + "Normal.png", TextureImporterType.NormalMap, false, 2048);
                Tex(t + "Metallic.png", TextureImporterType.Default, false, 2048);
                Tex(t + "Roughness.png", TextureImporterType.Default, false, 2048);
                var m = RoughnessMaterial(d + "M_Car_" + set + ".mat", t + "BaseColor.png", t + "Normal.png",
                    t + "Metallic.png", t + "Roughness.png", null);
                if (set == "metal") m.SetFloat("_Metallic", 1f);   // no metallic map in this set
                if (set == "glass") MakeGlass(m);
                materials[set] = m;
            }
            return BuildPrefab(d + "Car.fbx", d + "Car.prefab", "Car", b => CarLength / Mathf.Max(b.x, b.z),
                part => parts.TryGetValue(part, out var p) ? (materials[p.set], p.metal ? steel : null) : (null, null));
        }

        // ---------- Prefab ----------
        // Root (scale 1, pivot at the bottom centre) > "Model" (the unpacked model, scaled). Parts the callback
        // returns no material for are removed; the rest get the material and a mesh collider.
        static GameObject BuildPrefab(string modelPath, string prefabPath, string name, System.Func<Vector3, float> scaleFor,
            System.Func<string, (Material, PhysicsMaterial)> partSetup)
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
                // Meshes over 65k vertices are split on import into "<name>_MeshPart0", "_MeshPart1"...
                string part = r.gameObject.name;
                int split = part.IndexOf("_MeshPart", System.StringComparison.Ordinal);
                if (split > 0) part = part.Substring(0, split);
                var (material, surface) = partSetup(part);
                if (material == null) { Object.DestroyImmediate(r.gameObject); continue; }
                r.sharedMaterials = Fill(material, r.sharedMaterials.Length);
                var collider = r.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                collider.sharedMaterial = surface;
            }

            var bounds = Bounds(inner);
            float scale = scaleFor(bounds.size);
            inner.transform.localScale *= scale;
            bounds = Bounds(inner);
            inner.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            var size = bounds.size;
            Debug.Log($"{name}: {size.x:0.00} x {size.y:0.00} x {size.z:0.00} m (model x{scale:0.###}) -> {prefabPath}");
            return prefab;
        }

        static bool Place(GameObject prefab, string name, Vector3 position)
        {
            if (prefab == null || FindRoot(name) != null) return false;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            instance.transform.position = position;
            Undo.RegisterCreatedObjectUndo(instance, "Range props");
            return true;
        }

        static GameObject FindRoot(string name)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds(go.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
                if (i == 0) b = renderers[i].bounds; else b.Encapsulate(renderers[i].bounds);
            return b;
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
            var m = LoadOrCreate(path, Roughness);
            m.SetTexture("_MainTex", Load(albedo));
            m.SetTexture("_BumpMap", Load(normal));
            var metal = Load(metallic);
            m.SetTexture("_MetallicGlossMap", metal);
            m.SetTexture("_SpecGlossMap", Load(roughness));
            m.SetTexture("_OcclusionMap", ao != null ? Load(ao) : null);
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
            m.SetFloat("_Mode", 2f);
            m.SetColor("_Color", new Color(1f, 1f, 1f, 0.35f));
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetTexture("_SpecGlossMap", null);   // the roughness map would make it dull
            m.DisableKeyword("_SPECGLOSSMAP");
            m.SetFloat("_Glossiness", 0.95f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        static Material LoadOrCreate(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception($"Shader \"{shaderName}\" not found.");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        static void Keyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }

        static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

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

        static void Tex(string path, TextureImporterType type, bool srgb, int maxSize)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("Texture not found: " + path); return; }
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == maxSize
                && ti.textureShape == TextureImporterShape.Texture2D) return;
            ti.textureType = type;
            ti.textureShape = TextureImporterShape.Texture2D;   // a bare .meta can import as a cubemap
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = maxSize;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }
}
