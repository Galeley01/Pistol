using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pistol.EditorTools
{
    // Small helpers shared by the setup menus (Pistol > ...): texture import settings, find-or-create assets,
    // scene roots, folders. Each menu keeps its own values; the mechanics live here once.
    public static class EditorAssets
    {
        // Import settings of a texture. Only what is given is set; nothing is reimported when it is already so.
        // The shape is always a plain 2D texture: a .meta written by hand can import as a cubemap.
        // False (with a warning) when there is no texture at the path.
        public static bool ImportTexture(string path, TextureImporterType type, bool srgb, int maxSize = 0,
            TextureWrapMode? wrap = null, bool? alphaIsTransparency = null, TextureImporterAlphaSource? alphaSource = null)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("Texture not found: " + path); return false; }
            bool same = ti.textureType == type && ti.sRGBTexture == srgb && ti.textureShape == TextureImporterShape.Texture2D
                        && ti.mipmapEnabled
                        && (maxSize <= 0 || ti.maxTextureSize == maxSize)
                        && (wrap == null || ti.wrapMode == wrap.Value)
                        && (alphaIsTransparency == null || ti.alphaIsTransparency == alphaIsTransparency.Value)
                        && (alphaSource == null || ti.alphaSource == alphaSource.Value);
            if (same) return true;
            ti.textureType = type;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.sRGBTexture = srgb;
            ti.mipmapEnabled = true;
            if (maxSize > 0) ti.maxTextureSize = maxSize;
            if (wrap != null) ti.wrapMode = wrap.Value;
            if (alphaIsTransparency != null) ti.alphaIsTransparency = alphaIsTransparency.Value;
            if (alphaSource != null) ti.alphaSource = alphaSource.Value;
            ti.SaveAndReimport();
            return true;
        }

        public static Texture2D Texture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        // The material at the path, or a new one with the shader (onCreate fills a new one only: tuned values are kept).
        // switchShader: an existing material gets this shader too. Null (with an error) when the shader is missing.
        public static Material LoadOrCreateMaterial(string path, string shaderName, Action<Material> onCreate = null, bool switchShader = false)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !switchShader) return m;
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogError($"Shader \"{shaderName}\" not found (compile error?)."); return m; }
            if (m != null)
            {
                if (m.shader != shader) m.shader = shader;
                return m;
            }
            m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            onCreate?.Invoke(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // The asset at the path, or a new one (onCreate fills a new one only)
        public static T LoadOrCreate<T>(string path, Action<T> onCreate = null) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            onCreate?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static PhysicsMaterial PhysicsMaterialAt(string path, float dynamicFriction, float staticFriction, float bounciness)
        {
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m != null) return m;
            m = new PhysicsMaterial(Path.GetFileNameWithoutExtension(path))
                { dynamicFriction = dynamicFriction, staticFriction = staticFriction, bounciness = bounciness };
            AssetDatabase.CreateAsset(m, path);
            Debug.Log("Created " + path);
            return m;
        }

        // A see-through Standard material: blended by the colour's alpha (Fade), drawn with the transparent queue
        public static void MakeFade(Material m, Color color, float smoothness)
        {
            m.SetFloat("_Mode", 2f);
            m.SetColor("_Color", color);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetFloat("_Glossiness", smoothness);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        // The first root object of the open scene with this name
        public static GameObject FindRoot(string name)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        // A mesh over 65k vertices is split on import into "<name>_MeshPart0", "_MeshPart1"...: the part's own name
        public static string PartName(string objectName)
        {
            int split = objectName.IndexOf("_MeshPart", StringComparison.Ordinal);
            return split > 0 ? objectName.Substring(0, split) : objectName;
        }

        // Creates the folder and any missing parents ("Assets/A/B")
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        // The bounds around the renderers (those that pass the filter); false when there is none
        public static bool RendererBounds(IEnumerable<Renderer> renderers, out Bounds bounds, Func<Renderer, bool> include = null)
        {
            bounds = default;
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null || include != null && !include(r)) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
