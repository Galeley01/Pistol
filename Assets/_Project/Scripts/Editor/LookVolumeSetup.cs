using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Pistol.Core;
using Pistol.PostFx;

namespace Pistol.EditorTools
{
    // Makes the "new look" on top of the existing PostFX volume, as a separate global volume that can be switched
    // off with a key (LookComparison) to compare before / after:
    //   ACES tonemapping — film-like mapping of the bright HDR sky and sun to the screen (no burnt white);
    //   light bloom — only really bright things glow (sun glints, the muzzle flash);
    //   ambient occlusion — darker creases and contact spots (where objects touch the ground).
    // Settings it doesn't override stay as in the PostFX profile (saturation, contrast, vignette, grain...).
    // It also fills the LookSwitcher (before / after switches in DebugSettings: post-processing, sky, baked light).
    // Menu: Pistol > Create Look Volume. An existing profile is kept (it may be tuned); run it again to refill the
    // switcher's references; save the scene afterwards.
    public static class LookVolumeSetup
    {
        const string ProfilePath = "Assets/_Project/Settings/PostFX Look.asset";
        const string DebugPath = EditorPaths.DebugSettings;
        const string SkyPath = "Assets/_Project/Environment/Sky/M_Sky_GoldenGate.mat";

        [MenuItem("Pistol/Create Look Volume")]
        static void Create()
        {
            if (Application.isPlaying) { Debug.LogWarning("Create the look volume outside Play Mode."); return; }

            var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);

                var grading = Add<ColorGrading>(profile);
                grading.gradingMode.Override(GradingMode.HighDefinitionRange);
                grading.tonemapper.Override(Tonemapper.ACES);
                grading.postExposure.Override(0.3f);   // ACES darkens the mid tones a little

                var bloom = Add<Bloom>(profile);
                bloom.intensity.Override(0.6f);
                bloom.threshold.Override(1.2f);         // only what is brighter than white glows
                bloom.softKnee.Override(0.5f);

                var ao = Add<AmbientOcclusion>(profile);
                ao.mode.Override(AmbientOcclusionMode.ScalableAmbientObscurance);   // works in WebGL too (multi-scale needs compute shaders)
                ao.intensity.Override(0.6f);
                ao.thicknessModifier.Override(1f);

                AssetDatabase.SaveAssets();
                Debug.Log("Created " + ProfilePath);
            }

            // The new volume goes on the layer the camera's Post-process Layer listens to (same as the old volume)
            int layer = 0;
            foreach (var v in Object.FindObjectsByType<PostProcessVolume>(FindObjectsSortMode.None))   // editor tool, once
            {
                if (v.sharedProfile == profile)
                {
                    var existing = v.GetComponent<LookSwitcher>();
                    if (existing == null) existing = v.gameObject.AddComponent<LookSwitcher>();
                    Fill(existing);
                    Selection.activeObject = v.gameObject;
                    EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
                    Debug.Log("The look volume is already in the scene: switcher references refilled. Save the scene.");
                    return;
                }
                layer = v.gameObject.layer;
            }

            var go = new GameObject("PostFX Look") { layer = layer };
            Undo.RegisterCreatedObjectUndo(go, "Create Look Volume");
            var volume = go.AddComponent<PostProcessVolume>();
            volume.isGlobal = true;
            volume.priority = 1f;   // above the PostFX volume
            volume.sharedProfile = profile;
            Fill(go.AddComponent<LookSwitcher>());
            Selection.activeObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("Look volume added. Before / after: DebugSettings > Сравнение вида, or key 4 in Play. Save the scene.");
        }

        static void Fill(LookSwitcher switcher)
        {
            Undo.RecordObject(switcher, "Fill look switcher");
            switcher.Configure(
                AssetDatabase.LoadAssetAtPath<DebugSettings>(DebugPath),
                AssetDatabase.LoadAssetAtPath<Material>(SkyPath),
                AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat"),
                RenderSettings.sun,
                Object.FindFirstObjectByType<Pistol.Cameras.CameraMotion>());   // editor tool, once
            EditorUtility.SetDirty(switcher);
        }

        static T Add<T>(PostProcessProfile profile) where T : PostProcessEffectSettings
        {
            var s = profile.AddSettings<T>();
            s.enabled.Override(true);
            s.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(s, profile);
            return s;
        }
    }
}
