using UnityEngine;

namespace Pistol.Core
{
    public static class Noise
    {
        // Perlin noise in the -1..1 range on three axes; different seeds give independent channels
        public static Vector3 Perlin3(float t, float seed) => new Vector3(
            Mathf.PerlinNoise(t, seed + 0.1f) * 2f - 1f,
            Mathf.PerlinNoise(seed + 3.3f, t) * 2f - 1f,
            Mathf.PerlinNoise(t, seed + 7.7f) * 2f - 1f);
    }
}
