using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pistol.EditorTools
{
    // Puts the HDRI sky into the open scene: imports the panorama, makes a Skybox/Panoramic material, makes it
    // the scene sky, the ambient light and the reflections, and turns the sun (the directional light) to where
    // the sun is in the panorama, so shadows match the sky.
    // Menu: Pistol > Apply HDRI Sky. Save the scene afterwards.
    public static class SkySetup
    {
        const string TexturePath = "Assets/_Project/Environment/Sky/golden_gate_hills_2k.exr";
        const string MaterialPath = "Assets/_Project/Environment/Sky/M_Sky_GoldenGate.mat";

        // Where the sun is in golden_gate_hills_2k.exr: the brightest pixel, measured once
        // (u = 0.600 across the panorama, 42.7 degrees above the horizon)
        const float SunU = 0.600f;
        const float SunElevation = 42.7f;

        [MenuItem("Pistol/Apply HDRI Sky")]
        static void Apply()
        {
            if (Application.isPlaying) { Debug.LogWarning("Apply the sky outside Play Mode."); return; }

            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null) { Debug.LogError("Sky panorama not found: " + TexturePath); return; }
            // A 360 panorama: wraps around horizontally, not over the poles; no mipmaps (they leave a seam at the wrap)
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096;   // keep the full 2048 x 1024 panorama
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Panoramic")) { name = "M_Sky_GoldenGate" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Mapping", 1f);     // latitude-longitude layout
            material.SetFloat("_ImageType", 0f);   // full 360
            if (!material.HasProperty("_Exposure") || material.GetFloat("_Exposure") <= 0f) material.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;

            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))   // editor tool, once
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                Undo.RecordObject(sun.transform, "Turn the sun to the sky");
                // Skybox/Panoramic: u = 0.5 - atan2(z, x) / 2pi, so the sun's direction is
                float longitude = (0.5f - SunU) * 2f * Mathf.PI;
                float elev = SunElevation * Mathf.Deg2Rad;
                var toSun = new Vector3(Mathf.Cos(longitude) * Mathf.Cos(elev), Mathf.Sin(elev), Mathf.Sin(longitude) * Mathf.Cos(elev));
                sun.transform.rotation = Quaternion.LookRotation(-toSun);
                RenderSettings.sun = sun;
            }
            else Debug.LogWarning("No directional light in the scene: shadows won't match the sky.");

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("HDRI sky applied: sky, ambient light, reflections" + (sun != null ? ", sun turned to the sky" : "") + ". Save the scene.");
        }
    }
}
