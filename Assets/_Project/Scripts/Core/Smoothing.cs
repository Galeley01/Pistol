using UnityEngine;

namespace Pistol.Core
{
    public static class Smoothing
    {
        // How far to move towards the target this frame (0..1): the same approach whatever the frame rate
        public static float Factor(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);

        // Frame-rate independent exponential approach: the higher the speed, the faster current reaches target
        public static float Damp(float current, float target, float speed, float dt)
            => Mathf.Lerp(current, target, Factor(speed, dt));

        public static Vector2 Damp(Vector2 current, Vector2 target, float speed, float dt)
            => Vector2.Lerp(current, target, Factor(speed, dt));

        public static Vector3 Damp(Vector3 current, Vector3 target, float speed, float dt)
            => Vector3.Lerp(current, target, Factor(speed, dt));
    }
}
