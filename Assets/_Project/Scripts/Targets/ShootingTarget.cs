using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // Base of every target on the range: it hears bullet hits (IDamageable, called by BulletSystem)
    // and can be put back as it was (ResetTarget). Active targets are listed here, so TargetRange can reset
    // them all and play their impact sounds without searching the scene.
    //
    // Goes on the part that is hit (a plate) or on the root of a one-piece target: BulletSystem asks the hit
    // collider and its parents.
    public abstract class ShootingTarget : MonoBehaviour, IDamageable
    {
        [BoxGroup("Звук попадания"), SerializeField, Tooltip("Как звучит попадание: прямой удар и вскользь. Пусто — без звука")]
        ImpactSoundProfile impactSound;

        static readonly List<ShootingTarget> active = new List<ShootingTarget>();

        // Every enabled target
        public static IReadOnlyList<ShootingTarget> Active => active;

        // Raised on every hit of any target (TargetRange plays the impact sound)
        public static event Action<ShootingTarget, BulletHit> AnyHit;

        public ImpactSoundProfile ImpactSound => impactSound;

        // With domain reload off in the editor, statics survive between Play sessions
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearStatics()
        {
            active.Clear();
            AnyHit = null;
        }

        protected virtual void OnEnable() => active.Add(this);
        protected virtual void OnDisable() => active.Remove(this);

        public void TakeHit(in BulletHit hit)
        {
            AnyHit?.Invoke(this, hit);
            OnHit(hit);
        }

        // What this target does when a bullet hits it
        protected abstract void OnHit(in BulletHit hit);

        // Back to the state it had at the start
        public abstract void ResetTarget();
    }
}
