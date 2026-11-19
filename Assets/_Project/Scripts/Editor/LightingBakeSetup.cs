using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.Targets;

namespace Pistol.EditorTools
{
    // Baked light for the open scene. The sun stays realtime (sharp shadows, also on moving things); what is baked is
    // the light that comes from the sky and bounces off the ground and walls, and the soft darkening near walls and
    // under objects (baked AO). Moving things (the weapon, the arms, falling plates) get that light from light probes.
    //   - everything that never moves gets the "Contribute GI" static flag (moving target parts don't);
    //   - a renderer set to take its light from the probes (the car: its lightmap UVs are many small pieces) keeps that:
    //     it still blocks and bounces light in the bake, but gets no lightmap of its own;
    //   - the target model gets lightmap UVs; the huge ground plane gets a low resolution in the lightmap;
    //   - lighting settings: Baked Indirect, GPU lightmapper, baked AO; the sun is Mixed;
    //   - a light probe grid and a baked reflection probe over the range;
    //   - the bake starts (a few minutes, progress bottom right). Save the scene when it is done.
    // Menu: Pistol > Bake Lighting. Pistol > Remove Baked Lighting goes back to the look before (for a before / after look).
    public static class LightingBakeSetup
    {
        const string SettingsPath = "Assets/_Project/Environment/Lighting_Range.lighting";
        const string TargetModelPath = "Assets/ThirdParty/ShootingTargetSet/Models/ShootingTargetCollection.fbx";
        // The 1500 m ground plane gets ~0.4 texel per metre whatever the resolution: at a fixed share it fills the whole
        // lightmap at a high resolution and squeezes everything else into a corner
        const float GroundTexelsPerMetre = 0.4f;
        const float NormalResolution = 10f, HighResolution = 30f;   // texels per metre for normal objects

        [MenuItem("Pistol/Bake Lighting")]
        static void Bake() => Bake(false);

        // Experiment: sharper soft shadows at the foot of walls and in corners, cleaner and darker AO. Costs bake time,
        // lightmap memory and build size, not frame time.
        [MenuItem("Pistol/Bake Lighting (High Quality)")]
        static void BakeHigh() => Bake(true);

        static void Bake(bool high)
        {
            if (Application.isPlaying) { Debug.LogWarning("Bake outside Play Mode."); return; }
            if (Lightmapping.isRunning) { Debug.LogWarning("A bake is already running."); return; }

            LightmapUvsOnTargets();
            int statics = MarkStatics(high ? HighResolution : NormalResolution);
            ApplySettings(high);
            var range = RangeBounds();
            LightProbes(range);
            ReflectionProbe(range);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            StartBake();
            Debug.Log($"Bake started ({(high ? "high quality" : "normal")}): {statics} static objects. Progress is in the bottom right corner of the editor.");
        }

        static void StartBake()
        {
            Lightmapping.bakeCompleted -= OnBaked;
            Lightmapping.bakeCompleted += OnBaked;
            Lightmapping.BakeAsync();
        }

        static void OnBaked()
        {
            Lightmapping.bakeCompleted -= OnBaked;
            Debug.Log("Bake done. Save the scene (the lightmaps are in the folder next to it).");
        }

        // Back to the look before the bake: nothing static, the sun realtime, no lightmaps. The scene still needs its
        // lighting data (the sky light and reflections live there), so a quick bake of just the sky runs right away;
        // simply deleting the data leaves every shadow pitch black.
        [MenuItem("Pistol/Remove Baked Lighting")]
        static void Remove()
        {
            if (Application.isPlaying || Lightmapping.isRunning) return;
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    SetBakeStatic(r.gameObject, false);
            var sun = RenderSettings.sun;
            if (sun != null && sun.lightmapBakeType != LightmapBakeType.Realtime)
            {
                Undo.RecordObject(sun, "Sun to Realtime");
                sun.lightmapBakeType = LightmapBakeType.Realtime;
            }
            Lightmapping.Clear();
            EditorSceneManager.MarkSceneDirty(scene);
            StartBake();   // only the sky light and the reflection probe: a few seconds
            Debug.Log("Baked lighting removed, the sky light is being baked again (a few seconds). Save the scene afterwards.");
        }

        // ---------- Steps ----------
        static void LightmapUvsOnTargets()
        {
            if (AssetImporter.GetAtPath(TargetModelPath) is ModelImporter model && !model.generateSecondaryUV)
            {
                model.generateSecondaryUV = true;
                model.SaveAndReimport();
                Debug.Log("Lightmap UVs generated for " + TargetModelPath);
            }
        }

