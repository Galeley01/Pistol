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
    //   new camera — a narrower field of view and a weaker lens distortion, or the old wide ones;
    //   eye adaptation — auto exposure: the picture slowly brightens when the frame is dark and darkens on a bright
    //   one (no effect in WebGL: it needs compute shaders).
    // "All old" also puts the shadows back as they were (quality: resolution, cascades, distance; the sun's bias);
    // the project's own values are restored on "new" and when Play ends.
    // The baked light is switched outside Play only (it needs a bake): Pistol > Bake Lighting / Remove Baked Lighting, the label shows it.
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

        [BoxGroup("Адаптация глаз"), SerializeField, Range(0.05f, 4f), Tooltip("Общая яркость при адаптации: больше — светлее всё. " +
            "Подбирается так, чтобы обычный вид был как без адаптации")]
        float exposureKey = 1f;
        [BoxGroup("Адаптация глаз"), SerializeField, Range(-6f, 0f), Tooltip("Насколько глаз может «высветлить» тёмный кадр, ступени (EV): -1 = до 2 раз светлее")]
        float darkestEv = -1f;
        [BoxGroup("Адаптация глаз"), SerializeField, Range(0f, 6f), Tooltip("Насколько глаз может «притемнить» яркий кадр, ступени (EV)")]
        float brightestEv = 2f;
        [BoxGroup("Адаптация глаз"), SerializeField, MinMaxSlider(1f, 99f), Tooltip("Какую часть яркостей кадра учитывать, % (от тёмных к ярким). " +
            "Низ поменьше — тёмный пистолет весомее; верх ниже 100 — солнце и блики не тянут экспозицию вниз")]
        Vector2 meteringPercent = new Vector2(10f, 85f);
        [BoxGroup("Адаптация глаз"), SerializeField, Range(0.1f, 10f), Tooltip("Скорость привыкания к темноте (картинка светлеет)")]
        float adaptToDark = 1.5f;
        [BoxGroup("Адаптация глаз"), SerializeField, Range(0.1f, 10f), Tooltip("Скорость привыкания к яркому (картинка темнеет)")]
        float adaptToBright = 3f;

        [BoxGroup("Тени (как было)"), SerializeField, Tooltip("Разрешение теней в Quality при «всё как было»")]
        ShadowResolution oldShadowResolution = ShadowResolution.Medium;
        [BoxGroup("Тени (как было)"), SerializeField, Tooltip("Каскадов теней при «всё как было» (1, 2 или 4)")]
        int oldShadowCascades = 2;
        [BoxGroup("Тени (как было)"), SerializeField, Range(10f, 200f), Tooltip("Дальность теней при «всё как было», м")]
        float oldShadowDistance = 40f;
        [BoxGroup("Тени (как было)"), SerializeField, Range(0f, 2f), Tooltip("Смещение тени солнца (Bias) при «всё как было»")]
        float oldSunBias = 0.05f;
        [BoxGroup("Тени (как было)"), SerializeField, Range(0f, 3f), Tooltip("Normal Bias тени солнца при «всё как было»")]
        float oldSunNormalBias = 0.4f;

        [SerializeField, Tooltip("Клавиша: всё новое ↔ всё как было (небо, туман, запечённый свет, постобработка, камера, звук окружения)")]
        string key = "<Keyboard>/4";
        [SerializeField, Tooltip("Показывать в углу экрана, что сейчас включено")]
        bool showLabel = true;

        PostProcessVolume volume;
        InputAction toggle;
        GUIStyle style;
        Quaternion newSunRotation;
        bool newFog;
        bool? appliedPost, appliedSky, appliedBaked, appliedCamera, appliedBlocks;

        [BoxGroup("Блоки"), SerializeField, Tooltip("Стены, платформа и кубик: при «всё как было» (4) становятся снова белыми")]
        Renderer[] restyled = new Renderer[0];
        [BoxGroup("Блоки"), SerializeField, Tooltip("Старый материал блоков (стандартный белый Unity)")]
        Material oldBlockMaterial;
        Material[] newBlockMaterials;
        float oldFieldOfView = -1f;
        PostProcessVolume lensVolume;
        LensDistortion lens;
        PostProcessVolume exposureVolume;
        AutoExposure exposure;
        bool? appliedEyes, appliedShadows;
        // The project's own (new) shadow settings, read at start (Awake): put back on "new" and when Play ends.
        // Quality settings changed in Play stay in the project in the editor, so they must always be restored.
        ShadowResolution newShadowResolution;
        int newShadowCascades;
        float newShadowDistance, newSunBias, newSunNormalBias;
        bool localAllOld;   // key 4 without debug settings
        bool allOld => debug != null ? debug.AllOld : localAllOld;
        void SetAllOld(bool on) { if (debug != null) debug.SetAllOld(on); else localAllOld = on; }
        readonly System.Collections.Generic.List<(Renderer renderer, int index)> lightmapped =
            new System.Collections.Generic.List<(Renderer, int)>();
        // The number of lightmaps of the scene, read once: LightmapSettings.lightmaps builds a new array on every call
        int lightmapCount;
        string label;          // the corner label, rebuilt only when what it shows changes
        int labelState = -1;

        [ShowNativeProperty] public bool AllOld => allOld;
        // What is on right now (for the performance report)
        public bool PostOn => appliedPost == true;
        public bool SkyNew => appliedSky == true;
        public bool CameraNew => appliedCamera == true;
        public bool LightmapsUsed => appliedBaked != false && lightmapCount > 0;
        public float LensDistortion => appliedCamera == true ? newLensDistortion : float.NaN;   // NaN: the profile's own

        void Awake()
        {
            volume = GetComponent<PostProcessVolume>();
            if (debug != null) debug.SetAllOld(false);   // the asset outlives Play sessions: start with the new look
            if (sun != null) newSunRotation = sun.transform.rotation;
            CaptureShadows();
            lightmapCount = LightmapSettings.lightmaps.Length;
            useGUILayout = false;
            newFog = RenderSettings.fog;   // the scene is saved with the new sky and its fog
            if (DebugSettings.Allowed && (debug == null || newSky == null || oldSky == null || sun == null || cameraMotion == null))
                Debug.LogWarning("LookSwitcher: some references are empty (debug settings, skies, sun). Run Pistol > Create Look Volume and save the scene.", this);
        }

        void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneChanged;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            if (!DebugSettings.Allowed) return;
            toggle = new InputAction("CompareLook", InputActionType.Button, key);
            toggle.Enable();
        }

        void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneChanged;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            toggle?.Disable();
            toggle?.Dispose();
            toggle = null;
        }

        // Another scene brings or takes away lightmaps: read their number again (not every frame: it allocates)
        void OnSceneChanged(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) =>
            lightmapCount = LightmapSettings.lightmaps.Length;
        void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => lightmapCount = LightmapSettings.lightmaps.Length;

        void Update()
        {
            if (toggle != null && toggle.WasPressedThisFrame()) SetAllOld(!allOld);

            // Without the debug settings the parts of the new look are simply all on
            bool post = !allOld && (debug == null || debug.NewPostFx);
            bool sky = !allOld && (debug == null || debug.NewSky);
            bool camera = !allOld && (debug == null || debug.NewCamera);
            if (appliedBlocks != !allOld) { ApplyBlocks(!allOld); appliedBlocks = !allOld; }
            if (appliedCamera != camera && ApplyCamera(camera)) appliedCamera = camera;
            if (lens != null) lens.intensity.value = newLensDistortion;   // tunable live
            if (appliedCamera == true && cameraMotion != null && !Mathf.Approximately(cameraMotion.FieldOfView, newFieldOfView))
                cameraMotion.SetFieldOfView(newFieldOfView);   // tunable live
            bool eyes = !allOld && (debug == null || debug.AutoExposure);
            if (appliedEyes != eyes) { ApplyEyes(eyes); appliedEyes = eyes; }
            if (exposure != null) TuneEyes();   // tunable live
            if (appliedPost != post) { volume.weight = post ? 1f : 0f; appliedPost = post; }
            if (appliedSky != sky) { ApplySky(sky); appliedSky = sky; }
            if (appliedBaked != !allOld) { ApplyBaked(!allOld); appliedBaked = !allOld; }
            if (appliedShadows != !allOld) { ApplyShadows(!allOld); appliedShadows = !allOld; }
        }

        void CaptureShadows()
        {
            newShadowResolution = QualitySettings.shadowResolution;
            newShadowCascades = QualitySettings.shadowCascades;
            newShadowDistance = QualitySettings.shadowDistance;
            if (sun != null) { newSunBias = sun.shadowBias; newSunNormalBias = sun.shadowNormalBias; }
        }

        // Shadow quality and the sun's shadow offsets: the project's own, or as they were before the new look
        void ApplyShadows(bool useNew)
        {
            QualitySettings.shadowResolution = useNew ? newShadowResolution : oldShadowResolution;
            QualitySettings.shadowCascades = useNew ? newShadowCascades : oldShadowCascades;
            QualitySettings.shadowDistance = useNew ? newShadowDistance : oldShadowDistance;
            if (sun != null)
            {
                sun.shadowBias = useNew ? newSunBias : oldSunBias;
                sun.shadowNormalBias = useNew ? newSunNormalBias : oldSunNormalBias;
            }
        }

        void OnApplicationQuit() => ApplyShadows(true);   // the editor keeps Quality changes made in Play

        void ApplyBlocks(bool useNew)
        {
            if (restyled == null || restyled.Length == 0) return;
            if (newBlockMaterials == null)
            {
                newBlockMaterials = new Material[restyled.Length];
                for (int i = 0; i < restyled.Length; i++) newBlockMaterials[i] = restyled[i] != null ? restyled[i].sharedMaterial : null;
            }
            for (int i = 0; i < restyled.Length; i++)
                if (restyled[i] != null) restyled[i].sharedMaterial = useNew ? newBlockMaterials[i] : (oldBlockMaterial != null ? oldBlockMaterial : newBlockMaterials[i]);
        }

        // Called by Pistol > Apply Range Concrete
        public void SetRestyled(Renderer[] blocks, Material oldMaterial)
        {
            restyled = blocks;
            oldBlockMaterial = oldMaterial;
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
            ApplyShadows(true);
            if (lensVolume != null) RuntimeUtilities.DestroyVolume(lensVolume, true, true);
            if (exposureVolume != null) RuntimeUtilities.DestroyVolume(exposureVolume, true, true);
        }

        // Auto exposure: a small volume above the others with only this effect
        void ApplyEyes(bool on)
        {
            if (exposureVolume == null && on)
            {
                exposure = ScriptableObject.CreateInstance<AutoExposure>();
                exposure.enabled.Override(true);
                exposure.eyeAdaptation.Override(EyeAdaptation.Progressive);
                exposure.filtering.Override(meteringPercent);
                exposure.minLuminance.Override(darkestEv);
                exposure.maxLuminance.Override(brightestEv);
                exposure.keyValue.Override(exposureKey);
                exposure.speedUp.Override(adaptToBright);   // speedUp: from dark to light
                exposure.speedDown.Override(adaptToDark);
                exposureVolume = PostProcessManager.instance.QuickVolume(gameObject.layer, 5f, exposure);
            }
            if (exposureVolume != null) exposureVolume.enabled = on;
        }

        void TuneEyes()
        {
            exposure.filtering.value = meteringPercent;
            exposure.minLuminance.value = Mathf.Min(darkestEv, brightestEv);
            exposure.maxLuminance.value = brightestEv;
            exposure.keyValue.value = exposureKey;
            exposure.speedUp.value = adaptToBright;
            exposure.speedDown.value = adaptToDark;
        }

        [Button("Тест: всё новое / всё как было")]
        void TestToggleAll() { if (Application.isPlaying) SetAllOld(!allOld); }

        // No baked light for a moment: static renderers drop their lightmap and take the sky's light like before
        // the bake (a close look, not the exact scene without a bake: that is Pistol > Remove Baked Lighting, outside Play)
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
            if (!showLabel || !DebugSettings.Allowed || Event.current.type != EventType.Repaint) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            string text = Label();
            var rect = new Rect(Screen.width - 900, 12, 888, 26);
            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            GUI.color = new Color(0.75f, 1f, 0.75f);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        // What the label shows, packed into bits: the string is built again only when one of them changes
        string Label()
        {
            bool post = debug == null || debug.NewPostFx, sky = debug == null || debug.NewSky, cam = debug == null || debug.NewCamera;
            bool sound = debug == null || debug.NewAcoustics, indoor = debug != null && debug.ForceIndoor;
            int state = (allOld ? 1 : 0) | (post ? 2 : 0) | (sky ? 4 : 0) | (cam ? 8 : 0) | (appliedEyes == true ? 16 : 0)
                      | (sound ? 32 : 0) | (indoor ? 64 : 0) | (lightmapCount > 0 ? 128 : 0);
            if (state == labelState) return label;
            labelState = state;
            string OnOff(bool on) => on ? "on" : "off";
            label = allOld
                ? "Look: OLD  (4 = new)"
                : $"Look: NEW (4 = old)   Post: {OnOff(post)}   Sky: {OnOff(sky)}   Camera: {OnOff(cam)}   " +
                  $"Eyes: {(appliedEyes == true ? (SystemInfo.supportsComputeShaders ? "on" : "n/a") : "off")}   " +
                  $"Sound: {(sound ? (indoor ? "indoor" : "on") : "off")}   Baked: {OnOff(lightmapCount > 0)}";
            return label;
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
