using UnityEngine;
using NaughtyAttributes;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Pistol.Cameras;
using Pistol.Items;
using Pistol.Player;

namespace Pistol.Weapons.Revolver
{
    // Shot recoil and muzzle flash. Goes on Revolver (next to RevolverCylinder).
    // Listens to the cylinder: a shot triggers weapon and camera recoil, flash, light, smoke and sound;
    // a dry fire triggers only the sound.
    //
    // Recoil is a kick of the WEAPON (a separate stiff spring in WeaponSway, "Recoil"): the barrel flips up
    // sharply around the wrist on the grip and settles back calmly; the arms follow through IK.
    // The camera (head) jolts very slightly and rises a little. Each shot disturbs the grip (Instability),
    // so a quick follow-up shot is less accurate on its own.
    [RequireComponent(typeof(ItemRig))]
    public class RevolverFeedback : MonoBehaviour
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: отдача"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        RecoilProfile profile;
        public RecoilProfile Profile => profile;

        // TODO(stage 5-6): flash and smoke move to an FX prefab, sound to GameAudio + SoundEvent assets
        [BoxGroup("Вспышка")] public bool muzzleFlash = true;
        [BoxGroup("Вспышка"), ShowIf("muzzleFlash"), Range(0.3f, 5f), Tooltip("Размер огненной вспышки")]
        public float flashSize = 3f;
        [BoxGroup("Вспышка"), ShowIf("muzzleFlash")] public Color flashColor = new Color(0.96f, 0.66f, 0.30f, 1f);
        [BoxGroup("Вспышка"), ShowIf("muzzleFlash"), Range(0f, 30f), Tooltip("Яркость света от вспышки")]
        public float lightIntensity = 8f;
        [BoxGroup("Вспышка"), ShowIf("muzzleFlash"), Range(1f, 15f), Tooltip("Насколько далеко освещает, м")]
        public float lightRange = 6f;
        [BoxGroup("Вспышка"), ShowIf("muzzleFlash"), Range(0.02f, 0.2f), Tooltip("Сколько секунд горит свет")]
        public float lightDuration = 0.06f;

        [BoxGroup("Дым")] public bool smoke = true;
        [BoxGroup("Дым"), ShowIf("smoke"), Range(1, 40), Tooltip("Сколько клубов дыма из дула")]
        public int smokeAmount = 10;
        [BoxGroup("Дым"), ShowIf("smoke"), Range(0.3f, 4f)] public float smokeLifetime = 1.6f;
        [BoxGroup("Дым"), ShowIf("smoke"), Tooltip("Боковой выхлоп газов из зазора между барабаном и стволом — особенность револьвера")]
        public bool cylinderGapPuff = true;

        [BoxGroup("Материалы"), Tooltip("Материал огня (аддитивный). Пусто — создаётся сам, но для сборки игры лучше нажать кнопку ниже")]
        public Material flashMaterial;
        [BoxGroup("Материалы"), Tooltip("Материал дыма (полупрозрачный)")]
        public Material smokeMaterial;

        [BoxGroup("Звук"), Tooltip("Звук выстрела (необязательно)")] public AudioClip shotSound;
        [BoxGroup("Звук"), Tooltip("Звук сухого щелчка (необязательно)")] public AudioClip dryFireSound;
        [BoxGroup("Звук"), Range(0f, 1f)] public float volume = 1f;
        [BoxGroup("Звук"), Range(0f, 1f), Tooltip("0 — звук «в голове» (2D), 1 — из точки в мире (3D). Для своего оружия 0.6–0.8: слышно, что справа, но не режет одно ухо")]
        public float spatialBlend = 0.7f;
        [BoxGroup("Звук"), Range(0.1f, 10f), Tooltip("Ближе этого расстояния звук на полной громкости, м")]
        public float minDistance = 1f;
        [BoxGroup("Звук"), Range(10f, 1000f), Tooltip("Дальше этого расстояния звук больше не затихает, м. Выстрел слышно далеко")]
        public float maxDistance = 300f;
        [BoxGroup("Звук"), Tooltip("Как звук затихает с расстоянием. Logarithmic — как в жизни")]
        public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
        [BoxGroup("Звук"), Range(0f, 0.15f), Tooltip("Случайный сдвиг высоты звука, чтобы выстрелы не звучали одинаково (0.05 = ±5%)")]
        public float pitchVariation = 0.05f;

        RevolverCylinder cylinder;
        WeaponSway sway;
        WeaponAim weaponAim;
        CameraMotion cameraFx;
        PlayerMovement movement;
        Transform muzzle;
        AudioSource audioSource;

        ParticleSystem flashPs, flamePs, smokePs, gapPs;
        Light flashLight;
        float lightTimer;

