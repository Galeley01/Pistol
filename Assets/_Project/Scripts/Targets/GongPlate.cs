using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // The gong plate hanging on two chains: a real swing (Rigidbody + ConfigurableJoint, made at start, so the
    // prefab needs no physics setup). It swings freely forward and back; it can also twist around the vertical
    // and sway a little sideways, and the chains bring it back. So a hit in the middle swings it straight,
    // a hit near the edge also twists it. The joint is at the part's pivot (the hang point on the hooks).
    // Reset stops it hanging straight.
    //
    // Goes on the plate (GongTarget_Plate, next to its collider).
    public class GongPlate : ShootingTarget
    {
        [BoxGroup("Качание"), SerializeField, Tooltip("Ось подвеса в осях плиты (X — вдоль ширины: плита качается вперёд-назад)")]
        Vector3 axis = Vector3.right;
        [BoxGroup("Качание"), SerializeField, Range(1f, 200f), Tooltip("Масса плиты с цепями, кг")]
        float mass = 30f;
        [BoxGroup("Качание"), SerializeField, Range(0f, 200f), Tooltip("Толчок от пули, Н·с (сама пуля .357 даёт ~4.5, для тяжёлой плиты этого мало)")]
        float push = 25f;
        [BoxGroup("Качание"), SerializeField, Range(0f, 5f), Tooltip("Затухание качания: больше — быстрее успокаивается")]
        float damping = 0.25f;

        [BoxGroup("Цепи"), SerializeField, Range(0f, 80f), Tooltip("На сколько градусов плита может развернуться вокруг вертикали (удар в край)")]
        float twistLimit = 40f;
        [BoxGroup("Цепи"), SerializeField, Range(0f, 30f), Tooltip("На сколько градусов плита может качнуться вбок")]
        float sideLimit = 8f;
        [BoxGroup("Цепи"), SerializeField, Range(0f, 10f), Tooltip("Как сильно цепи возвращают разворот и боковое качание")]
        float chainReturn = 1.2f;
        [BoxGroup("Цепи"), SerializeField, Range(0f, 2f), Tooltip("Затухание разворота")]
        float twistDamping = 0.15f;

        Rigidbody body;
        Vector3 restPosition;
        Quaternion restRotation;

        void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.angularDamping = damping;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var joint = GetComponent<ConfigurableJoint>();
            if (joint == null) joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.anchor = Vector3.zero;                 // the part's pivot = the hang point
            joint.axis = axis.normalized;                // joint X: the swing axis
            joint.secondaryAxis = Vector3.up;            // joint Y: the vertical (twist)
            joint.autoConfigureConnectedAnchor = true;   // hangs on the world where it is now
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = twistLimit > 0f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;
            joint.angularZMotion = sideLimit > 0f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;
            joint.angularYLimit = new SoftJointLimit { limit = twistLimit };
            joint.angularZLimit = new SoftJointLimit { limit = sideLimit };

            // The chains turn a twisted plate back: a spring on twist and sideways sway, scaled to the plate's size
            var col0 = GetComponent<Collider>();
            float width = col0 != null ? col0.bounds.size.magnitude : 1f;
            float inertia = mass * width * width / 12f;
            float spring = inertia * 17f * chainReturn;   // ~1.5 s back and forth at chainReturn 1
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            joint.angularYZDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = 2f * Mathf.Sqrt(spring * inertia) * twistDamping,
                maximumForce = float.MaxValue
            };
            joint.targetRotation = Quaternion.identity;

            // The plate must not catch on its own frame
            var mine = GetComponent<Collider>();
            var root = transform.parent;
            if (mine != null && root != null)
                foreach (var c in root.GetComponentsInChildren<Collider>())
                    if (c != mine) Physics.IgnoreCollision(mine, c, true);
        }

        protected override void OnHit(in BulletHit hit)
        {
            if (body != null) body.AddForceAtPosition(hit.direction * push, hit.point, ForceMode.Impulse);
        }

        public override void ResetTarget()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
            body.position = transform.position;
            body.rotation = transform.rotation;
        }

        [Button("Тест: попадание в центр")]
        void TestHit()
        {
            if (!Application.isPlaying || body == null) return;
            var col = GetComponent<Collider>();
            Vector3 point = col != null ? col.bounds.center : transform.position;
            OnHit(new BulletHit { point = point, direction = transform.forward, speed = 440f });
        }
    }
}
