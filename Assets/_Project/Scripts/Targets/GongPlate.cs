using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // The gong plate hanging on chains: a real swing on a hinge (Rigidbody + HingeJoint, made at start, so the
    // prefab needs no physics setup). A hit pushes it at the hit point, it swings and settles by itself.
    // The hinge is the part's pivot (the hang point on the hooks). Reset stops it hanging straight.
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

            var hinge = GetComponent<HingeJoint>();
            if (hinge == null) hinge = gameObject.AddComponent<HingeJoint>();
            hinge.anchor = Vector3.zero;          // the part's pivot = the hang point
            hinge.axis = axis.normalized;
            hinge.autoConfigureConnectedAnchor = true;   // hangs on the world where it is now

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
            if (body == null) return;
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
