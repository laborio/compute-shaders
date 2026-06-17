Shader "ComputeCrowd/PitchLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Grass Base", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (0.345098, 0.588235, 0.313726, 1)
        _NormalMap("Grass Normal", 2D) = "bump" {}
        _FieldLinesMap("Field Lines", 2D) = "white" {}
        _LawnStripeMap("Lawn Stripe", 2D) = "white" {}
        _LargeNoiseMap("Lawn Noise", 2D) = "gray" {}

        _UseBaseTexture("Use Base Texture", Float) = 1
        _UseNormalMap("Use Normal Map", Float) = 1
        _UseSecondGrassSample("Use Second Grass Sample", Float) = 1
        _MaterialIsUnlit("Force Unlit", Float) = 0
        _MaterialReceivesShadows("Receives Shadows", Float) = 1

        _NormalStrength("Normal Strength", Range(0, 4)) = 1
        _Opacity("Opacity", Range(0, 1)) = 1
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.12
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.08

        _MaterialHighlightColor("Highlight Color", Color) = (1, 1, 1, 1)
        _MaterialHighlightStrength("Highlight Strength", Range(0, 1)) = 0

        _GrassColorA("Grass Color A", Color) = (0.85, 0.95, 0.82, 1)
        _GrassColorB("Grass Color B", Color) = (1.08, 1.02, 0.88, 1)
        _LargeNoiseScale("Large Noise Scale", Float) = 0.678
        _LargeNoiseContrast("Large Noise Contrast", Float) = 0.489
        _LargeNoiseStrength("Large Noise Strength", Float) = 0

        _LawnHighlightColor("Lawn Highlight Color", Color) = (1.2, 1.18, 1.1, 1)
        _LawnHighlightScale("Lawn Highlight Scale", Float) = 5.71
        _LawnHighlightContrast("Lawn Highlight Contrast", Float) = 0.524
        _LawnHighlightStrength("Lawn Highlight Strength", Float) = 0.203

        _LawnHighlight2Color("Lawn Highlight 2 Color", Color) = (0.625, 1, 0.49, 1)
        _LawnHighlight2Scale("Lawn Highlight 2 Scale", Float) = 0.728
        _LawnHighlight2Contrast("Lawn Highlight 2 Contrast", Float) = 4
        _LawnHighlight2Strength("Lawn Highlight 2 Strength", Float) = 0.025

        _LawnStripe2Center("Lawn Stripe 2 Center", Vector) = (0.499, 0.499, 0, 0)
        _LawnStripe2HalfSize("Lawn Stripe 2 Half Size", Vector) = (0.5, 0.5, 0, 0)
        _LawnStripe2TextureOffset("Lawn Stripe 2 Texture Offset", Vector) = (0.006, -0.051, 0, 0)
        _LawnStripe2TextureScale("Lawn Stripe 2 Texture Scale", Float) = 13.438
        _LawnStripe2RotationDegrees("Lawn Stripe 2 Rotation Degrees", Float) = 39.263
        _LawnStripe2Strength("Lawn Stripe 2 Strength", Range(0, 1)) = 0.334

        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Blend("__blend", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2
        [HideInInspector] _AlphaClip("__clip", Float) = 0
        [HideInInspector] _Cutoff("__cutoff", Float) = 0.5
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FieldLinesMap);
            SAMPLER(sampler_FieldLinesMap);
            TEXTURE2D(_LawnStripeMap);
            SAMPLER(sampler_LawnStripeMap);
            TEXTURE2D(_LargeNoiseMap);
            SAMPLER(sampler_LargeNoiseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _MaterialHighlightColor;
                float4 _GrassColorA;
                float4 _GrassColorB;
                float4 _LawnHighlightColor;
                float4 _LawnHighlight2Color;
                float4 _LawnStripe2Center;
                float4 _LawnStripe2HalfSize;
                float4 _LawnStripe2TextureOffset;
                float _UseBaseTexture;
                float _UseNormalMap;
                float _UseSecondGrassSample;
                float _MaterialIsUnlit;
                float _MaterialReceivesShadows;
                float _NormalStrength;
                float _Opacity;
                float _AmbientStrength;
                float _Smoothness;
                float _SpecularStrength;
                float _MaterialHighlightStrength;
                float _LargeNoiseScale;
                float _LargeNoiseContrast;
                float _LargeNoiseStrength;
                float _LawnHighlightScale;
                float _LawnHighlightContrast;
                float _LawnHighlightStrength;
                float _LawnHighlight2Scale;
                float _LawnHighlight2Contrast;
                float _LawnHighlight2Strength;
                float _LawnStripe2TextureScale;
                float _LawnStripe2RotationDegrees;
                float _LawnStripe2Strength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 tiledUv : TEXCOORD3;
                float2 rawUv : TEXCOORD4;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD5;
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD6;
            #else
                half fogFactor : TEXCOORD6;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            static const float2 kLawnStripeMaskHalfSize = float2(0.435, 0.456);

            float2 Rotate2D(float2 value, float radiansValue)
            {
                float s = sin(radiansValue);
                float c = cos(radiansValue);
                return float2(c * value.x - s * value.y, s * value.x + c * value.y);
            }

            half3 SamplePitchNormalTS(float2 tiledUv)
            {
                half3 tangentSpaceNormal = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, tiledUv).xyz * 2.0h - 1.0h;
                tangentSpaceNormal.xy *= _NormalStrength;
                return normalize(tangentSpaceNormal);
            }

            half3 BuildPitchAlbedo(float2 rawUv, float2 tiledUv)
            {
                half3 baseColor = _BaseColor.rgb;

                if (_UseBaseTexture > 0.5)
                {
                    half3 grassSampleA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, tiledUv).rgb;

                    if (_UseSecondGrassSample > 0.5)
                    {
                        float2 tiledUv2 = Rotate2D(rawUv, 0.65);
                        tiledUv2 *= _BaseMap_ST.xy * 0.73;
                        tiledUv2 += _BaseMap_ST.zw + float2(0.37, 0.19);

                        half3 grassSampleB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, tiledUv2).rgb;
                        grassSampleA = lerp(grassSampleA, grassSampleB, 0.35h);
                    }

                    baseColor = grassSampleA * _BaseColor.rgb;
                }

                float largeNoiseRaw = SAMPLE_TEXTURE2D(_LargeNoiseMap, sampler_LargeNoiseMap, rawUv * _LargeNoiseScale).r;
                float largeNoise = saturate((largeNoiseRaw - 0.5) * _LargeNoiseContrast + 0.5);
                float signedLargeNoise = largeNoise * 2.0 - 1.0;

                baseColor *= lerp(_GrassColorA.rgb, _GrassColorB.rgb, largeNoise);
                baseColor *= 1.0 + signedLargeNoise * _LargeNoiseStrength;

                float lawnHighlightRaw = SAMPLE_TEXTURE2D(_LargeNoiseMap, sampler_LargeNoiseMap, rawUv * _LawnHighlightScale).r;
                float lawnHighlight = saturate((lawnHighlightRaw - 0.5) * _LawnHighlightContrast + 0.5);
                float lawnHighlightMask = smoothstep(0.55, 0.9, lawnHighlight);
                baseColor = lerp(baseColor, _LawnHighlightColor.rgb, lawnHighlightMask * _LawnHighlightStrength);

                float2 lawnHighlight2Uv = Rotate2D(rawUv, -0.41);
                lawnHighlight2Uv *= _LawnHighlight2Scale;
                lawnHighlight2Uv += float2(0.23, 0.61);
                float lawnHighlight2Raw = SAMPLE_TEXTURE2D(_LargeNoiseMap, sampler_LargeNoiseMap, lawnHighlight2Uv).r;
                float lawnHighlight2 = saturate((lawnHighlight2Raw - 0.5) * _LawnHighlight2Contrast + 0.5);
                float lawnHighlight2Mask = smoothstep(0.58, 0.92, lawnHighlight2);
                baseColor = lerp(baseColor, _LawnHighlight2Color.rgb, lawnHighlight2Mask * _LawnHighlight2Strength);

                float2 fieldLinesUv = 0.999 - abs(rawUv * 2.0 - 1.0);
                half4 fieldLinesSample = SAMPLE_TEXTURE2D(_FieldLinesMap, sampler_FieldLinesMap, saturate(fieldLinesUv));
                half3 fieldLinesColor = lerp(fieldLinesSample.rgb, half3(1.0, 1.0, 1.0), 0.45h);
                baseColor = lerp(baseColor, fieldLinesColor, fieldLinesSample.a);

                float2 lawnStripeMaskDistance = abs(rawUv - 0.5);
                float lawnStripeMask =
                    (1.0 - step(kLawnStripeMaskHalfSize.x, lawnStripeMaskDistance.x)) *
                    (1.0 - step(kLawnStripeMaskHalfSize.y, lawnStripeMaskDistance.y));

                half4 lawnStripeSample = SAMPLE_TEXTURE2D(_LawnStripeMap, sampler_LawnStripeMap, float2(rawUv.x, rawUv.y) * 9.0);
                baseColor = lerp(baseColor, lawnStripeSample.rgb, lawnStripeSample.a * lawnStripeMask);

                float2 lawnStripe2LocalUv = Rotate2D(rawUv - _LawnStripe2Center.xy, radians(_LawnStripe2RotationDegrees));
                float2 lawnStripe2MaskDistance = abs(lawnStripe2LocalUv);
                float lawnStripe2Mask =
                    (1.0 - step(_LawnStripe2HalfSize.x, lawnStripe2MaskDistance.x)) *
                    (1.0 - step(_LawnStripe2HalfSize.y, lawnStripe2MaskDistance.y));

                float2 lawnStripe2Uv =
                    (lawnStripe2LocalUv + _LawnStripe2TextureOffset.xy + _LawnStripe2HalfSize.xy) * _LawnStripe2TextureScale;
                half4 lawnStripe2Sample = SAMPLE_TEXTURE2D(_LawnStripeMap, sampler_LawnStripeMap, lawnStripe2Uv);
                baseColor = lerp(baseColor, lawnStripe2Sample.rgb, lawnStripe2Sample.a * lawnStripe2Mask * _LawnStripe2Strength);

                return lerp(baseColor, _MaterialHighlightColor.rgb, _MaterialHighlightStrength);
            }

            half3 ApplyLightContribution(Light light, half3 normalWS, half3 viewDirWS, half receivesShadows, out half3 specularTerm)
            {
                half shadowAttenuation = lerp(1.0h, light.shadowAttenuation, receivesShadows);
                half attenuation = light.distanceAttenuation * shadowAttenuation;
                half ndotl = saturate(dot(normalWS, light.direction));
                half3 diffuse = light.color * (attenuation * ndotl);

                half3 halfVector = SafeNormalize(light.direction + viewDirWS);
                half ndoth = saturate(dot(normalWS, halfVector));
                half specularPower = lerp(8.0h, 128.0h, _Smoothness);
                half specular = pow(ndoth, specularPower) * _SpecularStrength * ndotl * attenuation;
                specularTerm = light.color * specular;

                return diffuse;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS.xyz, input.tangentOS.w * GetOddNegativeScale());
                output.rawUv = input.uv;
                output.tiledUv = TRANSFORM_TEX(input.uv, _BaseMap);

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
            #endif

                half fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                output.fogFactor = fogFactor;
            #endif

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 albedo = BuildPitchAlbedo(input.rawUv, input.tiledUv);

                half3 normalWS = normalize(input.normalWS);
                if (_UseNormalMap > 0.5)
                {
                    half3 normalTS = SamplePitchNormalTS(input.tiledUv);
                    half3 bitangentWS = input.tangentWS.w * cross(normalWS, input.tangentWS.xyz);
                    half3x3 tangentToWorld = half3x3(normalize(input.tangentWS.xyz), normalize(bitangentWS), normalWS);
                    normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
                }

                half fogFactor;
                half3 vertexLight;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                fogFactor = input.fogFactorAndVertexLight.x;
                vertexLight = input.fogFactorAndVertexLight.yzw;
            #else
                fogFactor = input.fogFactor;
                vertexLight = 0;
            #endif

                if (_MaterialIsUnlit > 0.5)
                {
                    return half4(MixFog(albedo, fogFactor), _Opacity);
                }

                half3 viewDirWS = SafeNormalize(GetWorldSpaceNormalizeViewDir(input.positionWS));
                half receivesShadows = _MaterialReceivesShadows > 0.5 ? 1.0h : 0.0h;

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                float4 shadowCoord = 0;
            #endif

                half3 diffuseLighting = SampleSH(normalWS) * _AmbientStrength + vertexLight;
                half3 specularLighting = 0;

                Light mainLight = GetMainLight(shadowCoord);
                half3 mainSpecular;
                diffuseLighting += ApplyLightContribution(mainLight, normalWS, viewDirWS, receivesShadows, mainSpecular);
                specularLighting += mainSpecular;

            #ifdef _ADDITIONAL_LIGHTS
                uint additionalLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(additionalLightCount)
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS);
                    half3 additionalSpecular;
                    diffuseLighting += ApplyLightContribution(additionalLight, normalWS, viewDirWS, receivesShadows, additionalSpecular);
                    specularLighting += additionalSpecular;
                LIGHT_LOOP_END
            #endif

                half3 finalColor = albedo * diffuseLighting + specularLighting;
                finalColor = MixFog(finalColor, fogFactor);
                return half4(finalColor, _Opacity);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}
