
namespace Pistol.Ballistics
{
    // Anything that can be hit and wants to know about it (target, enemy, bottle) implements this interface.
    public interface IDamageable
    {
        void TakeHit(in BulletHit hit);
    }
}
