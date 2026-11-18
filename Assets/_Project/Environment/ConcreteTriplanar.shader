// Concrete laid on by world position (triplanar): the texture is projected from the three axes and blended by the
// surface direction, so stretched boxes (walls, platforms) get the same tile size everywhere, with no stretching.
// Standard (metallic) lighting: colour, normal (OpenGL, as Unity expects), ambient occlusion and a metallic map
// (R = metallic, A = smoothness when the map has it); works with lightmaps.
Shader "Pistol/ConcreteTriplanar"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 3)) = 1
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "black" {}
        _Metallic ("Metallic", Range(0, 1)) = 1
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        [Toggle] _SmoothnessFromMap ("Smoothness from the map alpha", Float) = 0
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _TileSize ("Tile Size (m)", Float) = 2
        _BlendSharpness ("Blend Sharpness", Range(1, 16)) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _OcclusionMap;
        fixed4 _Color;
        half _BumpScale, _Metallic, _Glossiness, _SmoothnessFromMap, _OcclusionStrength, _TileSize, _BlendSharpness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        // World-space normal -> the tangent space the surface output expects
        float3 WorldToTangent(Input IN, float3 n)
        {
            float3 t0 = WorldNormalVector(IN, float3(1, 0, 0));
            float3 t1 = WorldNormalVector(IN, float3(0, 1, 0));
            float3 t2 = WorldNormalVector(IN, float3(0, 0, 1));
            return normalize(mul(float3x3(t0, t1, t2), n));
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 wn = WorldNormalVector(IN, float3(0, 0, 1));
            float3 w = pow(abs(wn), _BlendSharpness); w /= (w.x + w.y + w.z);
            float3 p = IN.worldPos / max(_TileSize, 0.01);
            float2 uvX = p.zy, uvY = p.xz, uvZ = p.xy;

            fixed4 c = tex2D(_MainTex, uvX) * w.x + tex2D(_MainTex, uvY) * w.y + tex2D(_MainTex, uvZ) * w.z;
            fixed4 mg = tex2D(_MetallicGlossMap, uvX) * w.x + tex2D(_MetallicGlossMap, uvY) * w.y + tex2D(_MetallicGlossMap, uvZ) * w.z;
            fixed ao = tex2D(_OcclusionMap, uvX).g * w.x + tex2D(_OcclusionMap, uvY).g * w.y + tex2D(_OcclusionMap, uvZ).g * w.z;

            // Normal maps per projection, swizzled into world space (UDN blend) and back to tangent space
            float3 nX = UnpackScaleNormal(tex2D(_BumpMap, uvX), _BumpScale);
            float3 nY = UnpackScaleNormal(tex2D(_BumpMap, uvY), _BumpScale);
            float3 nZ = UnpackScaleNormal(tex2D(_BumpMap, uvZ), _BumpScale);
            float3 worldN = normalize(
                float3(0, nX.y, nX.x) * w.x + float3(nY.x, 0, nY.y) * w.y + float3(nZ.x, nZ.y, 0) * w.z + wn);

            o.Albedo = c.rgb * _Color.rgb;
            o.Metallic = mg.r * _Metallic;
            o.Smoothness = _SmoothnessFromMap > 0.5 ? mg.a * _Glossiness : _Glossiness;
            o.Occlusion = lerp(1, ao, _OcclusionStrength);
            o.Normal = WorldToTangent(IN, worldN);
        }
        ENDCG
    }
    FallBack "Standard"
}
