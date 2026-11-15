using UnityEngine;

namespace Pistol.Core
{
    // Recoil spring: sharp rise and calm return.
    // While moving away from zero it is a stiff spring without overshoot (peak exactly after riseTime);
    // on the way back it is softer, takes returnTime, with the given damping.
    public static class RecoilSpring
    {
        // Kick velocity that makes the spring rise exactly to peak
        public static float ImpulseFor(float peak, float riseTime) => peak * 2.7182818f / Mathf.Max(riseTime, 0.005f);

        public static void Step(ref Vector3 x, ref Vector3 v, float riseTime, float returnTime, float returnDamping, float dt)
        {
            if (dt <= 0f) return;
            float wr = 1f / Mathf.Max(riseTime, 0.005f);
            float wb = 3.9f / Mathf.Max(returnTime, 0.02f);   // critical damping: 90% of the way in ~3.9/omega
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.002f));
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < 3; i++)
                {
                    float xi = x[i], vi = v[i];
                    bool outward = xi * vi > 0f || (Mathf.Abs(xi) < 1e-7f && Mathf.Abs(vi) > 1e-6f);
                    float a = outward ? (-wr * wr * xi - 2f * wr * vi)
                                      : (-wb * wb * xi - 2f * returnDamping * wb * vi);
                    vi += a * h;
                    xi += vi * h;
                    x[i] = xi; v[i] = vi;
                }
            }
        }
    }
}