        void Start()
        {
            if (profile == null) { Debug.LogError("RevolverFeedback: no profile assigned.", this); enabled = false; return; }
            cylinder = GetComponent<RevolverCylinder>();
            sway = GetComponent<WeaponSway>();
            weaponAim = GetComponent<WeaponAim>();
            movement = GetComponentInParent<PlayerMovement>();
            cameraFx = GetComponentInParent<Camera>() != null ? GetComponentInParent<Camera>().GetComponent<CameraMotion>() : null;
            var rig = ItemRig.On(this);
            muzzle = rig != null ? rig.MuzzleOrRoot : transform;

            if (cylinder != null && cylinder.Model != null)
            {
                cylinder.Model.Fired += (i, ammo) => OnFired();
                cylinder.Model.DryFired += (i, state) => OnDryFire();
            }
            else Debug.LogWarning("RevolverFeedback: no RevolverCylinder, recoil and muzzle flash won't work.");

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.dopplerLevel = 0f;   // no Doppler effect (otherwise the sound "warps" when turning)
            ApplyAudioSettings();

            BuildEffects();
        }

        // ---------- Shot ----------
        void OnFired()
        {
            float fatigue = movement != null ? movement.Fatigue : 0f;
            float m = Mathf.Lerp(1f, profile.tiredMultiplier, fatigue);

            // Weapon recoil: barrel up (-X), random side and roll; weapon back and slightly up
            if (sway != null)
            {
                float aim = weaponAim != null ? weaponAim.AimAmount : 0f;
                float flip = profile.flipAngle * Mathf.Lerp(1f, profile.aimFlipMultiplier, aim)
                           * Random.Range(1f - profile.flipVariation, 1f + profile.flipVariation);
                var rot = new Vector3(-flip,
                                      Random.Range(-profile.sideAngle, profile.sideAngle),
                                      Random.Range(-profile.rollAngle, profile.rollAngle)) * m;
                var pos = new Vector3(0f, profile.recoilUp, -profile.recoilBack) * m;
                sway.AddRecoil(rot, pos);
                sway.AddInstability(profile.gripDisturb * m);
            }

            // Camera: short kick and shake, plus a small view climb
            if (cameraFx != null)
            {
                cameraFx.AddRecoilPunch(profile.cameraFlip * m, Random.Range(-profile.cameraRollAngle, profile.cameraRollAngle));
                if (profile.cameraJolt > 0f) cameraFx.AddShake(profile.cameraJolt);
            }
            if (movement != null && profile.viewClimb > 0f)
                movement.AddRecoilLook(new Vector2(Random.Range(-profile.viewClimbSide, profile.viewClimbSide), profile.viewClimb * m));

            // Flash, light, smoke
            // Emit spawns particles immediately, even if the previous smoke is still around
            if (muzzleFlash)
            {
                flashPs.Emit(3);
                flamePs.Emit(6);
                lightTimer = lightDuration;
            }
            if (smoke) smokePs.Emit(smokeAmount);
            if (smoke && cylinderGapPuff && gapPs != null) gapPs.Emit(Mathf.Max(2, smokeAmount / 3));

            PlaySound(shotSound, volume);
        }

        void OnDryFire() => PlaySound(dryFireSound, volume * 0.8f);

        void PlaySound(AudioClip clip, float vol)
        {
            if (clip == null || audioSource == null) return;
            ApplyAudioSettings();   // so the sliders work live in Play Mode
            audioSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            audioSource.PlayOneShot(clip, vol);
        }

        void ApplyAudioSettings()
        {
            audioSource.spatialBlend = spatialBlend;
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = Mathf.Max(maxDistance, minDistance + 0.1f);
            audioSource.rolloffMode = rolloff;
        }

        void Update()
        {
            if (flashLight == null) return;
            if (lightTimer > 0f)
            {
                lightTimer -= Time.deltaTime;
                float k = Mathf.Clamp01(lightTimer / Mathf.Max(lightDuration, 0.001f));
                flashLight.enabled = true;
                flashLight.intensity = lightIntensity * k * k;
                flashLight.range = lightRange;
                flashLight.color = flashColor;
            }
            else flashLight.enabled = false;
        }

        // ---------- Effect creation ----------
        void BuildEffects()
        {
            if (flashMaterial == null) flashMaterial = CreateMaterial(true);
            if (smokeMaterial == null) smokeMaterial = CreateMaterial(false);

            // Light
            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(muzzle, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            flashLight = lightGo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            // Fire "flower" at the muzzle (local space, moves with the weapon)
            flashPs = CreateSystem("MuzzleFlash", muzzle, flashMaterial, ParticleSystemSimulationSpace.Local);
            {
                var main = flashPs.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f * flashSize, 0.13f * flashSize);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = flashColor;
                var shape = flashPs.shape; shape.enabled = false;
            }

            // Flame tongue forward along the barrel
            flamePs = CreateSystem("MuzzleFlame", muzzle, flashMaterial, ParticleSystemSimulationSpace.Local);
            {
                var main = flamePs.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f * flashSize, 0.07f * flashSize);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = flashColor;
                var shape = flamePs.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 8f;
                shape.radius = 0.003f;
                var sol = flamePs.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
            }

