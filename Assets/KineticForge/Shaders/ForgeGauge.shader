// Kinetic Forge: one voice's tuning gauge. A horizontal scale in cents with the target at the centre: a thin track, the
// in-tune band, a target tick, the voice's needle, and a ghost marker showing where the next cut would take it. All
// drawn procedurally from the quad's UVs, so it stays crisp at any size and needs no texture. _Size is the quad's size
// in meters, so the needle and ticks keep their real-world width.
Shader "Kinetic Forge/Forge Gauge"
{
    Properties
    {
        _Color("Needle", Color) = (1, 0.72, 0.3, 1)
        _Size("Size (m)", Vector) = (0.24, 0.05, 0, 0)
        _Range("Range (cents)", Float) = 150
        _Tolerance("In-tune band (cents)", Float) = 14
        _Error("Voice error (cents)", Float) = 60
        _Delta("Preview change (cents)", Float) = 0
        _PreviewAlpha("Preview strength", Range(0, 1)) = 0
        _Active("Ringing", Range(0, 1)) = 1
        _Fade("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Gauge"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Size;
                float _Range;
                float _Tolerance;
                float _Error;
                float _Delta;
                half _PreviewAlpha;
                half _Active;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Horizontal position in meters from the centre for a pitch error in cents.
            float PositionOf(float cents)
            {
                return clamp(cents / _Range, -1.0, 1.0) * _Size.x * 0.47;
            }

            // Coverage of a thin segment from a to b with half-width w, antialiased by aa.
            float Segment(float2 p, float2 a, float2 b, float w, float aa)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
                float d = length(pa - ba * h) - w;
                return saturate(0.5 - d / aa);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 p = (input.uv - 0.5) * _Size.xy;
                float aa = max(fwidth(p.x), 1e-5);

                half3 color = half3(0, 0, 0);
                half alpha = 0;

                // Track, with a tick every 50 cents.
                float track = saturate(0.5 - (abs(p.y) - 0.0016) / aa) * step(abs(p.x), _Size.x * 0.49);
                color = lerp(color, half3(0.62, 0.55, 0.48), track);
                alpha = max(alpha, track * 0.7);
                float tickSpacing = _Size.x * 0.47 * 50.0 / _Range;
                float tickPhase = abs(frac(p.x / tickSpacing + 0.5) - 0.5) * tickSpacing;
                float ticks = saturate(0.5 - (tickPhase - 0.0007) / aa) * step(abs(p.y), 0.0065) * step(abs(p.x), _Size.x * 0.47);
                color = lerp(color, half3(0.62, 0.55, 0.48), ticks);
                alpha = max(alpha, ticks * 0.55);

                // In-tune band around the target.
                float band = PositionOf(_Tolerance);
                float inBand = saturate(0.5 - (abs(p.x) - band) / aa) * saturate(0.5 - (abs(p.y) - 0.0105) / aa);
                color = lerp(color, half3(0.45, 1.0, 0.6), inBand * 0.5);
                alpha = max(alpha, inBand * 0.32);

                // Target tick.
                float target = saturate(0.5 - (abs(p.x) - 0.0011) / aa) * step(abs(p.y), 0.0195);
                color = lerp(color, half3(1.0, 0.97, 0.9), target);
                alpha = max(alpha, target * 0.95);

                // Where the next cut would take the voice: a ring at the new position, and a thin line to it.
                float here = PositionOf(_Error);
                float next = PositionOf(_Error + _Delta);
                half3 previewColor = (_Error + _Delta) < -_Tolerance ? half3(1.0, 0.4, 0.34) : half3(0.92, 0.96, 1.0);
                float link = Segment(p, float2(here, 0), float2(next, 0), 0.0009, aa) * _PreviewAlpha;
                float ringDistance = abs(length(p - float2(next, 0)) - 0.0068);
                float ring = saturate(0.5 - (ringDistance - 0.0009) / aa) * _PreviewAlpha;
                float preview = max(link * 0.55, ring);
                color = lerp(color, previewColor, preview);
                alpha = max(alpha, preview * 0.9);

                // The needle: a capsule with a soft glow, in the voice's state color.
                float needleDistance = Segment(p, float2(here, -0.0185), float2(here, 0.0185), 0.0026, aa);
                float glow = exp(-length(float2(p.x - here, p.y * 0.55)) / 0.0085) * 0.55;
                half3 needleColor = _Color.rgb;
                color = lerp(color, needleColor, saturate(glow));
                alpha = max(alpha, glow * 0.7);
                color = lerp(color, lerp(needleColor, half3(1, 1, 1), 0.55), needleDistance);
                alpha = max(alpha, needleDistance);

                alpha *= lerp(0.38, 1.0, _Active) * _Fade;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
