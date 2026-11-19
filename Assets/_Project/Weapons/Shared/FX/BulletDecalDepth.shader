// A bullet hole decal with real depth, still a flat quad on the surface: the height map (baked from the concrete
// scan) shifts the picture by the view angle so the crater looks sunk into the wall (parallax occlusion), the
// crater's rim throws a shadow inside it from the main light, and its deep parts get less ambient light.
// Because it lies on the wall it gets the wall's shadow, fog and post-processing AO like everything else.
// Standard lighting, blended over the surface by its alpha, AlphaTest queue (receives shadows), like Pistol/BulletDecal.
Shader "Pistol/BulletDecalDepth"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 3)) = 1
        _HeightMap ("Height (1 = surface, 0 = deepest)", 2D) = "white" {}
        _Parallax ("Depth (fraction of the decal size)", Range(0, 0.6)) = 0.16
        _Steps ("Depth steps", Range(4, 32)) = 16
        _ShadowStrength ("Self-shadow strength", Range(0, 1)) = 0.85
        _ShadowSoftness ("Self-shadow softness", Range(1, 40)) = 12
        _CavityAO ("Darker deeper (ambient)", Range(0, 1)) = 0.7
        _Glossiness ("Smoothness", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 300
        Cull Off   // the decal quad (ImpactDecal.BuildQuad) is wound facing into the wall; its normals face out
        ZWrite Off
        Offset -1, -1

        CGPROGRAM
        #pragma surface surf Depth decal:blend vertex:vert
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex, _BumpMap, _HeightMap;
        fixed4 _Color;
        half _BumpScale, _Glossiness, _Parallax, _Steps, _ShadowStrength, _ShadowSoftness, _CavityAO;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewTS;    // towards the camera, tangent space (x = u, y = v, z = out of the surface)
            float3 lightTS;   // towards the main light, tangent space
        };

        struct SurfaceOutputDepth
        {
            fixed3 Albedo; float3 Normal; half3 Emission; half Metallic; half Smoothness; half Occlusion; fixed Alpha;
            half SelfShadow;   // 1 = lit, 0 = in the shadow of the crater's rim
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            TANGENT_SPACE_ROTATION;
            o.viewTS = mul(rotation, ObjSpaceViewDir(v.vertex));
            o.lightTS = mul(rotation, ObjSpaceLightDir(v.vertex));
        }

        inline SurfaceOutputStandard ToStandard(SurfaceOutputDepth s)
        {
            SurfaceOutputStandard o;
            o.Albedo = s.Albedo; o.Normal = s.Normal; o.Emission = s.Emission; o.Metallic = s.Metallic;
            o.Smoothness = s.Smoothness; o.Occlusion = s.Occlusion; o.Alpha = s.Alpha;
            return o;
        }

        // Standard lighting, the direct light dimmed by the crater's own shadow
        inline half4 LightingDepth(SurfaceOutputDepth s, float3 viewDir, UnityGI gi)
        {
            gi.light.color *= s.SelfShadow;
            return LightingStandard(ToStandard(s), viewDir, gi);
        }

        inline void LightingDepth_GI(SurfaceOutputDepth s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(ToStandard(s), data, gi);
        }

        void surf(Input IN, inout SurfaceOutputDepth o)
        {
            float2 uv = IN.uv_MainTex;
            float2 dx = ddx(uv), dy = ddy(uv);
            int steps = (int)_Steps;
            float layer = 1.0 / steps;

            // Parallax occlusion: walk into the surface along the view ray until it goes below the height field
            float3 v = normalize(IN.viewTS);
            float2 shift = v.xy / max(v.z, 0.2) * _Parallax * layer;
            float level = 1.0;
            float h = tex2Dgrad(_HeightMap, uv, dx, dy).r;
            float2 prevUv = uv; float prevLevel = level, prevH = h;
            [loop] for (int i = 0; i < 32; i++)
            {
                if (i >= steps || level <= h) break;
                prevUv = uv; prevLevel = level; prevH = h;
                uv -= shift;
                level -= layer;
                h = tex2Dgrad(_HeightMap, uv, dx, dy).r;
            }
            // Between the last two steps: where the ray actually crossed the surface
            float after = h - level, before = prevH - prevLevel;
            float t = after / min(after - before, -1e-4);
            uv = lerp(uv, prevUv, saturate(t));
            level = lerp(level, prevLevel, saturate(t));

            // The rim's shadow: walk from that point towards the light; any higher ground on the way shades it
            float shadow = 1.0;
            float3 l = normalize(IN.lightTS);
            if (l.z > 0.0 && _ShadowStrength > 0.0)
            {
                float2 lshift = l.xy / max(l.z, 0.2) * _Parallax * layer;
                float2 suv = uv; float sl = level; float blocked = 0.0;
                [loop] for (int j = 0; j < 32; j++)
                {
                    if (j >= steps || sl >= 1.0) break;
                    suv += lshift;
                    sl += layer;
                    float sh = tex2Dgrad(_HeightMap, suv, dx, dy).r;
                    blocked = max(blocked, (sh - sl) * _ShadowSoftness);
                }
                shadow = 1.0 - saturate(blocked) * _ShadowStrength;
            }

            fixed4 c = tex2D(_MainTex, uv) * _Color;
            float depthHere = 1.0 - level;   // 0 at the surface, 1 at the deepest
            o.Albedo = c.rgb;
            o.Alpha = tex2D(_MainTex, IN.uv_MainTex).a * _Color.a;   // the outline stays put, only the inside moves
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, uv), _BumpScale);
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
            o.Occlusion = lerp(1.0, 1.0 - depthHere, _CavityAO);
            o.SelfShadow = shadow;
        }
        ENDCG
    }
    FallBack Off
}