            // Muzzle smoke (world space, keeps hanging while the weapon moves)
            smokePs = CreateSystem("MuzzleSmoke", muzzle, smokeMaterial, ParticleSystemSimulationSpace.World);
            SetupSmoke(smokePs, new ParticleSystem.MinMaxCurve(0.3f, 1.6f), 6f);

            // Side blast from the cylinder gap: a point at the cylinder's front face, on top
            var cyl = cylinder != null ? cylinder.CylinderTransform : null;
            if (cyl != null)
            {
                var gap = new GameObject("CylinderGap").transform;
                gap.SetParent(transform, false);
                gap.position = cyl.TransformPoint(new Vector3(0f, 0.01f, 0.0235f));
                gap.localRotation = Quaternion.identity;
                gapPs = CreateSystem("CylinderGapSmoke", gap, smokeMaterial, ParticleSystemSimulationSpace.World);
                SetupSmoke(gapPs, new ParticleSystem.MinMaxCurve(0.4f, 1.2f), 80f);
                var shape = gapPs.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;   // in every direction within the gap plane
                shape.radius = 0.004f;
                shape.rotation = Vector3.zero;
                var main = gapPs.main;
                main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            }
        }

        void SetupSmoke(ParticleSystem ps, ParticleSystem.MinMaxCurve speed, float coneAngle)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime * 0.6f, smokeLifetime);
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.82f, 0.82f, 0.82f, 0.35f);
            main.gravityModifier = -0.03f;   // smoke rises slightly
            main.maxParticles = 200;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.004f;
            var lim = ps.limitVelocityOverLifetime;     // slows down quickly in the air
            lim.enabled = true;
            lim.limit = 0.25f;
            lim.dampen = 0.25f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 4f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        }

        static ParticleSystem CreateSystem(string name, Transform parent, Material mat, ParticleSystemSimulationSpace space)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;              // the system is always "playing" but emits nothing on its own;
            main.duration = 1f;            // particles are spawned only by Emit on a shot
            main.simulationSpace = space;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ---------- Materials and textures (drawn in code) ----------
        static Material CreateMaterial(bool fire)
        {
            var shader = Shader.Find(fire ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended");
            if (shader == null)
            {
                Debug.LogWarning("RevolverFeedback: particle shader not found. Press the flash/smoke materials button on the component.");
                shader = Shader.Find("Sprites/Default");
            }
            var mat = new Material(shader) { name = fire ? "MuzzleFlash (auto)" : "MuzzleSmoke (auto)" };
            mat.mainTexture = fire ? MakeFlashTexture() : MakeSmokeTexture();
            if (fire) mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return mat;
        }

        // Fire star: bright core and ragged "petals"
        public static Texture2D MakeFlashTexture(int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MuzzleFlashTex" };
            var px = new Color[size * size];
            float seed = Random.value * 10f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Atan2(v, u);
                    float petals = 0.55f + 0.45f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2.5f)), 3f) * (0.7f + 0.3f * Mathf.PerlinNoise(a * 3f + seed, 0.5f));
                    float k = Mathf.Clamp01(1f - r / petals);
                    k = k * k * (3f - 2f * k);
                    float core = Mathf.Clamp01(1f - r * 3f);
                    float val = Mathf.Clamp01(k * 0.9f + core);
                    px[y * size + x] = new Color(val, val * 0.92f, val * 0.8f, val);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // Soft smoke puff
        public static Texture2D MakeSmokeTexture(int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MuzzleSmokeTex" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.15f + 7f, y * 0.15f) * 0.4f;
                    float k = Mathf.Clamp01(1f - r);
                    k = k * k * (3f - 2f * k);
                    float alpha = Mathf.Clamp01(k * (0.55f + 0.6f * n));
                    px[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

#if UNITY_EDITOR
        // Saves the materials and textures into the project so the shaders are guaranteed to be in the build
        [Button("Создать материалы вспышки и дыма")]
        void CreateMaterialAssets()
        {
            const string dir = "Assets/Pistol/FX";
            if (!AssetDatabase.IsValidFolder("Assets/Pistol")) AssetDatabase.CreateFolder("Assets", "Pistol");
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Pistol", "FX");

            flashMaterial = SaveMaterial(dir, "M_MuzzleFlash", "Legacy Shaders/Particles/Additive", MakeFlashTexture(), true);
            smokeMaterial = SaveMaterial(dir, "M_MuzzleSmoke", "Legacy Shaders/Particles/Alpha Blended", MakeSmokeTexture(), false);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log("Muzzle flash and smoke materials created in " + dir);
        }

        static Material SaveMaterial(string dir, string name, string shaderName, Texture2D tex, bool fire)
        {
            string texPath = $"{dir}/{name}_Tex.png";
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();

            string matPath = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (fire) mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(mat);
            return mat;
        }
#endif
    }
}
