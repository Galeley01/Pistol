using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.Audio;
using Pistol.Core;

namespace Pistol.EditorTools
{
    // Sets up how the range answers a gunshot: the tail and echo sound events (from the distant-shot recordings),
    // the outdoor and indoor acoustics profiles, the RangeAcoustics object in the scene (with its reverb zone),
    // and points GameAudio to it. The revolver shot gets cut before its recorded echo (the scene makes echoes now).
    // Assets that already exist are kept (they may be tuned). Menu: Pistol > Setup Range Acoustics. Save the scene.
    public static class AcousticsSetup
    {
        const string Dir = "Assets/_Project/Weapons/Shared/Audio/";
        const string TailClip = Dir + "Distant Shot Tail.mp3";
        const string EchoClip = Dir + "Distant Shot Echo.mp3";
        const string ShotPath = "Assets/_Project/Weapons/Revolver/Audio/Revolver_Shot.asset";
        const string DebugPath = "Assets/_Project/Settings/DebugSettings.asset";
        const string OutdoorPath = "Assets/_Project/Settings/Acoustics_Outdoor.asset";
        const string IndoorPath = "Assets/_Project/Settings/Acoustics_Indoor.asset";

        [MenuItem("Pistol/Setup Range Acoustics")]
        static void Setup()
        {
            if (Application.isPlaying) { Debug.LogWarning("Set up the acoustics outside Play Mode."); return; }

            var weapons = WeaponsGroup();
            var tail = Sound("Shot_Tail", TailClip, weapons, s =>
            {
                s.volume = 0.7f; s.pitchVariation = 0.04f;
                s.spatialBlend = 0.7f; s.spread = 60f;           // rolls away to the open side
                s.minDistance = 50f; s.maxDistance = 500f;
            });
            var echo = Sound("Shot_Echo", EchoClip, weapons, s =>
            {
                s.volume = 0.8f; s.pitchVariation = 0.05f;
                s.spatialBlend = 0.85f; s.spread = 90f;           // from where it bounced, but wide
                s.minDistance = 400f; s.maxDistance = 1000f;      // the volume is set by RangeAcoustics, not by the distance
            });

            // Early reflections: the bang itself, short, heard from the wall (no fading with distance: set by the code)
            var shotEvent = AssetDatabase.LoadAssetAtPath<SoundEvent>(ShotPath);
            var reflection = AssetDatabase.LoadAssetAtPath<SoundEvent>(Dir + "Shot_Reflection.asset");
            if (reflection == null && shotEvent != null)
            {
                reflection = ScriptableObject.CreateInstance<SoundEvent>();
                reflection.clips = shotEvent.clips;
                reflection.output = weapons;
                reflection.volume = 1f; reflection.pitchVariation = 0.03f;
                reflection.spatialBlend = 1f; reflection.spread = 0f;   // a point on the wall: clearly left or right
                reflection.minDistance = 30f; reflection.maxDistance = 300f;
                reflection.endTime = 0.45f; reflection.endFade = 0.2f;
                AssetDatabase.CreateAsset(reflection, Dir + "Shot_Reflection.asset");
            }

            var outdoor = Profile(OutdoorPath, p =>
            {
                p.reverb = AudioReverbPreset.Plain;
                p.tail = tail; p.tailVolume = 0.6f;
                p.echo = echo; p.terrainEcho = true; p.minDistance = 12f; p.maxEchoes = 2;
            });
            var indoor = Profile(IndoorPath, p =>
            {
                p.reverb = AudioReverbPreset.Stoneroom;
                p.tail = tail; p.tailVolume = 0.25f;              // walls hold the roll in: mostly the room's own reverb
                p.echo = echo; p.minDistance = 4f; p.maxDistance = 40f; p.maxEchoes = 3; p.echoVolume = 0.2f;
                p.terrainEcho = false;
            });

            foreach (var p in new[] { outdoor, indoor })
                if (p != null && p.reflection == null)
                {
                    Undo.RecordObject(p, "Early reflections");
                    p.reflection = reflection;
                    if (p == indoor) { p.reflectionVolume = 0.6f; p.maxReflections = 6; }
                    EditorUtility.SetDirty(p);
                }

            // The revolver shot recording has an echo of the place it was recorded in (1.1–1.5 s): cut before it
            var shot = AssetDatabase.LoadAssetAtPath<SoundEvent>(ShotPath);
            if (shot != null && shot.endTime <= 0f)
            {
                Undo.RecordObject(shot, "Cut the recorded echo");
                shot.endTime = 0.9f; shot.endFade = 0.25f;
                EditorUtility.SetDirty(shot);
            }

            // The scene object
            var acoustics = Object.FindFirstObjectByType<RangeAcoustics>();   // editor tool, once
            if (acoustics == null)
            {
                var go = new GameObject("RangeAcoustics");
                Undo.RegisterCreatedObjectUndo(go, "Range Acoustics");
                var zone = go.AddComponent<AudioReverbZone>();
                zone.minDistance = 50f; zone.maxDistance = 60f;   // it moves with the listener: always fully inside
                acoustics = go.AddComponent<RangeAcoustics>();
            }
            var gameAudio = Object.FindFirstObjectByType<GameAudio>();
            var cam = Camera.main != null ? Camera.main.transform : null;
            Undo.RecordObject(acoustics, "Configure acoustics");
            acoustics.Configure(gameAudio, cam, AssetDatabase.LoadAssetAtPath<DebugSettings>(DebugPath), outdoor, indoor);
            EditorUtility.SetDirty(acoustics);
            if (gameAudio != null)
            {
                var so = new SerializedObject(gameAudio);
                so.FindProperty("acoustics").objectReferenceValue = acoustics;
                so.ApplyModifiedProperties();
            }
            else Debug.LogWarning("No GameAudio in the scene: shots won't reach the acoustics.");

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(acoustics.gameObject.scene);
            Selection.activeObject = acoustics.gameObject;
            Debug.Log("Range acoustics set up (outdoor by default). Before / after: DebugSettings > New Acoustics or key 4. Save the scene.");
        }

        static UnityEngine.Audio.AudioMixerGroup WeaponsGroup()
        {
            var shot = AssetDatabase.LoadAssetAtPath<SoundEvent>(ShotPath);
            return shot != null ? shot.output : null;
        }

        static SoundEvent Sound(string name, string clipPath, UnityEngine.Audio.AudioMixerGroup output, System.Action<SoundEvent> setup)
        {
            string path = Dir + name + ".asset";
            var s = AssetDatabase.LoadAssetAtPath<SoundEvent>(path);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<SoundEvent>();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip == null) Debug.LogWarning("Clip not found: " + clipPath);
            s.clips = clip != null ? new[] { clip } : new AudioClip[0];
            s.output = output;
            setup(s);
            AssetDatabase.CreateAsset(s, path);
            return s;
        }

        static AcousticsProfile Profile(string path, System.Action<AcousticsProfile> setup)
        {
            var p = AssetDatabase.LoadAssetAtPath<AcousticsProfile>(path);
            if (p != null) return p;
            p = ScriptableObject.CreateInstance<AcousticsProfile>();
            setup(p);
            AssetDatabase.CreateAsset(p, path);
            return p;
        }
    }
}
