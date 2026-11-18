using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Pistol.EditorTools
{
    // Turns Steam Audio on for the project: its spatializer (HRTF: sounds in headphones come from their direction)
    // and its ambisonic decoder become the project's audio plugins. GameAudio's voices already carry a
    // SteamAudioSource and use it for 3D sounds. Menu: Pistol > Setup Steam Audio.
    public static class SteamAudioSetup
    {
        const string Spatializer = "Steam Audio Spatializer";
        const string Ambisonics = "Steam Audio Ambisonics";

        [MenuItem("Pistol/Setup Steam Audio")]
        static void Setup()
        {
            var names = AudioSettings.GetSpatializerPluginNames();
            if (!names.Contains(Spatializer))
            {
                Debug.LogError("Steam Audio spatializer not found. Available: " + string.Join(", ", names) +
                               ". Is the Steam Audio package imported and compiled?");
                return;
            }
            AudioSettings.SetSpatializerPluginName(Spatializer);

            // The ambisonic decoder lives in the AudioManager project settings
            var audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset").FirstOrDefault();
            if (audioManager != null)
            {
                var so = new SerializedObject(audioManager);
                var decoder = so.FindProperty("m_AmbisonicDecoderPlugin");
                if (decoder != null) { decoder.stringValue = Ambisonics; so.ApplyModifiedProperties(); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Steam Audio is the spatializer now. Compare: DebugSettings > Steam Audio HRTF, or key 4.");
        }
    }
}
