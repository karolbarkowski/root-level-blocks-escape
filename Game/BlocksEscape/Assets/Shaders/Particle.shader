// Particles for the small effects (ParticleFx): a shape drawn from the particle's UVs, coloured by the particle colour.
// Shape: 0 = soft dot (sparkles), 1 = leaf, 2 = thin ring (water ripples). Blend modes come from the material, so one
// shader serves additive light (One One) and ordinary transparency (SrcAlpha OneMinusSrcAlpha).
Shader "BlocksEscape/Particle"
{
    Properties
    {
        [Enum(Dot, 0, Leaf, 1, Ring, 2)] _Shape ("Shape", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
        _Softness ("Edge Softness", Range(0.01, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Particle"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                float _Softness;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float a;
                float3 tint = 1;
                if (_Shape < 0.5)
                {
                    float d = saturate(1.0 - length(p));
                    a = d * d;
                }
                else if (_Shape < 1.5)
                {
                    // A leaf pointing along y, a little darker along its midrib.
                    float halfWidth = 0.5 * (1.0 - p.y * p.y);
                    a = smoothstep(0.0, _Softness, halfWidth - abs(p.x));
                    tint = lerp(0.75, 1.0, smoothstep(0.0, 0.08, abs(p.x)));
                }
                else
                {
                    float d = length(p);
                    a = smoothstep(_Softness, 0.0, abs(d - 0.85)) * step(d, 1.0);
                }
                a *= input.color.a;
                // Additive (One One) has no alpha in the blend, so the shape scales the colour here; with SrcAlpha the
                // blend applies it.
                return half4(input.color.rgb * tint * (_SrcBlend == 1 ? a : 1.0), a);
            }
            ENDHLSL
        }
    }
}
