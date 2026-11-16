using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;

namespace Pistol.PostFx
{
    // Screen effects of one situation (aiming, cylinder inspect...): eye focus, darker edges, blurred edges.
    // Whoever needs them sends a request to CameraEffects with this profile and a strength (0..1).
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Profiles/Screen Effects", fileName = "ScreenEffectsProfile")]
    public class ScreenEffectsProfile : ScriptableObject
    {
        [BoxGroup("Общее"), Tooltip("Если одновременно включены несколько наборов эффектов, этот приоритет решает, чей сверху. Больше — главнее")]
        public int priority = 100;

        [BoxGroup("Фокус глаз (Depth of Field)"), Tooltip("Размывать то, что не в фокусе. Где фокус, решает тот, кто включает эффект")]
        public bool focus = true;
        [BoxGroup("Фокус глаз (Depth of Field)"), ShowIf("focus"), Range(1f, 32f), Tooltip("Диафрагма (f-число). Меньше — сильнее размыто всё, что не в фокусе")]
        public float aperture = 17f;
        [BoxGroup("Фокус глаз (Depth of Field)"), ShowIf("focus"), Range(10f, 100f), Tooltip("Фокусное расстояние «объектива», мм. Больше — сильнее размытие и уже зона резкости")]
        public float focalLength = 31f;
        [BoxGroup("Фокус глаз (Depth of Field)"), ShowIf("focus"), Tooltip("Качество размытия. Medium — обычно достаточно, Large/VeryLarge дороже для видеокарты")]
        public KernelSize kernelSize = KernelSize.Medium;
        [BoxGroup("Фокус глаз (Depth of Field)"), ShowIf("focus")]
        [Tooltip("Плавно включать фокус диафрагмой, а не силой эффекта. Иначе в начале перехода фокус смешивается со стандартными 10 м и на мгновение размывает само оружие")]
        public bool fadeByAperture = false;
        [BoxGroup("Фокус глаз (Depth of Field)"), ShowIf(EConditionOperator.And, "focus", "fadeByAperture"), Range(1f, 32f)]
        [Tooltip("Диафрагма в начале перехода: чем больше, тем меньше размытие на старте")]
        public float closedAperture = 32f;

        [BoxGroup("Затемнение краёв (Vignette)"), Tooltip("Затемнять края экрана")]
        public bool vignette = false;
        [BoxGroup("Затемнение краёв (Vignette)"), ShowIf("vignette"), Range(0f, 1f), Tooltip("Насколько темнеют края экрана")]
        public float vignetteIntensity = 0.42f;
        [BoxGroup("Затемнение краёв (Vignette)"), ShowIf("vignette"), Range(0.01f, 1f), Tooltip("Плавность перехода от центра к тёмным краям")]
        public float vignetteSmoothness = 0.45f;
        [BoxGroup("Затемнение краёв (Vignette)"), ShowIf("vignette"), Range(0f, 1f), Tooltip("1 — круглое затемнение, меньше — по форме экрана")]
        public float vignetteRoundness = 1f;

        [BoxGroup("Размытие по краям экрана"), Tooltip("Края экрана размываются — взгляд сосредоточен в центре")]
        public bool edgeBlur = false;
        [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0f, 1f), Tooltip("Сила размытия краёв")]
        public float edgeBlurIntensity = 0.7f;
        [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0f, 0.8f), Tooltip("Радиус чёткой зоны в центре (доля высоты экрана)")]
        public float edgeBlurRadius = 0.3f;
        [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0.05f, 1f), Tooltip("Плавность перехода от чёткого к размытому")]
        public float edgeBlurSoftness = 0.45f;
        [BoxGroup("Размытие по краям экрана"), ShowIf("edgeBlur"), Range(0.005f, 0.08f), Tooltip("Ширина размытия")]
        public float edgeBlurSpread = 0.025f;
    }
}
