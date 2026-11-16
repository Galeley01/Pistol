using UnityEngine;

namespace Pistol.Core
{
    public static class Spring
    {
        // Damped spring towards target, integrated in small sub-steps (stable at low frame rates)
        public static void Step(ref Vector3 x, ref Vector3 v, Vector3 target, float stiffness, float damping, float dt)
        {
            int steps = Mathf.CeilToInt(dt / 0.005f);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 a = (target - x) * stiffness - v * damping;
                v += a * h;
                x += v * h;
            }
        }
    }
}
