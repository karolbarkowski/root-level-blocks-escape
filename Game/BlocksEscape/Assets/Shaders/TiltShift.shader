// Screen-space tilt shift for URP's Full Screen Pass Renderer Feature.
// Keeps a horizontal band sharp and blurs above and below it, more the further from the band, which makes the scene
// read as a miniature. The band comes from BoardCamera as _TiltShiftBand (viewport y min, max of the board), so the
// playing area itself is never blurred; with no band set, the screen center is sharp.
Shader "BlocksEscape/TiltShift"
{
    Properties
    {
        _BandPadding ("Extra Sharp Space (screen heights)", Range(0, 0.3)) = 0.02
        _Falloff ("Blur Ramp (screen heights)", Range(0.01, 0.6)) = 0.18
        _BlurRadius ("Max Blur Radius (screen heights)", Range(0, 0.03)) = 0.009
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "TiltShift"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _TiltShiftBand; // x = sharp band bottom, y = top, in viewport heights
            float _BandPadding;
            float _Falloff;
            float _BlurRadius;

            static const int Taps = 24;
            static const float GoldenAngle = 2.39996323;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float2 band = _TiltShiftBand.y > _TiltShiftBand.x ? _TiltShiftBand.xy : float2(0.4, 0.6);
                float outside = max(max(band.x - _BandPadding - uv.y, uv.y - band.y - _BandPadding), 0.0);
                float amount = saturate(outside / _Falloff);
                float radius = _BlurRadius * amount * amount;
                if (radius < 1e-5)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Disc of samples on a golden-angle spiral, evenly spread so few taps blur smoothly.
                float2 scale = float2(radius * _ScreenParams.y / _ScreenParams.x, radius);
                half4 sum = 0;
                [unroll]
                for (int i = 0; i < Taps; i++)
                {
                    float r = sqrt((i + 0.5) / Taps);
                    float a = i * GoldenAngle;
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(cos(a), sin(a)) * r * scale);
                }
                return sum / Taps;
            }
            ENDHLSL
        }
    }
}
