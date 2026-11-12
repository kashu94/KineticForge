// Kinetic Forge: the diegetic UI plate behind world-space text. A rounded rectangle of smoked glass with a thin gilt rule
// and a soft outer glow, drawn procedurally from the quad's UVs so it stays crisp at any size and needs no texture.
// _Size is the plate's size in meters, so corners and rules keep their real-world width whatever the aspect ratio.
Shader "Kinetic Forge/Forge Panel"
{
    Properties
    {
        _Color("Fill", Color) = (0.05, 0.04, 0.035, 0.74)
        _FillTop("Fill Top Tint", Color) = (0.11, 0.085, 0.065, 0.78)
        _BorderColor("Rule", Color) = (1, 0.76, 0.42, 0.9)
        _GlowColor("Outer Glow", Color) = (1, 0.55, 0.2, 0.22)
        _Size("Size (m)", Vector) = (0.6, 0.3, 0, 0)
        _Radius("Corner Radius (m)", Range(0, 0.1)) = 0.018
        _Border("Rule Width (m)", Range(0, 0.01)) = 0.0018
        _Inset("Rule Inset (m)", Range(0, 0.03)) = 0.006
        _GlowWidth("Glow Width (m)", Range(0, 0.06)) = 0.014
        _Fade("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Panel"
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
                half4 _FillTop;
                half4 _BorderColor;
                half4 _GlowColor;
                float4 _Size;
                float _Radius;
                float _Border;
                float _Inset;
                float _GlowWidth;
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

            // Signed distance to a rounded box of half-extents h and corner radius r, in meters.
            float RoundedBox(float2 p, float2 h, float r)
            {
                float2 q = abs(p) - h + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // The quad spans the plate plus its glow margin on every side.
                float2 outer = _Size.xy * 0.5 + _GlowWidth;
                float2 p = (input.uv * 2.0 - 1.0) * outer;
                float2 extent = _Size.xy * 0.5;
                float d = RoundedBox(p, extent, _Radius);
                float aa = max(fwidth(d), 1e-5);

                float inside = saturate(0.5 - d / aa);
                half3 fill = lerp(_Color.rgb, _FillTop.rgb, saturate(input.uv.y));
                half fillAlpha = lerp(_Color.a, _FillTop.a, saturate(input.uv.y)) * inside;

                float ruleDistance = abs(RoundedBox(p, extent - _Inset, max(_Radius - _Inset, 0.0)));
                float rule = saturate(0.5 - (ruleDistance - _Border * 0.5) / aa) * inside;

                float glow = _GlowWidth > 0.0 ? exp(-max(d, 0.0) / (_GlowWidth * 0.35)) * (1.0 - inside) : 0.0;

                half3 color = fill;
                half alpha = fillAlpha;
                color = lerp(color, _BorderColor.rgb, rule * _BorderColor.a);
                alpha = max(alpha, rule * _BorderColor.a);
                half glowAlpha = glow * _GlowColor.a;
                color = lerp(color, _GlowColor.rgb, glowAlpha * (1.0 - alpha));
                alpha = alpha + glowAlpha * (1.0 - alpha);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha * _Fade);
            }
            ENDHLSL
        }
    }
}
