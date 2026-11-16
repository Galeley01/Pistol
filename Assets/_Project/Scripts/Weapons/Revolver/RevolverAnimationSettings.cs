using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace Pistol.Weapons.Revolver
{
    using Ids = RevolverAnimatorIds;

    // Revolver animation settings. Stored in an asset (Assets/Pistol/RevolverAnimationSettings.asset)
    // and shown right in the revolver's Inspector (RevolverFire > Animation Settings).
    //
    // "Create missing" creates only the .anim/Animator assets that don't exist yet. Existing ones are NOT touched.
    // "Overwrite from settings" recalculates all .anim clips from these numbers (with confirmation).
    [CreateAssetMenu(menuName = "Revolver/Animation Settings", fileName = "RevolverAnimationSettings")]
    public class RevolverAnimationSettings : ScriptableObject
    {
        [BoxGroup("Папка"), Tooltip("Куда сохранять .anim и Animator")]
        public string folder = "Assets/Pistol/Animations";

        [BoxGroup("Углы (если едет не туда — поменяйте знак)"), Tooltip("Спуск назад, ось X")]
        public float triggerPull = 25f;
        [BoxGroup("Углы (если едет не туда — поменяйте знак)"), Tooltip("Курок взведён, ось X")]
        public float hammerCock = -35f;
        [BoxGroup("Углы (если едет не туда — поменяйте знак)"), Tooltip("Поворот барабана на 1 камору, ось Z")]
        public float cylinderStep = -60f;
        [BoxGroup("Углы (если едет не туда — поменяйте знак)"), Tooltip("Откидывание барабана, ось Z")]
        public float craneOpen = 85f;

        [BoxGroup("Выстрел"), MinValue(0.02f), Tooltip("Сколько секунд взводится курок")]
        public float fireCockTime = 0.117f;
        [BoxGroup("Выстрел"), MinValue(0.005f), Tooltip("Сколько секунд падает курок")]
        public float hammerDropTime = 0.016f;
        [BoxGroup("Выстрел"), MinValue(0.05f), Tooltip("Длина всей анимации выстрела")]
        public float fireDuration = 0.30f;

        [BoxGroup("Откидывание и закрытие"), MinValue(0.1f)] public float openDuration = 0.45f;
        [BoxGroup("Откидывание и закрытие"), Tooltip("Отскок при откидывании, градусы")] public float openOvershoot = 6f;
        [BoxGroup("Откидывание и закрытие"), MinValue(0.1f)] public float closeDuration = 0.30f;

        [BoxGroup("Раскрутка барабана"), MinValue(1)] public int spinTurns = 2;
        [BoxGroup("Раскрутка барабана"), MinValue(0.2f)] public float spinDuration = 1.2f;

        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f), Tooltip("Множитель скорости, применяется в игре без пересоздания анимаций")]
        public float fireSpeed = 1f;
        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float openSpeed = 1f;
        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float closeSpeed = 1f;
        [BoxGroup("Скорость анимаций"), Range(0.25f, 3f)] public float spinSpeed = 1f;

        // Paths to parts inside Revolver
        public const string TRIGGER = "Trigger", HAMMER = "Hammer", CRANE = "Crane", CYLINDER = "Crane/Cylinder";

        [Button("Создать недостающие анимации")]
        public void CreateMissingButton()
        {
#if UNITY_EDITOR
            Generate(false, null);
#endif
        }

        [Button("Перезаписать анимации из этих настроек")]
        void OverwriteFromAsset() => OverwriteButton(null);

        // target: the revolver that gets the generated Animator Controller (null = only create the assets)
        public void OverwriteButton(RevolverFire target)
        {
#if UNITY_EDITOR
            if (EditorUtility.DisplayDialog("Перезаписать анимации?",
                "Все .anim револьвера будут пересчитаны по этим настройкам. Ручные правки в окне Animation пропадут.",
                "Перезаписать", "Отмена"))
                Generate(true, target);
#endif
        }

