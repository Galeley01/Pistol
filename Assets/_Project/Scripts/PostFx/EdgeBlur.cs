using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Pistol.PostFx
{
    // Screen edge blur (aim "tunnel" effect) for the Post Processing package.
    // Works together with EdgeBlur.shader (Hidden/Custom/EdgeBlur).
    // Driven by CameraEffects; no need to add it to a profile manually.
    [Serializable]
    [PostProcess(typeof(EdgeBlurRenderer), PostProcessEvent.AfterStack, "Custom/Edge Blur")]
    public sealed class EdgeBlur : PostProcessEffectSettings
    {
        [Range(0f, 1f), Tooltip("Сила размытия по краям")]
        public FloatParameter intensity = new FloatParameter { value = 0f };
        [Range(0f, 1f), Tooltip("Радиус чёткой зоны в центре (доля высоты экрана)")]
        public FloatParameter radius = new FloatParameter { value = 0.3f };
        [Range(0.01f, 1f), Tooltip("Насколько плавно чёткая зона переходит в размытую")]
        public FloatParameter softness = new FloatParameter { value = 0.45f };
        [Range(0f, 0.1f), Tooltip("Ширина размытия")]
        public FloatParameter spread = new FloatParameter { value = 0.025f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
            => enabled.value && intensity.value > 0.001f;
    }

    public sealed class EdgeBlurRenderer : PostProcessEffectRenderer<EdgeBlur>
    {
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        static readonly int SpreadId = Shader.PropertyToID("_Spread");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        Shader shader;

        public override void Render(PostProcessRenderContext context)
        {
            if (shader == null) shader = Shader.Find("Hidden/Custom/EdgeBlur");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }
            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat(IntensityId, settings.intensity);
            sheet.properties.SetFloat(RadiusId, settings.radius);
            sheet.properties.SetFloat(SoftnessId, settings.softness);
            sheet.properties.SetFloat(SpreadId, settings.spread);
            sheet.properties.SetFloat(AspectId, context.width / (float)Mathf.Max(context.height, 1));
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
