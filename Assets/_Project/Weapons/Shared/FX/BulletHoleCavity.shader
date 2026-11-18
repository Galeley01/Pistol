// The inside of a 3D bullet crater: Standard lighting with colour and normal maps, drawn only where the crater's
// mouth (Pistol/BulletHoleMask) opened the wall, and drawn "through" the wall surface (depth test always).
// Only the faces turned to the camera are drawn: from outside a hollow crater shows only its inner side.
Shader "Pistol/BulletHoleCavity"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 3)) = 1
        _Glossiness ("Smoothness", Range(0, 1)) = 0.1
        _Darken ("Darker deeper (0 = off)", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Geometry+2" "RenderType"="Opaque" "ForceNoShadowCasting"="True" "IgnoreProjector"="True" }
        LOD 300
        Cull Back
        ZTest Always
        Stencil { Ref 64 ReadMask 64 Comp Equal }

        CGPROGRAM
        #pragma surface surf Standard vertex:vert
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap;
        fixed4 _Color;
        half _BumpScale, _Glossiness, _Darken;

        struct Input { float2 uv_MainTex; float depth; };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.depth = saturate(-v.vertex.z / 0.04);   // object space: Z is out of the surface, the crater goes to -Z
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb * lerp(1, 0.25, IN.depth * _Darken);   // the deep tunnel gets little light
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack Off
}
