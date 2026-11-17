using UnityEngine;

namespace Pistol.Core
{
    // How one shot's recoil moves over time, as a fraction of its peak (1 = top of the flip).
    // Pure math, no state: the caller keeps the time since the shot.
    //
    //   Rise    0 .. Rise              sharp ease-out up to 1 (the kick)
    //   Fall    .. + Fall              smooth down through zero to -Nod (the heavy gun "nods" below the start)
    //   Settle  .. + Settle            slow ease back to 0
    //
    // Exactly one overshoot below zero and no oscillation: the shooter catches the gun once and levels it.
    public readonly struct RecoilShape
    {
        public readonly float Rise, Fall, Settle, Nod;

        public RecoilShape(float rise, float fall, float settle, float nod)
        {
            Rise = Mathf.Max(rise, 0.005f);
            Fall = Mathf.Max(fall, 0.005f);
            Settle = Mathf.Max(settle, 0.005f);
            Nod = Mathf.Max(nod, 0f);
        }

        public float Duration => Rise + Fall + Settle;

        // The main motion (barrel flip, arm lift): up, down past zero by Nod, back to zero
        public float WithNod(float t)
        {
            if (t <= 0f) return 0f;
            if (t < Rise) return RiseAt(t);
            t -= Rise;
            if (t < Fall) return Mathf.LerpUnclamped(1f, -Nod, Smooth(t / Fall));
            t -= Fall;
            if (t < Settle) return -Nod * (1f - Smooth(t / Settle));
            return 0f;
        }

        // Motions that must not swing to the other side (random side and roll, the push back):
        // the same rise, then a smooth return to zero without overshoot
        public float WithoutNod(float t)
        {
            if (t <= 0f) return 0f;
            if (t < Rise) return RiseAt(t);
            float back = Fall + 0.5f * Settle;
            t -= Rise;
            return t < back ? 1f - Smooth(t / back) : 0f;
        }

        // 0 -> 1 -> 0 during the settle phase only: one short move while the gun is levelled
        public float SettleBump(float t)
        {
            t -= Rise + Fall;
            if (t <= 0f || t >= Settle) return 0f;
            float s = Mathf.Sin(Mathf.PI * t / Settle);
            return s * s;
        }

        // Sharp ease-out up to 1: the kick, shared by both motions
        float RiseAt(float t) { float u = 1f - t / Rise; return 1f - u * u; }

        static float Smooth(float u) => Mathf.SmoothStep(0f, 1f, u);
    }
}
