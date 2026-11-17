using UnityEditor;
using UnityEngine;
using Pistol.Items;
using Pistol.Weapons;
using Pistol.Weapons.Revolver;

namespace Pistol.EditorTools
{
    // Builds the revolver's shot effects as a prefab (MuzzleFx_Revolver) with the values that used to be
    // built in code by RevolverFeedback, and nests it under the muzzle of the Revolver prefab.
    // Run once (menu Pistol > Build Revolver Muzzle FX); after that the effects are tuned in the prefab itself.
    // Running it again rebuilds the FX prefab from these defaults.
    public static class MuzzleFxBuilder
    {
        const string RevolverPrefab = "Assets/_Project/Weapons/Revolver/Revolver.prefab";
        const string FxFolder = "Assets/_Project/Weapons/Revolver";
        const string FxPath = FxFolder + "/MuzzleFx_Revolver.prefab";
        const string FlashMaterialGuid = "7b9db86482394ab43b8db2a046cc4af0";   // M_MuzzleFlash
        const string SmokeMaterialGuid = "6533366364c212e4780fee9b5bf183e0";   // M_MuzzleSmoke

        // The values the revolver had on RevolverFeedback
        const float FlashSize = 3f;
        static readonly Color FlashColor = new Color(0.9528302f, 0.65997773f, 0.30113026f, 1f);
        const float LightIntensity = 8f, LightRange = 6f, LightDuration = 0.06f;
        const int SmokeAmount = 5;
        const float SmokeLifetime = 1.6f;
        static readonly Vector3 GapPointOnCylinder = new Vector3(0f, 0.01f, 0.0235f);

        [MenuItem("Pistol/Build Revolver Muzzle FX")]
        static void Build()
        {
            if (Application.isPlaying) { Debug.LogWarning("Build the muzzle FX outside Play Mode."); return; }

            var flashMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(FlashMaterialGuid));
            var smokeMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SmokeMaterialGuid));
            if (flashMat == null || smokeMat == null) { Debug.LogError("Muzzle flash / smoke materials not found."); return; }

            var contents = PrefabUtility.LoadPrefabContents(RevolverPrefab);
            try
            {
                var rig = contents.GetComponent<ItemRig>();
                var feedback = contents.GetComponent<RevolverFeedback>();
                var cylinder = contents.GetComponent<RevolverCylinder>();
                if (rig == null || rig.Muzzle == null || feedback == null)
                {
                    Debug.LogError("Revolver prefab needs ItemRig with a Muzzle and RevolverFeedback.");
                    return;
                }
                var muzzle = rig.Muzzle;

                // Where the side blast comes from, in muzzle space
                Vector3? gapLocal = null;
                if (cylinder != null && cylinder.CylinderTransform != null)
                    gapLocal = muzzle.InverseTransformPoint(cylinder.CylinderTransform.TransformPoint(GapPointOnCylinder));

                var fxPrefab = BuildFxPrefab(flashMat, smokeMat, gapLocal);

                // Replace an older nested copy, then nest the prefab under the muzzle
                var old = muzzle.Find(fxPrefab.name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(fxPrefab, muzzle);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                var so = new SerializedObject(feedback);
                so.FindProperty("muzzleFx").objectReferenceValue = instance.GetComponent<MuzzleFx>();
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, RevolverPrefab);
                Debug.Log($"Muzzle FX built ({FxPath}) and nested under the revolver muzzle.", fxPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static GameObject BuildFxPrefab(Material flashMat, Material smokeMat, Vector3? gapLocal)
        {
            var root = new GameObject("MuzzleFx_Revolver");
            try
            {
                var fx = root.AddComponent<MuzzleFx>();

                var lightGo = new GameObject("MuzzleLight");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.range = LightRange;
                light.color = FlashColor;
                light.intensity = 0f;
                light.enabled = false;

                // Fire "flower" at the muzzle (local space, moves with the weapon)
                var flash = CreateSystem("MuzzleFlash", root.transform, flashMat, ParticleSystemSimulationSpace.Local);
                {
                    var main = flash.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.07f * FlashSize, 0.13f * FlashSize);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = FlashColor;
                    var shape = flash.shape; shape.enabled = false;
                }

                // Flame tongue forward along the barrel
                var flame = CreateSystem("MuzzleFlame", root.transform, flashMat, ParticleSystemSimulationSpace.Local);
                {
                    var main = flame.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f * FlashSize, 0.07f * FlashSize);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = FlashColor;
                    var shape = flame.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 8f;
                    shape.radius = 0.003f;
                    var sol = flame.sizeOverLifetime;
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
                }

                // Muzzle smoke (world space, keeps hanging while the weapon moves)
                var smoke = CreateSystem("MuzzleSmoke", root.transform, smokeMat, ParticleSystemSimulationSpace.World);
                SetupSmoke(smoke, new ParticleSystem.MinMaxCurve(0.3f, 1.6f), 6f);

                // Side blast from the cylinder gap: in every direction within the gap plane
                ParticleSystem side = null;
                if (gapLocal.HasValue)
                {
                    side = CreateSystem("CylinderGapSmoke", root.transform, smokeMat, ParticleSystemSimulationSpace.World);
                    side.transform.localPosition = gapLocal.Value;
                    SetupSmoke(side, new ParticleSystem.MinMaxCurve(0.4f, 1.2f), 80f);
                    var shape = side.shape;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.004f;
                    shape.rotation = Vector3.zero;
                    var main = side.main;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                }

                fx.SetParts(flash, flame, smoke, side, light);
                fx.flashCount = 3;
                fx.flameCount = 6;
                fx.smokeCount = SmokeAmount;
                fx.sideSmokeCount = Mathf.Max(2, SmokeAmount / 3);
                fx.lightIntensity = LightIntensity;
                fx.lightDuration = LightDuration;

                if (!AssetDatabase.IsValidFolder(FxFolder)) AssetDatabase.CreateFolder("Assets/_Project/Weapons", "Revolver");
                return PrefabUtility.SaveAsPrefabAsset(root, FxPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void SetupSmoke(ParticleSystem ps, ParticleSystem.MinMaxCurve speed, float coneAngle)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(SmokeLifetime * 0.6f, SmokeLifetime);
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

        // Always "playing" with zero emission: particles are spawned only by MuzzleFx.Play on a shot
        static ParticleSystem CreateSystem(string name, Transform parent, Material mat, ParticleSystemSimulationSpace space)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = space;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }
    }
}
