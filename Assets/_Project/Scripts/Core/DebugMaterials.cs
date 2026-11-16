using UnityEngine;
using UnityEngine.Rendering;

namespace Pistol.Core
{
    public static class DebugMaterials
    {
        // Untextured transparent material on the built-in Hidden/Internal-Colored shader (present in every build).
        // Color = vertex color x _Color. Used for debug lines, markers and simple overlays.
        public static Material CreateColored(bool zWrite, CompareFunction zTest = CompareFunction.LessEqual, int renderQueue = -1)
        {
            var mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetInt("_ZWrite", zWrite ? 1 : 0);
            mat.SetInt("_ZTest", (int)zTest);
            if (renderQueue >= 0) mat.renderQueue = renderQueue;
            return mat;
        }
    }
}
