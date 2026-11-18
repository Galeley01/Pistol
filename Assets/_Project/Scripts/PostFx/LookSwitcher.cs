using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;
using Pistol.Cameras;
using Pistol.Core;

namespace Pistol.PostFx
{
    // Before / after for the look of the scene. Key 4 flips everything at once between the new look and the look
    // before all this work (Unity's default sky and old sun, no fog, no new post-processing, no baked light: static
    // objects then drop their lightmaps and take only the sky's light). The DebugSettings switches pick parts of the
    // new look, live in Play:
    //   new post-processing — this volume (ACES, light bloom, AO) on / off;
    //   new sky — the HDRI sky with the sun turned to it and the horizon fog, or Unity's default sky with the old sun
    //   and no fog;
    //   new camera — a narrower field of view and a weaker lens distortion, or the old wide ones.
    // The baked light is switched outside Play only (it needs a bake): DebugSettings > Baked Light, the label shows it.
    // A small label in the corner shows what is on. In a normal build everything new is on and nothing switches.
    //
    // Goes on the object with the look volume (Pistol > Create Look Volume makes it and fills the references).
    [RequireComponent(typeof(PostProcessVolume))]
    public class LookSwitcher : MonoBehaviour
    {
        [BoxGroup("Ссылки"), SerializeField, Tooltip("Переключатели «Сравнение вида»")]
        DebugSettings debug;
        [BoxGroup("Небо"), SerializeField, Tooltip("Новое небо (HDRI)")]
        Material newSky;
        [BoxGroup("Небо"), SerializeField, Tooltip("Старое небо (стандартное небо Unity)")]
        Material oldSky;
        [BoxGroup("Небо"), SerializeField, Tooltip("Солнце (Directional Light). Его поворот для нового неба берётся из сцены")]
        Light sun;
        [BoxGroup("Небо"), SerializeField, Tooltip("Поворот солнца при старом небе (как было до HDRI)")]
        Quaternion oldSunRotation = new Quaternion(0.40821788f, -0.23456968f, 0.10938163f, 0.8754261f);

        [BoxGroup("Камера"), SerializeField, Tooltip("Движение камеры: через него меняется поле зрения (с ним считается и прицеливание)")]
        CameraMotion cameraMotion;
        [BoxGroup("Камера"), SerializeField, Range(40f, 100f), Tooltip("Новое поле зрения, градусы по вертикали (70 ≈ 103° по горизонтали на 16:9)")]
        float newFieldOfView = 70f;
        [BoxGroup("Камера"), SerializeField, Range(-100f, 100f), Tooltip("Новое искажение линзы (в старом профиле 35)")]
        float newLensDistortion = 12f;

        [SerializeField, Tooltip("Клавиша: всё новое ↔ всё как было (небо, туман, запечённый свет, постобработка, камера, звук окружения)")]
        string key = "<Keyboard>/4";
        [SerializeField, Tooltip("Показывать в углу экрана, что сейчас включено")]
        bool showLabel = true;

        PostProcessVolume volume;
        InputAction toggle;
        GUIStyle style;
        Quaternion newSunRotation;
        bool newFog;
        bool? appliedPost, appliedSky, appliedBaked, appliedCamera;
        float oldFieldOfView = -1f;
        PostProcessVolume lensVolume;
        LensDistortion lens;
        bool localAllOld;   // key 4 without debug settings
        bool allOld => debug != null ? debug.AllOld : localAllOld;
        void SetAllOld(bool on) { if (debug != null) debug.SetAllOld(on); else localAllOld = on; }
        readonly System.Collections.Generic.List<(Renderer renderer, int index)> lightmapped =
            new System.Collections.Generic.List<(Renderer, int)>();

        [ShowNativeProperty] public bool AllOld => allOld;
        // What is on right now (for the performance report)
        public bool PostOn => appliedPost == true;
        public bool SkyNew => appliedSky == true;
        public bool CameraNew => appliedCamera == true;
        public bool LightmapsUsed => appliedBaked != false && LightmapSettings.lightmaps.Length > 0;
        public float LensDistortion => appliedCamera == true ? newLensDistortion : float.NaN;   // NaN: the profile's own

        void Awake()
        {
            volume = GetComponent<PostProcessVolume>();
            if (debug != null) debug.SetAllOld(false);   // the asset outlives Play sessions: start with the new look
            if (sun != null) newSunRotation = sun.transform.rotation;
            newFog = RenderSettings.fog;   // the scene is saved with the new sky and its fog
            if (DebugSettings.Allowed && (debug == null || newSky == null || oldSky == null || sun == null || cameraMotion == null))
                Debug.LogWarning("LookSwitcher: some references are empty (debug settings, skies, sun). Run Pistol > Create Look Volume and save the scene.", this);
        }

