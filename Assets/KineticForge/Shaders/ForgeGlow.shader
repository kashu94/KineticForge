// Kinetic Forge: additive glow for sparks, embers, halos and the resonance ring. Draws a soft disc, or a ring when
// _Ring is above 0, procedurally from the quad's UVs, so it needs no texture. Particle vertex colors tint it.
Shader "Kinetic Forge/Forge Glow"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 0.6, 0.2, 1)
        _Ring("Ring Thickness", Range(0, 0.5)) = 0
        _Softness("Softness", Range(0.5, 6)) = 1.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
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
                half _Ring;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
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
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float d = length(input.uv * 2.0 - 1.0);
                half alpha;
                if (_Ring > 0.001)
                {
                    float x = (d - (1.0 - _Ring)) / _Ring;
                    alpha = exp(-x * x * 2.5) * step(d, 1.0);
                }
                else
                {
                    alpha = pow(saturate(1.0 - d), _Softness);
                }

                half3 color = _Color.rgb * input.color.rgb * (alpha * input.color.a * _Color.a);
                // Additive light fades into fog by dimming toward black.
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
