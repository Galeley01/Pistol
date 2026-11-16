using UnityEngine;

namespace Pistol.Core
{
    public static class Smoothing
    {
        // Frame-rate independent exponential approach: the higher the speed, the faster current reaches target
        public static float Damp(float current, float target, float speed, float dt)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));
    }
}
