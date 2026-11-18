// Cardboard target: Standard (metallic) lighting with real holes. The hole mask (R: 1 = card, 0 = hole) is drawn
// by PaperTargetCard; it maps onto the card by the object-space X/Y of the vertices (_CardRect: size XY, centre ZW),
// so it doesn't need UVs. Holes are cut in the shadow too. No batching: batched vertices lose object space.
Shader "Pistol/PaperTargetCutout"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "black" {}
        _GlossMapScale ("Smoothness", Range(0,1)) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        [NoScaleOffset] _HoleMask ("Hole Mask (set by code)", 2D) = "white" {}
        _CardRect ("Card size XY, centre ZW (set by code)", Vector) = (0.46, 0.76, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "DisableBatching"="True" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _OcclusionMap, _HoleMask;
        fixed4 _Color;
        half _BumpScale, _GlossMapScale, _OcclusionStrength;
        float4 _CardRect;

        struct Input
        {
            float2 uv_MainTex;
            float2 maskUV;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.maskUV = (v.vertex.xy - _CardRect.zw) / _CardRect.xy + 0.5;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            clip(tex2D(_HoleMask, IN.maskUV).r - 0.5);
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Albedo = c.rgb;
            o.Metallic = mg.r;
            o.Smoothness = mg.a * _GlossMapScale;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Occlusion = lerp(1, tex2D(_OcclusionMap, IN.uv_MainTex).g, _OcclusionStrength);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
