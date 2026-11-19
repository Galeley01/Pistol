using System.Collections.Generic;
using UnityEngine;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // Base of every target on the range: it hears bullet hits (IDamageable, called by BulletSystem)
    // and can be put back as it was (ResetTarget). The impact sound is not the target's business:
    // BulletSystem plays it by the collider's Physics Material.
    //
    // Goes on the part that is hit (a plate) or on the root of a one-piece target: BulletSystem asks the hit
    // collider and its parents. Every enabled target is in Active, wherever it is in the scene (TargetRange resets them).
    public abstract class ShootingTarget : MonoBehaviour, IDamageable
    {
        static readonly List<ShootingTarget> active = new List<ShootingTarget>();
        public static IReadOnlyList<ShootingTarget> Active => active;

        protected virtual void OnEnable() => active.Add(this);
        protected virtual void OnDisable() => active.Remove(this);

        // Moves or disappears in play (plates, glass): the light bake keeps it dynamic. A fixed steel plate doesn't.
        public virtual bool MovesAtRuntime => true;

        public void TakeHit(in BulletHit hit) => OnHit(hit);

        // What this target does when a bullet hits it
        protected abstract void OnHit(in BulletHit hit);

        // Back to the state it had at the start
        public abstract void ResetTarget();

        // A part moved by code (a hinged plate, the spinning arm) needs a kinematic Rigidbody: without one the physics
        // treats its collider as static, rebuilds the static scene on every move and doesn't push loose bodies with it
        protected Rigidbody MakeKinematic()
        {
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            return body;
        }
    }
}
