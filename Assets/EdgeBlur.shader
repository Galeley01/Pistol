// Размытие по краям экрана. Используется скриптом EdgeBlur.cs (пакет Post Processing).
Shader "Hidden/Custom/EdgeBlur"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float _Intensity;
        float _Radius;
        float _Softness;
        float _Spread;
        float _Aspect;

        float4 Frag(VaryingsDefault i) : SV_Target
        {
            float2 uv = i.texcoord;
            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

            // Расстояние от центра с учётом пропорций экрана (круглая чёткая зона)
            float2 d = uv - 0.5;
            d.x *= _Aspect;
            float mask = saturate((length(d) - _Radius) / max(_Softness, 0.0001)) * _Intensity;
            if (mask <= 0.001) return col;

            // Размытие диском: два кольца по 12 выборок, радиус растёт к краям
            float r = _Spread * mask;
            float4 acc = col;
            [unroll]
            for (int k = 0; k < 12; k++)
            {
                float a = k * 0.5235988;   // 2π / 12
                float2 o = float2(cos(a) / _Aspect, sin(a)) * r;
                acc += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + o);
                acc += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + o * 0.5 + float2(o.y, -o.x) * 0.25);
            }
            acc /= 25.0;
            return lerp(col, acc, saturate(mask * 1.5));
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment Frag
            ENDHLSL
        }
    }
}
