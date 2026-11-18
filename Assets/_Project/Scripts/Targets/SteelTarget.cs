using Pistol.Ballistics;

namespace Pistol.Targets
{
    // A one-piece steel target (no moving parts): rings when hit, the hole mark is left by BulletSystem.
    // Goes on the root of the target.
    public class SteelTarget : ShootingTarget
    {
        protected override void OnHit(in BulletHit hit) { }
        public override void ResetTarget() { }
    }
}
