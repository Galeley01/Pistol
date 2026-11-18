using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pistol.EditorTools
{
    // Puts the HDRI sky into the open scene: imports the panorama, makes a Skybox/Panoramic material, makes it
    // the scene sky, the ambient light and the reflections, and turns the sun (the directional light) to where
    // the sun is in the panorama, so shadows match the sky. A light fog in the colour of the haze over the horizon
    // dissolves the far edge of the ground plane into the panorama's hills (the sky itself is never fogged).
    // Menu: Pistol > Apply HDRI Sky. Save the scene afterwards.
    public static class SkySetup
    {
        const string TexturePath = "Assets/_Project/Environment/Sky/golden_gate_hills_8k.exr";
        const string MaterialPath = "Assets/_Project/Environment/Sky/M_Sky_GoldenGate.mat";

        // The panorama has its sun dimmed (clamped to a luminance of 20; the original peak was ~95 000): the directional
        // light is the sun, the sky only adds the soft light from everywhere. With the real sun in it the baked light
        // got it twice and the scene was washed out.
        // Where the sun is in golden_gate_hills_8k.exr (same place in all sizes): the brightest pixel, measured once
        // (u = 0.600 across the panorama, 42.7 degrees above the horizon)
        const float SunU = 0.600f;
        const float SunElevation = 42.7f;

        // The haze just above the horizon of the panorama, measured (between the hills and the bluish sky), sRGB
        static readonly Color FogColor = new Color(0.50f, 0.56f, 0.58f);
        const float FogStart = 25f, FogEnd = 450f;   // metres; the ground plane ends at 750 m

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
            // Mac / PC: the full 8192 x 4096 panorama, compressed HDR (BC6H, ~32 MB of video memory)
            importer.maxTextureSize = 8192;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = true, maxTextureSize = 8192,
                format = TextureImporterFormat.BC6H, textureCompression = TextureImporterCompression.CompressedHQ
            });
            // WebGL can't use BC6H: 4096 x 2048 in the shared-exponent HDR format (RGB9e5, ~32 MB)
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "WebGL", overridden = true, maxTextureSize = 4096,
                format = TextureImporterFormat.RGB9E5
            });
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
                // Shadows that start right at the foot of a wall (a big normal bias leaves a light gap under it)
                sun.shadowBias = 0.03f;
                sun.shadowNormalBias = 0.15f;
                RenderSettings.sun = sun;
            }
            else Debug.LogWarning("No directional light in the scene: shadows won't match the sky.");

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("HDRI sky applied: sky, ambient light, reflections, horizon fog" + (sun != null ? ", sun turned to the sky" : "") + ". Save the scene.");
        }
    }
}
