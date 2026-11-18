// Concrete laid on by world position (triplanar): the texture is projected from the three axes and blended by the
// surface direction, so stretched boxes (walls, platforms) get the same tile size everywhere, with no stretching.
// Standard (specular) lighting with the colour, normal and specular maps of a concrete set; works with lightmaps.
Shader "Pistol/ConcreteTriplanar"
{
    Properties
    {
        _Color ("Tint", Color) = (0.6,0.6,0.6,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 3)) = 1
        _SpecGlossMap ("Specular", 2D) = "white" {}
        _SpecColor ("Specular Colour", Color) = (0.2,0.2,0.2,1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
        _TileSize ("Tile Size (m)", Float) = 2.5
        _BlendSharpness ("Blend Sharpness", Range(1, 16)) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _SpecGlossMap;
        fixed4 _Color;
        half _BumpScale, _Glossiness, _TileSize, _BlendSharpness;

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

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float3 wn = WorldNormalVector(IN, float3(0, 0, 1));
            float3 w = pow(abs(wn), _BlendSharpness); w /= (w.x + w.y + w.z);
            float3 p = IN.worldPos / max(_TileSize, 0.01);
            float2 uvX = p.zy, uvY = p.xz, uvZ = p.xy;

            fixed4 c = tex2D(_MainTex, uvX) * w.x + tex2D(_MainTex, uvY) * w.y + tex2D(_MainTex, uvZ) * w.z;
            fixed4 s = tex2D(_SpecGlossMap, uvX) * w.x + tex2D(_SpecGlossMap, uvY) * w.y + tex2D(_SpecGlossMap, uvZ) * w.z;

            // Normal maps per projection, swizzled into world space (UDN blend) and back to tangent space
            float3 nX = UnpackScaleNormal(tex2D(_BumpMap, uvX), _BumpScale);
            float3 nY = UnpackScaleNormal(tex2D(_BumpMap, uvY), _BumpScale);
            float3 nZ = UnpackScaleNormal(tex2D(_BumpMap, uvZ), _BumpScale);
            float3 worldN = normalize(
                float3(0, nX.y, nX.x) * w.x + float3(nY.x, 0, nY.y) * w.y + float3(nZ.x, nZ.y, 0) * w.z + wn);

            o.Albedo = c.rgb * _Color.rgb;
            o.Specular = s.rgb * _SpecColor.rgb;
            o.Smoothness = _Glossiness * s.a;
            o.Normal = WorldToTangent(IN, worldN);
        }
        ENDCG
    }
    FallBack "Standard (Specular setup)"
}
