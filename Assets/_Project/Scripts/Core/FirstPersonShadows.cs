using UnityEngine;
using UnityEngine.Rendering;

namespace Pistol.Core
{
    // What the player sees of themselves (the arms, the weapon in the hands, the body capsule) must not throw
    // shadows into the world: in first person it is placed for the camera, not for the sun, and its shadow
    // looks wrong (a floating box, a giant hand). It still receives shadows.
    public static class FirstPersonShadows
    {
        public static void TurnOff(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public static void TurnOff(Renderer renderer)
        {
            if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
