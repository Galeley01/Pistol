using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // A plate that breaks off when hit (Texas star): it leaves the arm, gets a Rigidbody with the arm's motion
    // and its push from the bullet, and falls to the ground. While it lies there, more hits push it around (same push).
    // Reset hangs it back on the arm in its start place.
    //
    // Goes on the plate (next to its collider). TexasStarSpinner hangs it on the arm at start.
    public class DetachingPlate : ShootingTarget
    {
        [BoxGroup("Отрыв"), SerializeField, Range(0.2f, 20f), Tooltip("Масса пластины, кг")]
        float mass = 3f;
        [BoxGroup("Отрыв"), SerializeField, Range(0f, 50f), Tooltip("Толчок от пули, Н·с (сама пуля .357 даёт ~4.5)")]
        float push = 6f;

        Transform arm, looseParent;
        TexasStarSpinner spinner;
        Vector3 restPosition;
        Quaternion restRotation;
        Vector3 lastPosition;
        Rigidbody body;

        [ShowNativeProperty] public bool Detached => body != null;

        // Called by the spinner at start: the plate turns with the arm from now on
        public void HangOn(TexasStarSpinner star)
        {
            looseParent = transform.parent;   // where a broken-off plate lies (the target root, it doesn't turn)
            spinner = star;
            arm = star.transform;
            transform.SetParent(arm, true);
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            lastPosition = transform.position;
            // The plate must not catch on the arm it was hanging on
            var mine = GetComponent<Collider>();
            if (mine != null)
                foreach (var c in arm.GetComponentsInChildren<Collider>())
                    if (c != mine) Physics.IgnoreCollision(mine, c, true);
        }

        void LateUpdate()
        {
            if (body == null) lastPosition = transform.position;   // to give the plate the arm's speed when it breaks off
        }

        protected override void OnHit(in BulletHit hit)
        {
            if (body != null)
            {
                // Already lying: the plate's own push moves it (BulletSystem leaves the push of targets to them)
                body.AddForceAtPosition(hit.direction * push, hit.point, ForceMode.Impulse);
                return;
            }
            if (arm == null) return;
            Vector3 armVelocity = Time.deltaTime > 0f ? (transform.position - lastPosition) / Time.deltaTime : Vector3.zero;
            transform.SetParent(looseParent, true);
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = armVelocity;
            body.AddForceAtPosition(hit.direction * push, hit.point, ForceMode.Impulse);
            spinner.PlateDetached();
        }

        public override void ResetTarget()
        {
            if (arm == null) return;
            if (body != null) { body.isKinematic = true; Destroy(body); body = null; }   // Destroy waits for the frame end
            transform.SetParent(arm, false);
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
            lastPosition = transform.position;
        }
    }
}
