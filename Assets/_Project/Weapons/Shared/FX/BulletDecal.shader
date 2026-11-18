// A bullet hole decal laid on a surface: Standard (metallic) lighting with a normal map, blended over the surface
// by its alpha. Rendered in the AlphaTest queue (before transparent things), so it still receives shadows,
// and pulled a little towards the camera so it never flickers with the surface under it.
Shader "Pistol/BulletDecal"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 3)) = 1
        _Glossiness ("Smoothness", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 300
        Cull Off
        ZWrite Off
        Offset -1, -1

        CGPROGRAM
        #pragma surface surf Standard decal:blend
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap;
        fixed4 _Color;
        half _BumpScale, _Glossiness;

        struct Input { float2 uv_MainTex; };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack Off
}