#if UNITY_EDITOR
        const string DefaultAssetPath = "Assets/Pistol/RevolverAnimationSettings.asset";

        [MenuItem("Tools/Revolver/Создать недостающие анимации")]
        static void MenuCreateMissing() => GetOrCreateDefault().Generate(false, null);

        public static RevolverAnimationSettings GetOrCreateDefault()
        {
            var guids = AssetDatabase.FindAssets("t:RevolverAnimationSettings");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<RevolverAnimationSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            EnsureFolder("Assets/Pistol");
            var s = CreateInstance<RevolverAnimationSettings>();
            AssetDatabase.CreateAsset(s, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        // target: the revolver that gets the generated Animator Controller (null = only create the assets)
        public void Generate(bool overwrite, RevolverFire target)
        {
            EnsureFolder(folder);
            float drop = fireCockTime + hammerDropTime;
            float spinAngle = Mathf.Sign(cylinderStep) * 360f * spinTurns;
            var made = new List<string>();

            var idle = Clip("Revolver_Idle", overwrite, made, c =>
            {
                Rot(c, TRIGGER, 'x', 1 / 60f, K(0, 0)); Rot(c, HAMMER, 'x', 1 / 60f, K(0, 0));
                Rot(c, CRANE, 'z', 1 / 60f, K(0, 0)); Rot(c, CYLINDER, 'z', 1 / 60f, K(0, 0));
            });

            var fire = Clip("Revolver_Fire", overwrite, made, c =>
            {
                float d = fireDuration;
                Rot(c, TRIGGER, 'x', d, new Key(0, 0), new Key(fireCockTime, triggerPull), new Key(d, 0));
                Rot(c, HAMMER, 'x', d, new Key(0, 0), new Key(fireCockTime, hammerCock, true),
                                       new Key(drop, 0, true), new Key(d, 0));
                Rot(c, CYLINDER, 'z', d, new Key(0, 0), new Key(fireCockTime, cylinderStep), new Key(d, cylinderStep));
                Rot(c, CRANE, 'z', d, K(0, 0));
                Event(c, drop, nameof(RevolverFire.OnShot));
            });

            var open = Clip("Revolver_Open", overwrite, made, c =>
            {
                float d = openDuration;
                Rot(c, CRANE, 'z', d, new Key(0, 0), new Key(d * 0.18f, 0), new Key(d * 0.67f, craneOpen + openOvershoot),
                                      new Key(d * 0.84f, craneOpen - openOvershoot * 0.5f), new Key(d, craneOpen));
                Rot(c, CYLINDER, 'z', d, K(0, 0)); Rot(c, TRIGGER, 'x', d, K(0, 0)); Rot(c, HAMMER, 'x', d, K(0, 0));
            });

            var openIdle = Clip("Revolver_OpenIdle", overwrite, made, c =>
            {
                Rot(c, CRANE, 'z', 1 / 60f, K(0, craneOpen)); Rot(c, CYLINDER, 'z', 1 / 60f, K(0, 0));
                Rot(c, TRIGGER, 'x', 1 / 60f, K(0, 0)); Rot(c, HAMMER, 'x', 1 / 60f, K(0, 0));
            });

            var spin = Clip("Revolver_Spin", overwrite, made, c =>
            {
                float d = spinDuration;
                Rot(c, CRANE, 'z', d, K(0, craneOpen));
                Rot(c, CYLINDER, 'z', d, new Key(0, 0), new Key(d * 0.125f, spinAngle * 0.28f),
                                         new Key(d * 0.46f, spinAngle * 0.78f), new Key(d, spinAngle));
                Event(c, 0.02f, nameof(RevolverFire.OnSpinStart));
            });

            var close = Clip("Revolver_Close", overwrite, made, c =>
            {
                float d = closeDuration;
                Rot(c, CRANE, 'z', d, new Key(0, craneOpen), new Key(d * 0.57f, -3f * Mathf.Sign(craneOpen), true),
                                      new Key(d * 0.77f, 1f * Mathf.Sign(craneOpen)), new Key(d, 0));
                Rot(c, CYLINDER, 'z', d, K(0, 0)); Rot(c, TRIGGER, 'x', d, K(0, 0)); Rot(c, HAMMER, 'x', d, K(0, 0));
                Event(c, d * 0.57f, nameof(RevolverFire.OnCylinderClosed));
            });

            var ctrl = GetOrCreateController(idle, fire, open, openIdle, spin, close, made);
            AssetDatabase.SaveAssets();

            if (target != null)
            {
                var anim = target.GetComponent<Animator>();
                if (anim.runtimeAnimatorController != ctrl)
                {
                    Undo.RecordObject(anim, "Assign Revolver Controller");
                    anim.runtimeAnimatorController = ctrl;
                    EditorUtility.SetDirty(anim);
                }
            }
            else Debug.Log("Revolver: Animator Controller not assigned to any revolver. Use the animation buttons on RevolverFire to assign it.");

            Debug.Log(made.Count == 0
                ? "Revolver: everything already exists, nothing overwritten."
                : "Revolver: " + (overwrite ? "updated: " : "created: ") + string.Join(", ", made));
        }

        // ---------- Clips ----------
        struct Key
        {
            public float time, value; public bool linear;
            public Key(float t, float v, bool lin = false) { time = t; value = v; linear = lin; }
        }
        static Key[] K(float t, float v) => new[] { new Key(t, v) };

        AnimationClip Clip(string name, bool overwrite, List<string> made, System.Action<AnimationClip> fill)
        {
            string path = $"{folder}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && !overwrite) return clip;          // already exists, leave it alone

            if (clip == null)
            {
                clip = new AnimationClip { name = name, frameRate = 60 };
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                clip.ClearCurves();                                // overwrite in place: Animator references are kept
                AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            }
            fill(clip);
            var st = AnimationUtility.GetAnimationClipSettings(clip);
            st.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            EditorUtility.SetDirty(clip);
            made.Add(name);
            return clip;
        }

        static void Rot(AnimationClip clip, string path, char axis, float length, params Key[] keys)
        {
            float end = Mathf.Max(length, keys[keys.Length - 1].time);
            foreach (char a in "xyz")
            {
                var curve = new AnimationCurve();
                if (a == axis)
                {
                    foreach (var k in keys) curve.AddKey(new Keyframe(k.time, k.value));
                    if (keys.Length == 1) curve.AddKey(new Keyframe(end, keys[0].value));
                    for (int i = 0; i < curve.length; i++)
                    {
                        bool lin = i < keys.Length && keys[i].linear;
                        var tm = lin ? AnimationUtility.TangentMode.Linear : AnimationUtility.TangentMode.ClampedAuto;
                        AnimationUtility.SetKeyLeftTangentMode(curve, i, tm);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, tm);
                    }
                }
                else { curve.AddKey(0, 0); curve.AddKey(end, 0); }
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw." + a), curve);
            }
        }

        static void Event(AnimationClip clip, float time, string function)
        {
            var list = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip))
                { new AnimationEvent { time = time, functionName = function } };
            AnimationUtility.SetAnimationEvents(clip, list.ToArray());
        }

        // ---------- Animator ----------
        AnimatorController GetOrCreateController(AnimationClip idle, AnimationClip fire, AnimationClip open,
            AnimationClip openIdle, AnimationClip spin, AnimationClip close, List<string> made)
        {
            string path = $"{folder}/Revolver.controller";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null)
            {
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
                foreach (var p in Ids.Triggers)
                    ctrl.AddParameter(p, AnimatorControllerParameterType.Trigger);

                var sm = ctrl.layers[0].stateMachine;
                var sIdle = State(sm, Ids.IdleState, idle, new Vector3(300, 0));
                var sFire = State(sm, Ids.FireState, fire, new Vector3(300, -120));
                var sOpen = State(sm, Ids.OpenState, open, new Vector3(560, 0));
                var sOpenIdle = State(sm, Ids.OpenIdleState, openIdle, new Vector3(820, 0));
                var sSpin = State(sm, Ids.SpinState, spin, new Vector3(820, -120));
                var sClose = State(sm, Ids.CloseState, close, new Vector3(560, 120));
                sm.defaultState = sIdle;

                ByTrigger(sIdle, sFire, Ids.Fire); ByExit(sFire, sIdle);
                ByTrigger(sIdle, sOpen, Ids.Open); ByExit(sOpen, sOpenIdle);
                ByTrigger(sOpenIdle, sSpin, Ids.Spin); ByExit(sSpin, sOpenIdle);
                ByTrigger(sOpenIdle, sClose, Ids.Close);
                ByTrigger(sSpin, sClose, Ids.Close); ByExit(sClose, sIdle);
                made.Add("Revolver.controller");
            }

            // Soft update of an existing Animator: add speed parameters, remove nothing
            bool changed = false;
            foreach (var p in Ids.SpeedParams)
            {
                bool has = false;
                foreach (var e in ctrl.parameters) if (e.name == p) has = true;
                if (!has)
                {
                    ctrl.AddParameter(new AnimatorControllerParameter
                        { name = p, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
                    changed = true;
                }
            }
            foreach (var cs in ctrl.layers[0].stateMachine.states)
            {
                string param = Ids.SpeedParamFor(cs.state.name);
                if (param != null && !cs.state.speedParameterActive)
                {
                    cs.state.speedParameterActive = true;
                    cs.state.speedParameter = param;
                    changed = true;
                }
            }
            if (changed) { EditorUtility.SetDirty(ctrl); made.Add("speed parameters in Animator"); }
            return ctrl;
        }

        // Names the game expects that are missing from the controller (empty = everything is in place)
        public static List<string> FindMissing(RuntimeAnimatorController runtimeController)
        {
            var missing = new List<string>();
            var ctrl = runtimeController as AnimatorController;
            if (ctrl == null) { missing.Add("Animator Controller is not assigned"); return missing; }
            var parameters = new HashSet<string>();
            foreach (var p in ctrl.parameters) parameters.Add(p.name);
            foreach (var p in Ids.Triggers) if (!parameters.Contains(p)) missing.Add("trigger " + p);
            foreach (var p in Ids.SpeedParams) if (!parameters.Contains(p)) missing.Add("float " + p);
            var states = new HashSet<string>();
            foreach (var cs in ctrl.layers[0].stateMachine.states) states.Add(cs.state.name);
            foreach (var s in Ids.States) if (!states.Contains(s)) missing.Add("state " + s);
            return missing;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion m, Vector3 pos)
        {
            var s = sm.AddState(name, pos); s.motion = m; return s;
        }

        static void ByTrigger(AnimatorState from, AnimatorState to, string trigger)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false; t.duration = 0; t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        }

        static void ByExit(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true; t.exitTime = 1f; t.duration = 0;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
#endif
    }
}
