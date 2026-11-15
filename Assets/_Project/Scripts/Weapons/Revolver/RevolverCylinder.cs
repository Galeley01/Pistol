using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Weapons.Revolver
{
    // Cylinder: holds the chamber logic (CylinderModel), shows rounds in 6 sockets
    // and rotates the cylinder itself on every shot.
    // Goes on Revolver. Sockets Chamber_0..5 are created inside Crane/Cylinder when the game starts.
    //
    // Extractor: the model has two parts, the star (Crane/Ejector) and the rod (Crane/EjectorRod).
    // Their meshes are built in cylinder space, but their FBX placement is unreliable,
    // so the script places them by the cylinder: the star goes inside the cylinder (rotates with it),
    // the rod stays on Crane at the cylinder's position (doesn't rotate). Outside Play Mode use the "place star and rod" button.
    // The Eject Stroke slider (0..1) pushes the star and the rounds back and the rod inwards.
    //
    // Why the script rotates the cylinder instead of the animation: the Fire clip rotates it 0 -> -60 degrees
    // and then returns to 0. With an empty cylinder that's invisible (it's symmetric), but with rounds
    // they would "jump back". So the script keeps the accumulated angle (chamber x 60 degrees) itself
    // and takes only the spin from the animation (Spin).
    [DefaultExecutionOrder(100)]
    public class RevolverCylinder : MonoBehaviour
    {
        [BoxGroup("Патроны"), Expandable, Tooltip("Тип патрона. Пусто — .357 Magnum по умолчанию")]
        public AmmoType ammoType;
        [BoxGroup("Патроны"), Tooltip("Зарядить все 6 камор при запуске")]
        public bool startLoaded = true;

        [BoxGroup("Гнёзда камор"), Tooltip("Барабан (найдётся сам: Crane/Cylinder)")]
        public Transform cylinder;
        [BoxGroup("Гнёзда камор"), Range(0.005f, 0.02f), Tooltip("Расстояние от оси барабана до оси каморы, м")]
        public float chamberRadius = 0.0100f;
        [BoxGroup("Гнёзда камор"), Range(-0.04f, 0.04f), Tooltip("Где задний торец барабана по оси Z (донца патронов), м")]
        public float rearFace = -0.0230f;
        [BoxGroup("Гнёзда камор"), Range(-180f, 180f), Tooltip("Поворот всей шестёрки гнёзд, градусы. Камора 0 должна стоять напротив ствола")]
        public float angleOffset = 0f;
        [BoxGroup("Гнёзда камор"), Tooltip("Рисовать гнёзда в окне Scene")]
        public bool showGizmos = true;

        [BoxGroup("Экстрактор"), Tooltip("Звезда экстрактора (найдётся сама: Crane/Ejector)")]
        public Transform ejector;
        [BoxGroup("Экстрактор"), Tooltip("Шток экстрактора (найдётся сам: Crane/EjectorRod)")]
        public Transform ejectorRod;
        [BoxGroup("Экстрактор"), Range(0f, 0.03f), Tooltip("Полный ход звезды назад, м")]
        public float ejectorTravel = 0.012f;
        [BoxGroup("Экстрактор"), Range(0f, 1f), Tooltip("Насколько выдвинута звезда: 0 — на месте, 1 — до конца. Можно двигать в Play Mode для проверки")]
        public float ejectStroke = 0f;

        [BoxGroup("Поворот"), Tooltip("Шаг поворота на одну камору (берётся из настроек анимаций, обычно −60)")]
        public float stepAngle = -60f;

        [ShowNativeProperty] public string ChambersState => model != null ? model.Describe() : "—";
        [ShowNativeProperty] public int LiveRounds => model != null ? model.CountOf(RoundState.Live) : 0;

        public CylinderModel Model => model;
        public AmmoType Ammo => ammoType;

        CylinderModel model;
        readonly Transform[] sockets = new Transform[CylinderModel.Count];
        readonly GameObject[] liveViews = new GameObject[CylinderModel.Count];
        readonly GameObject[] spentViews = new GameObject[CylinderModel.Count];

        Animator animator;
        float indexAngle, angleFrom, angleTo, turnTime, turnDuration;

        void Awake()
        {
            if (ammoType == null) ammoType = AmmoType.CreateDefault();
            if (cylinder == null) cylinder = transform.Find("Crane/Cylinder");
            animator = GetComponent<Animator>();

            var fire = GetComponent<RevolverFire>();
            if (fire != null && fire.animationSettings != null) stepAngle = fire.animationSettings.cylinderStep;

            model = new CylinderModel();
            model.ChamberChanged += RefreshView;
            if (cylinder != null) { BuildSockets(); SetupEjector(); }
            if (startLoaded) model.FillAll(ammoType);
            else for (int i = 0; i < CylinderModel.Count; i++) RefreshView(i);
        }

        // ---------- Sockets and visuals ----------
        // Chamber k sits at angle 90 - step*k: chamber 0 is at the top, aligned with the barrel,
        // and one step of rotation brings chamber 1 under the hammer.
        Vector3 SocketLocalPosition(int k)
        {
            float a = (90f - stepAngle * k + angleOffset) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * chamberRadius, Mathf.Sin(a) * chamberRadius, rearFace);
        }

        void BuildSockets()
        {
            for (int k = 0; k < CylinderModel.Count; k++)
            {
                string n = "Chamber_" + k;
                var t = cylinder.Find(n);
                if (t == null)
                {
                    t = new GameObject(n).transform;
                    t.SetParent(cylinder, false);
                }
                t.localPosition = SocketLocalPosition(k);
                t.localRotation = Quaternion.identity;   // +Z points from case head to bullet (towards the muzzle)
                sockets[k] = t;

                liveViews[k] = CreateView(t, ammoType.livePrefab, false);
                spentViews[k] = CreateView(t, ammoType.spentPrefab, true);
            }
        }

        void SetupEjector()
        {
            if (ejector == null) ejector = transform.Find("Crane/Ejector");
            if (ejectorRod == null) ejectorRod = transform.Find("Crane/EjectorRod");
            if (ejector != null)
            {
                // The star mesh is already in cylinder space, so inside the cylinder it sits at zero
                ejector.SetParent(cylinder, false);
                ejector.localPosition = Vector3.zero;
                ejector.localRotation = Quaternion.identity;
                ejector.localScale = Vector3.one;
            }
            if (ejectorRod != null && cylinder.parent != null)
            {
                ejectorRod.SetParent(cylinder.parent, false);
                ejectorRod.localScale = cylinder.localScale;
            }
        }

        void ApplyEjector()
        {
            float d = ejectorTravel * Mathf.Clamp01(ejectStroke);
            for (int k = 0; k < CylinderModel.Count; k++)
                if (sockets[k] != null) sockets[k].localPosition = SocketLocalPosition(k) + new Vector3(0f, 0f, -d);
            if (ejector != null) ejector.localPosition = new Vector3(0f, 0f, -d);
            // The rod sits where the cylinder is, without its rotation, and slides inwards along the axis
            if (ejectorRod != null)
            {
                ejectorRod.localRotation = Quaternion.identity;
                // Inside Crane the cylinder is rotated only around its own Z axis, so "no rotation" means identity
                ejectorRod.localPosition = cylinder.localPosition + new Vector3(0f, 0f, -d * cylinder.localScale.z);
            }
        }

        GameObject CreateView(Transform socket, GameObject prefab, bool spent)
        {
            GameObject go;
            bool spentFromLive = spent && prefab == null && ammoType.livePrefab != null;
            if (spentFromLive) prefab = ammoType.livePrefab;
            if (prefab != null)
            {
                go = Instantiate(prefab, socket);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.Euler(ammoType.modelRotation);
                go.transform.localScale *= ammoType.modelScale;
                if (spentFromLive)
                {
                    // Spent case = the same cartridge without the bullet, slightly darker
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("Bullet")) t.gameObject.SetActive(false);
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", ammoType.spentTint);
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
                }
            }
            else
            {
                // Placeholder: brass cylinder the size of a .357 round (9.6 mm diameter, 39 mm long; case 33 mm)
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = spent ? "Spent_Placeholder" : "Live_Placeholder";
                Destroy(go.GetComponent<Collider>());
                float len = spent ? 0.033f : 0.039f;
                go.transform.SetParent(socket, false);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // cylinder axis Y -> socket +Z
                go.transform.localPosition = new Vector3(0f, 0f, len * 0.5f);
                go.transform.localScale = new Vector3(0.0096f, len * 0.5f, 0.0096f);
                var r = go.GetComponent<Renderer>();
                r.material.color = spent ? new Color(0.42f, 0.33f, 0.17f) : new Color(0.8f, 0.62f, 0.27f);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        void RefreshView(int i)
        {
            if (liveViews[i] == null) return;
            var s = model.GetState(i);
            liveViews[i].SetActive(s == RoundState.Live);
            spentViews[i].SetActive(s == RoundState.Spent);
        }

        public Transform GetSocket(int i) => sockets[CylinderModel.Wrap(i)];

        // ---------- Cylinder rotation ----------
        // Called on trigger pull: the cylinder turns one chamber while the hammer cocks
        public void AdvanceForShot(float duration)
        {
            model.Advance(1);
            angleFrom = indexAngle;
            angleTo += stepAngle;            // always exactly one chamber in the step direction
            turnTime = 0f;
            turnDuration = Mathf.Max(duration, 0.01f);
        }

        // The hammer fell
        public bool TryFire(out AmmoType firedAmmo) => model.TryFire(out firedAmmo);

        void LateUpdate()
        {
            if (cylinder == null) return;

            if (turnTime < turnDuration)
            {
                turnTime += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(turnTime / turnDuration));
                indexAngle = Mathf.Lerp(angleFrom, angleTo, t);
                if (turnTime >= turnDuration)
                {
                    angleTo = Mathf.Repeat(angleTo, 360f);   // keep the angle from growing forever
                    indexAngle = angleTo;
                }
            }

            // Take only the open-cylinder spin from the animation; the Fire clip rotation is replaced by our own angle
            float spinAngle = 0f;
            if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("Spin"))
                spinAngle = Mathf.DeltaAngle(0f, cylinder.localEulerAngles.z);

            cylinder.localRotation = Quaternion.Euler(0f, 0f, indexAngle + spinAngle);
            ApplyEjector();
        }

        // ---------- Test buttons (Play Mode) ----------
        [Button("Зарядить все")] void BtnFill() { if (model != null) model.FillAll(ammoType); }
        [Button("Все — стреляные гильзы")] void BtnSpend() { if (model != null) model.SpendAll(); }
        [Button("Опустошить")] void BtnEmpty() { if (model != null) model.EmptyAll(); }

        // Outside Play Mode: puts the star and rod where the cylinder is (so they don't hang off to the side in the Scene view).
        // In Play Mode the script does this itself.
        [Button("Поставить звезду и шток на место")]
        void BtnPlaceEjector()
        {
            var cyl = cylinder != null ? cylinder : transform.Find("Crane/Cylinder");
            if (cyl == null) { Debug.LogWarning("Crane/Cylinder not found"); return; }
            foreach (var name in new[] { "Crane/Ejector", "Crane/EjectorRod" })
            {
                var t = transform.Find(name);
                if (t == null) t = cyl.Find(name.Substring(6));
                if (t == null) { Debug.LogWarning(name + " not found. Is Revolver.fbx up to date?"); continue; }
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(t, "Place ejector");
#endif
                if (t.parent == cyl) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
                else { t.localPosition = cyl.localPosition; t.localRotation = cyl.localRotation; t.localScale = cyl.localScale; }
            }
        }

        void OnDrawGizmos()
        {
            if (!showGizmos) return;
            var cyl = cylinder != null ? cylinder : transform.Find("Crane/Cylinder");
            if (cyl == null) return;
            for (int k = 0; k < CylinderModel.Count; k++)
            {
                Vector3 p = cyl.TransformPoint(SocketLocalPosition(k));
                Gizmos.color = k == 0 ? Color.red : new Color(1f, 0.85f, 0.3f);
                Gizmos.DrawWireSphere(p, 0.0048f);
                Gizmos.DrawLine(p, p + cyl.forward * 0.039f);
            }
        }
    }
}
