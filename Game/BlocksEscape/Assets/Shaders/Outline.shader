// Silhouette outline, used as a second material on block and gate renderers.
// Draws a slightly inflated shell of the mesh with no depth test, in a queue after the meadow and pen floor (Ground.mat,
// PenFloor.mat: Geometry-10) but before the pieces themselves (Geometry). The pieces then cover the shell's inside, so
// what remains is an even line around each silhouette that nothing on the ground can hide, and that fills the gaps
// between touching pieces. The camera is a narrow-FOV top-down view, so a world-space thickness reads as an even width.
//
// Look (colour, tint from the piece, darkness, width, opacity) comes from global values set by the OutlineSettings
// component, so one place drives every piece. The per-piece tint is the piece's average albedo (the texture's smallest
// mip), darkened, so it is one flat colour per animal. A stencil bit lets each pixel take the outline once, so a
// translucent outline doesn't darken where the shell overlaps itself or a neighbour's shell.
//
// Pushes along TEXCOORD3, the smoothed normals OutlineNormals bakes on import, so hard-edged meshes (gates) don't
// split open at their corners; falls back to the vertex normal when the channel is empty.
Shader "BlocksEscape/Outline"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-5" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
            CBUFFER_END

            // Set by OutlineSettings.
            float4 _OutlineColor;   // rgb: fixed colour
            float _OutlineTint;     // 0 = fixed colour, 1 = the piece's own colour, darkened
            float _OutlineDarken;   // brightness of the piece's colour in the tint
            float _OutlineWidth;    // in cells
            float _OutlineOpacity;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float3 smoothNormalOS : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalOS = dot(input.smoothNormalOS, input.smoothNormalOS) > 0.01 ? input.smoothNormalOS : input.normalOS;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Grow a shell of constant thickness around the piece. Downward components are dropped: pieces stand on the
                // floor, so a shell pushed below it would be hidden there and the rim would come only from higher edges,
                // floating off the base. Faces pointing straight down don't move at all.
                float3 push = TransformObjectToWorldDir(normalOS);
                push.y = max(push.y, 0.0);
                float len = length(push);
                if (len > 1e-4)
                    positionWS += push / len * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv, 12).rgb;
                half luma = dot(albedo, half3(0.299, 0.587, 0.114));
                half3 tint = saturate(lerp(luma.xxx, albedo, 1.3) * _OutlineDarken);
                half3 color = lerp(_OutlineColor.rgb, tint, _OutlineTint);
                return half4(color, _OutlineOpacity);
            }
            ENDHLSL
        }
    }
}
