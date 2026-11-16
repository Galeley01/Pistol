using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Weapons.Revolver
{
    // Cylinder: holds the chamber logic (CylinderModel), shows rounds in 6 sockets
    // and rotates the cylinder itself on every shot.
    // Goes on Revolver. Sockets Chamber_0..5 are created inside the cylinder when the game starts.
    // The cylinder, extractor star and rod are assigned in the prefab.
    //
    // Extractor: the star (child of the cylinder) rotates with the cylinder, the rod (child of the crane)
    // stays on the crane. Both are placed by the model; the script only slides them along the cylinder axis:
    // the Eject Stroke slider (0..1) pushes the star and the rounds back and the rod with them.
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

        [BoxGroup("Гнёзда камор"), SerializeField, Tooltip("Барабан (Crane/Cylinder)")]
        Transform cylinder;
        [BoxGroup("Гнёзда камор"), Range(0.005f, 0.02f), Tooltip("Расстояние от оси барабана до оси каморы, м")]
        public float chamberRadius = 0.0100f;
        [BoxGroup("Гнёзда камор"), Range(-0.04f, 0.04f), Tooltip("Где задний торец барабана по оси Z (донца патронов), м")]
        public float rearFace = -0.0230f;
        [BoxGroup("Гнёзда камор"), Range(-180f, 180f), Tooltip("Поворот всей шестёрки гнёзд, градусы. Камора 0 должна стоять напротив ствола")]
        public float angleOffset = 0f;
        [BoxGroup("Гнёзда камор"), Tooltip("Рисовать гнёзда в окне Scene")]
        public bool showGizmos = true;

        [BoxGroup("Экстрактор"), SerializeField, Tooltip("Звезда экстрактора (Crane/Cylinder/Ejector)")]
        Transform ejector;
        [BoxGroup("Экстрактор"), SerializeField, Tooltip("Шток экстрактора (Crane/EjectorRod)")]
        Transform ejectorRod;
        [BoxGroup("Экстрактор"), Range(0f, 0.03f), Tooltip("Полный ход звезды назад, м")]
        public float ejectorTravel = 0.012f;
        [BoxGroup("Экстрактор"), Range(0f, 1f), Tooltip("Насколько выдвинута звезда: 0 — на месте, 1 — до конца. Можно двигать в Play Mode для проверки")]
        public float ejectStroke = 0f;

        // Step per chamber, taken from the animation settings (Cylinder Step)
        [ShowNativeProperty] public float StepAngle => stepAngle;

        [ShowNativeProperty] public string ChambersState => model != null ? model.Describe() : "—";
        [ShowNativeProperty] public int LiveRounds => model != null ? model.CountOf(RoundState.Live) : 0;

        public CylinderModel Model => model;
        public Transform CylinderTransform => cylinder;
        public AmmoType Ammo => ammoType;

        CylinderModel model;
        float stepAngle = -60f;
        readonly Transform[] sockets = new Transform[CylinderModel.Count];
        readonly GameObject[] liveViews = new GameObject[CylinderModel.Count];
        readonly GameObject[] spentViews = new GameObject[CylinderModel.Count];

        Animator animator;
        float indexAngle, angleFrom, angleTo, turnTime, turnDuration;

        void Awake()
        {
            if (ammoType == null) ammoType = AmmoType.CreateDefault();
            animator = GetComponent<Animator>();
            ReadStepAngle();

            model = new CylinderModel();
            model.ChamberChanged += RefreshView;
            if (cylinder != null) { BuildSockets(); StoreExtractorRest(); }
            else Debug.LogError("RevolverCylinder: Cylinder is not assigned, rounds and cylinder rotation are disabled.", this);
            if (startLoaded) model.FillAll(ammoType);
            else for (int i = 0; i < CylinderModel.Count; i++) RefreshView(i);
        }

        void ReadStepAngle()
        {
            var fire = GetComponent<RevolverFire>();
            if (fire != null && fire.animationSettings != null) stepAngle = fire.animationSettings.cylinderStep;
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
                var t = new GameObject("Chamber_" + k).transform;
                t.SetParent(cylinder, false);
                t.localPosition = SocketLocalPosition(k);
                t.localRotation = Quaternion.identity;   // +Z points from case head to bullet (towards the muzzle)
                sockets[k] = t;

                liveViews[k] = CreateView(t, ammoType.livePrefab, false);
                spentViews[k] = CreateView(t, ammoType.spentPrefab, true);
            }
        }

        Vector3 ejectorRest, ejectorRodRest;   // positions from the model, the stroke is added on top

        void StoreExtractorRest()
        {
            if (ejector == null || ejectorRod == null)
                Debug.LogWarning("RevolverCylinder: Ejector or Ejector Rod is not assigned, that part won't slide.", this);
            if (ejector != null) ejectorRest = ejector.localPosition;
            if (ejectorRod != null) ejectorRodRest = ejectorRod.localPosition;
        }

        void ApplyEjector()
        {
            float d = ejectorTravel * Mathf.Clamp01(ejectStroke);
            for (int k = 0; k < CylinderModel.Count; k++)
                if (sockets[k] != null) sockets[k].localPosition = SocketLocalPosition(k) + new Vector3(0f, 0f, -d);
            // The cylinder and the rod are not rotated relative to the crane, so the cylinder axis is local Z for both
            if (ejector != null) ejector.localPosition = ejectorRest + new Vector3(0f, 0f, -d);
            if (ejectorRod != null) ejectorRod.localPosition = ejectorRodRest + new Vector3(0f, 0f, -d);
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

        void OnDrawGizmos()
        {
            if (!showGizmos || cylinder == null) return;
            if (!Application.isPlaying) ReadStepAngle();
            var cyl = cylinder;
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
