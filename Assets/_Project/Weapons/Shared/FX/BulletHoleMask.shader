// The opening of a 3D bullet crater: draws nothing, only marks in the stencil buffer where the crater's mouth is
// visible on the wall. The crater (Pistol/BulletHoleCavity) is then drawn only there, "inside" the wall.
Shader "Pistol/BulletHoleMask"
{
    SubShader
    {
        Tags { "Queue"="Geometry+1" "RenderType"="Opaque" "ForceNoShadowCasting"="True" "IgnoreProjector"="True" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            Offset -1, -1
            Stencil { Ref 64 Comp Always Pass Replace }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
