using UnityEngine;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // Base of every target on the range: it hears bullet hits (IDamageable, called by BulletSystem)
    // and can be put back as it was (ResetTarget). The impact sound is not the target's business:
    // BulletSystem plays it by the collider's Physics Material.
    //
    // Goes on the part that is hit (a plate) or on the root of a one-piece target: BulletSystem asks the hit
    // collider and its parents. Targets that belong to a range are children of its TargetRange.
    public abstract class ShootingTarget : MonoBehaviour, IDamageable
    {
        public void TakeHit(in BulletHit hit) => OnHit(hit);

        // What this target does when a bullet hits it
        protected abstract void OnHit(in BulletHit hit);

        // Back to the state it had at the start
        public abstract void ResetTarget();
    }
}