        void OnEnable()
        {
            if (!DebugSettings.Allowed) return;
            toggle = new InputAction("CompareLook", InputActionType.Button, key);
            toggle.Enable();
        }

        void OnDisable()
        {
            toggle?.Disable();
            toggle?.Dispose();
            toggle = null;
        }

        void Update()
        {
            if (toggle != null && toggle.WasPressedThisFrame()) SetAllOld(!allOld);

            // Without the debug settings the parts of the new look are simply all on
            bool post = !allOld && (debug == null || debug.NewPostFx);
            bool sky = !allOld && (debug == null || debug.NewSky);
            bool baked = !allOld;
            bool camera = !allOld && (debug == null || debug.NewCamera);
            if (appliedCamera != camera && ApplyCamera(camera)) appliedCamera = camera;
            if (lens != null) lens.intensity.value = newLensDistortion;   // tunable live
            if (appliedCamera == true && cameraMotion != null && !Mathf.Approximately(cameraMotion.FieldOfView, newFieldOfView))
                cameraMotion.SetFieldOfView(newFieldOfView);   // tunable live
            if (appliedPost != post) { volume.weight = post ? 1f : 0f; appliedPost = post; }
            if (appliedSky != sky) { ApplySky(sky); appliedSky = sky; }
            if (appliedBaked != baked) { ApplyBaked(baked); appliedBaked = baked; }
        }

        // False while the camera hasn't started yet (its field of view isn't known): tried again next frame
        bool ApplyCamera(bool useNew)
        {
            if (cameraMotion != null)
            {
                if (cameraMotion.FieldOfView <= 0f) return false;
                if (oldFieldOfView < 0f) oldFieldOfView = cameraMotion.FieldOfView;   // the profile's value
                cameraMotion.SetFieldOfView(useNew ? newFieldOfView : oldFieldOfView);
            }
            // The weaker lens distortion: a small volume above the others that overrides only it
            if (lensVolume == null && useNew)
            {
                lens = ScriptableObject.CreateInstance<LensDistortion>();
                lens.enabled.Override(true);
                lens.intensity.Override(newLensDistortion);
                lensVolume = PostProcessManager.instance.QuickVolume(gameObject.layer, 5f, lens);
            }
            if (lensVolume != null) lensVolume.enabled = useNew;
            return true;
        }

        void OnDestroy()
        {
            if (lensVolume != null) RuntimeUtilities.DestroyVolume(lensVolume, true, true);
        }

        [Button("Тест: всё новое / всё как было")]
        void TestToggleAll() { if (Application.isPlaying) SetAllOld(!allOld); }

        // No baked light for a moment: static renderers drop their lightmap and take the sky's light like before
        // the bake (a close look, not the exact scene without a bake: that is DebugSettings > Baked Light, outside Play)
        void ApplyBaked(bool on)
        {
            if (!on)
            {
                lightmapped.Clear();
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        if (r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE)
                        {
                            lightmapped.Add((r, r.lightmapIndex));
                            r.lightmapIndex = -1;
                        }
            }
            else
            {
                foreach (var (r, index) in lightmapped)
                    if (r != null) r.lightmapIndex = index;
                lightmapped.Clear();
            }
        }

        void ApplySky(bool useNew)
        {
            var material = useNew ? newSky : oldSky;
            if (material != null) RenderSettings.skybox = material;
            if (sun != null) sun.transform.rotation = useNew ? newSunRotation : oldSunRotation;
            RenderSettings.fog = useNew && newFog;
            DynamicGI.UpdateEnvironment();   // the sky light (ambient) follows the sky
        }

        void OnGUI()
        {
            if (!showLabel || !DebugSettings.Allowed) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            string text = allOld
                ? "Look: OLD  (4 = new)"
                : $"Look: NEW (4 = old)   Post: {(debug == null || debug.NewPostFx ? "on" : "off")}   Sky: {(debug == null || debug.NewSky ? "on" : "off")}   Camera: {(debug == null || debug.NewCamera ? "on" : "off")}   Sound: {(debug == null || debug.NewAcoustics ? (debug != null && debug.ForceIndoor ? "indoor" : "on") : "off")}   Baked: {(LightmapSettings.lightmaps.Length > 0 ? "on" : "off")}";
            var rect = new Rect(Screen.width - 900, 12, 888, 26);
            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            GUI.color = new Color(0.75f, 1f, 0.75f);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        // Called by Pistol > Create Look Volume
        public void Configure(DebugSettings settings, Material hdriSky, Material defaultSky, Light sunLight, CameraMotion motion)
        {
            cameraMotion = motion;
            debug = settings;
            newSky = hdriSky;
            oldSky = defaultSky;
            sun = sunLight;
        }
    }
}
