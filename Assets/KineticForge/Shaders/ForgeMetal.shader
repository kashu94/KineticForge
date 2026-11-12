// Kinetic Forge: hot, resonating, carvable metal for URP.
// Lit PBR with a procedural hammered surface, black-body heat glow, glowing carved runes and cracks from the carve map
// (R groove depth, G rune glow, B crack), and Chladni figures: the nodal lines of the relic's ringing plate modes, which
// shimmer where sand would gather on a bowed plate. Mode shapes match ResonanceMath.PlateMode. The vertex color's red
// channel marks the carvable face; only that face shows runes and Chladni lines.
Shader "Kinetic Forge/Forge Metal"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.78, 0.5, 0.24, 1)
        _Metallic("Metallic", Range(0, 1)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.6
        _HammerScale("Hammer Scale", Float) = 40
        _HammerStrength("Hammer Strength", Range(0, 1)) = 0.35
        [NoScaleOffset] _CarveMap("Carve Map", 2D) = "black" {}
        _Heat("Heat", Range(0, 1)) = 0
        _HeatGlow("Heat Glow", Float) = 0.45
        [HDR] _RuneColor("Rune Color", Color) = (1, 0.55, 0.15, 1)
        _RuneGlow("Rune Glow", Float) = 1.6
        [HDR] _ChladniColor("Chladni Color", Color) = (0.35, 0.8, 1, 1)
        _ChladniWidth("Chladni Line Width", Range(0.01, 0.4)) = 0.12
        _ModeAmp("Mode Amplitudes", Vector) = (0, 0, 0, 0)
        _ModeM("Mode M", Vector) = (1, 1, 2, 1)
        _ModeN("Mode N", Vector) = (2, 3, 3, 4)
        _ModeSign("Mode Sign", Vector) = (-1, 1, -1, 1)
        _Pulse("Pulse", Float) = 0
        [HDR] _CrackColor("Crack Glow", Color) = (1, 0.35, 0.05, 1)
        _Emissive("Self Glow", Float) = 0
        _TargetAmp("Target Figure", Vector) = (0, 0, 0, 0)
        [HDR] _TargetColor("Target Figure Color", Color) = (0.25, 0.85, 1.3, 1)
        _TargetGlow("Target Figure Glow", Range(0, 2)) = 0
        _Fill("Pour Fill", Range(0, 1)) = 1
        _FillHeight("Pour Height", Float) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_CarveMap);
        SAMPLER(sampler_CarveMap);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Metallic;
            half _Smoothness;
            float _HammerScale;
            half _HammerStrength;
            half _Heat;
            half _HeatGlow;
            half4 _RuneColor;
            half _RuneGlow;
            half4 _ChladniColor;
            half _ChladniWidth;
            float4 _ModeAmp;
            float4 _ModeM;
            float4 _ModeN;
            float4 _ModeSign;
            half _Pulse;
            half4 _CrackColor;
            half _Emissive;
            float4 _TargetAmp;
            half4 _TargetColor;
            half _TargetGlow;
            half _Fill;
            float _FillHeight;
        CBUFFER_END
        ENDHLSL


        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half faceMask : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                float x00 = lerp(n000, n100, u.x);
                float x10 = lerp(n010, n110, u.x);
                float x01 = lerp(n001, n101, u.x);
                float x11 = lerp(n011, n111, u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            float Hammer(float3 p)
            {
                return ValueNoise(p) * 0.65 + ValueNoise(p * 2.7 + 17.0) * 0.35;
            }

            float PlateMode(float m, float n, float s, float2 uv)
            {
                return cos(m * PI * uv.x) * cos(n * PI * uv.y) + s * cos(n * PI * uv.x) * cos(m * PI * uv.y);
            }

            // Matches ResonanceMath.Incandescence: dull red, cherry, orange, yellow-orange.
            half3 Incandescence(half heat)
            {
                half t = saturate(heat);
                half3 c = saturate(half3(t * 1.7, t * 1.7 - 0.6, t * 1.7 - 1.25)) * half3(1.0, 0.8, 0.5);
                return c * c * smoothstep(0.2, 0.55, t);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                output.faceMask = input.color.r;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // A relic being poured exists only below the molten surface rising through its mold.
                float pourHeight = input.positionOS.y / max(_FillHeight, 1e-4) + 0.5;
                clip(_Fill * 1.02 - pourHeight);
                float pourFront = (1.0 - step(0.999, _Fill)) * exp(-pow((pourHeight - _Fill * 1.02) / 0.035, 2.0));

                float3 normalWS = normalize(input.normalWS);

                // Hammered surface: bend the normal along the gradient of a two-octave noise in object space.
                float3 p = input.positionOS * _HammerScale;
                float h = Hammer(p);
                const float e = 0.35;
                float3 gradient = float3(Hammer(p + float3(e, 0, 0)), Hammer(p + float3(0, e, 0)), Hammer(p + float3(0, 0, e))) - h;
                float3 gradientWS = TransformObjectToWorldDir(gradient / e, false);
                gradientWS -= normalWS * dot(gradientWS, normalWS);
                normalWS = normalize(normalWS - gradientWS * (_HammerStrength * 0.15));

                half4 carve = SAMPLE_TEXTURE2D(_CarveMap, sampler_CarveMap, input.uv) * input.faceMask;
                half groove = carve.r;
                half rune = carve.g;
                half crack = carve.b;
                half heat = saturate(_Heat);

                half3 albedo = _BaseColor.rgb * lerp(1.0, 0.8, h);
                albedo *= lerp(1.0, 0.35, smoothstep(0.3, 0.9, heat));
                albedo = lerp(albedo, albedo * 0.45, groove);
                albedo = lerp(albedo, half3(0.02, 0.015, 0.01), crack);

                half smoothness = _Smoothness * lerp(0.75, 1.0, h) * (1.0 - 0.4 * groove) * (1.0 - crack) * lerp(1.0, 0.55, heat);

                half3 emission = Incandescence(heat) * _HeatGlow * (1.0 + groove * 1.5);
                emission += _RuneColor.rgb * rune * _RuneGlow * (0.35 + 0.65 * heat) * (0.85 + 0.15 * _Pulse);

                // Chladni figures: glowing nodal lines of the summed plate modes.
                float2 uv = input.uv;
                float4 amplitude = _ModeAmp;
                float field = amplitude.x * PlateMode(_ModeM.x, _ModeN.x, _ModeSign.x, uv) +
                              amplitude.y * PlateMode(_ModeM.y, _ModeN.y, _ModeSign.y, uv) +
                              amplitude.z * PlateMode(_ModeM.z, _ModeN.z, _ModeSign.z, uv) +
                              amplitude.w * PlateMode(_ModeM.w, _ModeN.w, _ModeSign.w, uv);
                float energy = abs(amplitude.x) + abs(amplitude.y) + abs(amplitude.z) + abs(amplitude.w);
                float relative = field / max(energy, 1e-3);
                float lines = exp(-(relative * relative) / (_ChladniWidth * _ChladniWidth));
                float sparkle = Hash31(floor(input.positionOS * 320.0) + floor(_Time.y * 20.0));
                half chladni = lines * saturate(energy) * input.faceMask * (0.6 + 0.8 * sparkle * sparkle);
                emission += _ChladniColor.rgb * chladni * (0.75 + 0.25 * _Pulse);

                emission += _CrackColor.rgb * crack * (0.25 + 1.5 * heat + 0.4 * saturate(energy));

                // The sigil to ring: the target figure's nodal lines as a faint dotted blue ghost.
                float4 target = _TargetAmp;
                float targetField = target.x * PlateMode(_ModeM.x, _ModeN.x, _ModeSign.x, uv) +
                                    target.y * PlateMode(_ModeM.y, _ModeN.y, _ModeSign.y, uv) +
                                    target.z * PlateMode(_ModeM.z, _ModeN.z, _ModeSign.z, uv) +
                                    target.w * PlateMode(_ModeM.w, _ModeN.w, _ModeSign.w, uv);
                float targetEnergy = abs(target.x) + abs(target.y) + abs(target.z) + abs(target.w);
                float targetRelative = targetField / max(targetEnergy, 1e-3);
                float dots = step(0.5, frac((uv.x + uv.y) * 60.0));
                emission += _TargetColor.rgb * _TargetGlow * input.faceMask * dots *
                            exp(-(targetRelative * targetRelative) / 0.0025);

                emission += half3(1.0, 0.75, 0.35) * pourFront * 2.0;
                emission += _BaseColor.rgb * _Emissive;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = EvaluateAmbientProbeSRGB(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0 - 0.5 * groove;
                surface.emission = emission;
                surface.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP's shadow pass setup; used for the normal bias.
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
