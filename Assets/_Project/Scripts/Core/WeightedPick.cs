using System.Collections.Generic;

namespace Pistol.Core
{
    // Random choice by weights that never repeats the same option more than maxRun times in a row
    // (when another option is available), so randomness doesn't look broken.
    public static class WeightedPick
    {
        // weights: option weights (<= 0 means the option is off); last: the previous pick (-1 = none);
        // runLength: how many times in a row `last` was picked; maxRun: the repeat limit (0 = no limit);
        // random01: a random number in [0, 1). Returns the picked index, or -1 when every weight is off.
        public static int Pick(IReadOnlyList<float> weights, int last, int runLength, int maxRun, float random01)
        {
            bool excludeLast = maxRun > 0 && last >= 0 && runLength >= maxRun && HasOtherThan(weights, last);

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
                if (Allowed(weights, i, last, excludeLast)) total += weights[i];
            if (total <= 0f) return -1;

            float r = random01 * total;
            int lastAllowed = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!Allowed(weights, i, last, excludeLast)) continue;
                lastAllowed = i;
                if (r < weights[i]) return i;
                r -= weights[i];
            }
            return lastAllowed;   // random01 at the very top of the range
        }

        // The run length after picking `picked`
        public static int NextRun(int picked, int last, int runLength) => picked == last ? runLength + 1 : 1;

        static bool Allowed(IReadOnlyList<float> w, int i, int last, bool excludeLast) =>
            w[i] > 0f && !(excludeLast && i == last);

        static bool HasOtherThan(IReadOnlyList<float> w, int index)
        {
            for (int i = 0; i < w.Count; i++)
                if (i != index && w[i] > 0f) return true;
            return false;
        }
    }
}
