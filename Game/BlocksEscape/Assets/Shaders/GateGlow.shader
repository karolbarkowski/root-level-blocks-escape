// Glow over a gate cell while a block that fits it is held (GateCell.SetGlow): a bright square line on the floor and
// soft light rising from it, streaked and drifting so it shimmers. Additive and unlit; _Glow (0..1, per renderer)
// fades the whole effect in and out.
//
// The mesh (GateCell.GlowMesh) carries in TEXCOORD0: x = position along the square's edge or across the floor,
// y = height up the wall (0 at the floor) or across the floor, z = 0 for the floor quad and 1 for the walls.
Shader "BlocksEscape/GateGlow"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.33, 0.51, 0.84, 1)
        _Glow ("Glow", Range(0, 1)) = 1
        _LineWidth ("Floor Line Width", Range(0.005, 0.2)) = 0.05
        _Streaks ("Streaks Across An Edge", Float) = 5
        _Speed ("Shimmer Speed", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GateGlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Glow;
                float _LineWidth;
                float _Streaks;
                float _Speed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _Speed;
                float glow;
                if (input.uv.z < 0.5)
                {
                    // Floor: a line along the square's edge with a soft halo inwards, pulsing gently.
                    float2 p = input.uv.xy - 0.5;
                    float d = 0.5 - max(abs(p.x), abs(p.y)); // 0 at the edge
                    float edgeLine = exp(-max(d - _LineWidth * 0.5, 0) / (_LineWidth * 0.6));
                    float halo = exp(-d / 0.18) * 0.35;
                    glow = (edgeLine + halo) * (0.85 + 0.15 * sin(t * 2.0));
                }
                else
                {
                    // Walls: light fading upwards in uneven vertical streaks that drift along the edge and flicker.
                    float v = input.uv.y;
                    float x = input.uv.x * _Streaks + t * 0.35;
                    float i = floor(x);
                    float f = frac(x);
                    float streak = lerp(0.35, 1.0, Hash(i)) * smoothstep(0.0, 0.5, f) * smoothstep(1.0, 0.5, f);
                    float flicker = 0.75 + 0.25 * sin(t * (2.0 + Hash(i + 7.0) * 3.0) + Hash(i + 3.0) * 6.283);
                    float rise = 0.85 + 0.15 * sin(v * 18.0 - t * 4.0); // faint bands running up
                    float fade = pow(saturate(1.0 - v), 2.2);
                    glow = (0.25 + streak * flicker) * rise * fade;
                }
                return half4(_Color.rgb * glow * _Glow, 1);
            }
            ENDHLSL
        }
    }
}
