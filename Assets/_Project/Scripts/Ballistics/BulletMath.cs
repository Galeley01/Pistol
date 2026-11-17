using UnityEngine;

namespace Pistol.Ballistics
{
    // The flight of a bullet, in one place: the real bullets (BulletSystem) and every preview of a future shot
    // use the same step, so a preview line always shows where the bullet will really go.
    public static class BulletMath
    {
        // Gravity that acts on a bullet of this ammo (null = plain gravity)
        public static Vector3 Gravity(AmmoType ammo) => Physics.gravity * (ammo != null ? ammo.gravityScale : 1f);

        // One time step under constant gravity. The position uses the average of the old and the new velocity,
        // which is exact for constant acceleration: the path does not depend on the frame rate.
        public static void Step(Vector3 position, Vector3 velocity, Vector3 gravity, float dt,
                                out Vector3 newPosition, out Vector3 newVelocity)
        {
            newVelocity = velocity + gravity * dt;
            newPosition = position + (velocity + newVelocity) * 0.5f * dt;
        }
    }
}