        // Things that move must stay dynamic: targets that move or break in play (ShootingTarget.MovesAtRuntime),
        // bodies, and everything under them
        static bool Moves(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (t.GetComponent<Rigidbody>()) return true;
                var target = t.GetComponent<ShootingTarget>();
                if (target != null && target.MovesAtRuntime) return true;
            }
            return false;
        }

        // The "Contribute GI" and "Reflection Probe" static flags on or off (with Undo)
        static void SetBakeStatic(GameObject go, bool on)
        {
            const StaticEditorFlags Bake = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            var wanted = on ? flags | Bake : flags & ~Bake;
            if (wanted == flags) return;
            Undo.RecordObject(go, on ? "Mark static for baking" : "Unmark static");
            GameObjectUtility.SetStaticEditorFlags(go, wanted);
        }

        static int MarkStatics(float resolution)
        {
            int count = 0;
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<Camera>(true) != null) continue;   // the player with the weapon and the arms
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var go = r.gameObject;
                    bool moves = Moves(r.transform);
                    SetBakeStatic(go, !moves);
                    if (moves) continue;
                    // How a renderer takes the baked light (lightmap, Unity's default, or the probes, like the car) is left
                    // as its prefab set it
                    // The ground plane is huge: a low resolution in the lightmap is enough for the soft bounce light
                    if (go.transform.lossyScale.x > 50f || go.transform.lossyScale.z > 50f)
                    {
                        var so = new SerializedObject(r);
                        so.FindProperty("m_ScaleInLightmap").floatValue = GroundTexelsPerMetre / resolution;
                        so.ApplyModifiedProperties();
                    }
                    count++;
                }
            }
            return count;
        }

        // The values are written on every bake, so the normal and the high quality bake can be switched back and forth
        static void ApplySettings(bool high)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "Lighting_Range" };
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.realtimeGI = false;
            settings.bakedGI = true;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;   // the sun's direct light and shadows stay realtime
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = high ? HighResolution : NormalResolution;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 2;
            settings.ao = true;                               // soft darkening near walls and under objects
            settings.aoMaxDistance = high ? 1.5f : 1f;
            settings.aoExponentIndirect = high ? 1.5f : 1f;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = high ? 1024 : 256;
            settings.environmentSampleCount = high ? 512 : 256;
            settings.maxBounces = 2;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            settings.lightmapCompression = high ? LightmapCompression.HighQuality : LightmapCompression.NormalQuality;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            var sun = RenderSettings.sun;
            if (sun != null && sun.lightmapBakeType != LightmapBakeType.Mixed)
            {
                Undo.RecordObject(sun, "Sun to Mixed");
                sun.lightmapBakeType = LightmapBakeType.Mixed;
            }
        }

        // Where the range is: the static things except the huge ground, with some room around
        static Bounds RangeBounds()
        {
            var renderers = new List<Renderer>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
            if (!EditorAssets.RendererBounds(renderers, out var b, r => r.bounds.size.x <= 100f && r.bounds.size.z <= 100f))
                b = new Bounds(Vector3.zero, new Vector3(20f, 4f, 20f));
            b.Expand(new Vector3(8f, 0f, 8f));
            b.SetMinMax(new Vector3(b.min.x, 0f, b.min.z), new Vector3(b.max.x, Mathf.Max(b.max.y, 3f), b.max.z));
            return b;
        }

        static void LightProbes(Bounds range)
        {
            var group = Object.FindFirstObjectByType<LightProbeGroup>();   // editor tool, once
            if (group == null)
            {
                var go = new GameObject("Light Probes");
                Undo.RegisterCreatedObjectUndo(go, "Light Probes");
                group = go.AddComponent<LightProbeGroup>();
            }
            group.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            const float step = 2.5f;
            float[] heights = { 0.3f, 1.5f, 3f };
            var positions = new List<Vector3>();
            for (float x = range.min.x; x <= range.max.x + 0.01f; x += step)
                for (float z = range.min.z; z <= range.max.z + 0.01f; z += step)
                    foreach (var y in heights) positions.Add(new Vector3(x, y, z));
            group.probePositions = positions.ToArray();
            Debug.Log($"Light probes: {positions.Count} over {range.size.x:0} x {range.size.z:0} m");
        }

        static void ReflectionProbe(Bounds range)
        {
            var probe = Object.FindFirstObjectByType<ReflectionProbe>();   // editor tool, once
            if (probe == null)
            {
                var go = new GameObject("Reflection Probe");
                Undo.RegisterCreatedObjectUndo(go, "Reflection Probe");
                probe = go.AddComponent<ReflectionProbe>();
            }
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.transform.position = new Vector3(range.center.x, 1.5f, range.center.z);
            probe.size = new Vector3(range.size.x, 10f, range.size.z);
            probe.boxProjection = false;
            probe.resolution = 256;
        }
    }
}
