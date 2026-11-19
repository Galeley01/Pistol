using UnityEngine;

namespace Pistol.Core
{
    // The nearest hit along a ray that isn't part of the shooter (the player's own capsule, the held weapon).
    // Used by the bullets, the trajectory preview and the echo rays.
    public static class PhysicsQuery
    {
        static RaycastHit[] buffer = new RaycastHit[16];
        const int MaxBuffer = 256;

        public static bool NearestHit(Vector3 from, Vector3 dir, float distance, LayerMask mask, Transform ignoreRoot, out RaycastHit best)
        {
            best = default;
            if (distance <= 0f) return false;
            dir = dir.normalized;
            // The buffer gets hits in no particular order: when it is full the nearest one may be missing, so grow it and ask again
            int n = Physics.RaycastNonAlloc(from, dir, buffer, distance, mask, QueryTriggerInteraction.Ignore);
            while (n == buffer.Length && buffer.Length < MaxBuffer)
            {
                buffer = new RaycastHit[buffer.Length * 2];
                n = Physics.RaycastNonAlloc(from, dir, buffer, distance, mask, QueryTriggerInteraction.Ignore);
            }
            float bestDistance = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = buffer[i];
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (h.distance < bestDistance) { bestDistance = h.distance; best = h; }
            }
            return bestDistance < float.MaxValue;
        }
    }
}
