Shader "ComputeCrowd/CrowdRender"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BillboardMap("Billboard Map", 2D) = "black" {}
        _OutfitDataMap("Outfit Data Map", 2D) = "black" {}
        _ColorTint("Color Tint", Color) = (0.8, 0.82, 0.86, 1)
        _AmbientScale("Ambient Scale", Range(0, 1)) = 0.18
        _DirectLightScale("Direct Light Scale", Range(0, 1)) = 0.4
        _Brightness("Brightness", Range(0, 1)) = 0.55
        _BaseAreaBrightness("Base Area Brightness", Range(0, 2)) = 1
        [HideInInspector] _FallbackTransitionFade("Fallback Transition Fade", Float) = 1
        [HideInInspector] _FallbackColorR("Fallback Color R", Vector) = (1, 0, 0, 0)
        [HideInInspector] _FallbackColorG("Fallback Color G", Vector) = (0, 1, 0, 0)
        [HideInInspector] _FallbackColorB("Fallback Color B", Vector) = (0, 0, 1, 0)
        [HideInInspector] _FallbackColorA("Fallback Color A", Vector) = (1, 1, 1, 0)
        [HideInInspector] _FallbackAnimData("Fallback Anim Data", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BillboardMap);
            SAMPLER(sampler_BillboardMap);
            TEXTURE2D(_OutfitDataMap);
            SAMPLER(sampler_OutfitDataMap);
            TEXTURE2D(_PoseTexture);
            SAMPLER(sampler_PoseTexture);
            float4 _PoseTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _ClipMeta0;
                float4 _ClipMeta1;
                float4 _ClipMeta2;
                float4 _ColorTint;
                float4 _FallbackColorR;
                float4 _FallbackColorG;
                float4 _FallbackColorB;
                float4 _FallbackColorA;
                float4 _FallbackAnimData;
                float _FallbackTransitionFade;
                float _AmbientScale;
                float _DirectLightScale;
                float _Brightness;
                float _BaseAreaBrightness;
                int _BoneCount;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorR)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorG)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorB)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorA)
                UNITY_DEFINE_INSTANCED_PROP(float4, _AnimData)
                UNITY_DEFINE_INSTANCED_PROP(float, _TransitionFade)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            #if defined(SHADER_API_GLES3)
                #define CROWD_BLENDINDICES_TYPE float4
            #else
                #define CROWD_BLENDINDICES_TYPE uint4
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 weights : BLENDWEIGHTS;
                CROWD_BLENDINDICES_TYPE indices : BLENDINDICES;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float renderMode : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetClipMeta(float clipIndex)
            {
                if (clipIndex < 0.5)
                {
                    return _ClipMeta0;
                }

                if (clipIndex < 1.5)
                {
                    return _ClipMeta1;
                }

                return _ClipMeta2;
            }

            float4 GetAnimData()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _AnimData);
                #else
                    return _FallbackAnimData;
                #endif
            }

            half3 GetColorR()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorR).rgb;
                #else
                    return _FallbackColorR.rgb;
                #endif
            }

            half3 GetColorG()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorG).rgb;
                #else
                    return _FallbackColorG.rgb;
                #endif
            }

            half3 GetColorB()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorB).rgb;
                #else
                    return _FallbackColorB.rgb;
                #endif
            }

            half3 GetColorA()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorA).rgb;
                #else
                    return _FallbackColorA.rgb;
                #endif
            }

            float4 SamplePoseTexel(int bonePixel, int poseRow)
            {
                float2 uv = float2((bonePixel + 0.5) * _PoseTexture_TexelSize.x, (poseRow + 0.5) * _PoseTexture_TexelSize.y);
                return SAMPLE_TEXTURE2D_LOD(_PoseTexture, sampler_PoseTexture, uv, 0);
            }

            float3x4 SampleBoneMatrix(int boneIndex, int poseRow)
            {
                int basePixel = boneIndex * 3;
                float4 row0 = SamplePoseTexel(basePixel + 0, poseRow);
                float4 row1 = SamplePoseTexel(basePixel + 1, poseRow);
                float4 row2 = SamplePoseTexel(basePixel + 2, poseRow);
                return float3x4(row0, row1, row2);
            }

            float3x4 SampleAnimatedBone(int boneIndex, float4 animData)
            {
                float4 clipMeta = GetClipMeta(animData.x);
                float startRow = clipMeta.x;
                float frameCount = clipMeta.y;
                float normalizedTime = saturate(animData.y);

                if (frameCount <= 1.0)
                {
                    return SampleBoneMatrix(boneIndex, (int)startRow);
                }

                float framePosition = normalizedTime * (frameCount - 1.0);
                float frame0 = floor(framePosition);
                float frame1 = min(frame0 + 1.0, frameCount - 1.0);
                float interpolation = frac(framePosition);

                float3x4 matrix0 = SampleBoneMatrix(boneIndex, (int)(startRow + frame0));
                float3x4 matrix1 = SampleBoneMatrix(boneIndex, (int)(startRow + frame1));
                return lerp(matrix0, matrix1, interpolation);
            }

            int4 ResolveBoneIndices(float4 indices)
            {
                return int4(
                    (int)floor(indices.x + 0.5),
                    (int)floor(indices.y + 0.5),
                    (int)floor(indices.z + 0.5),
                    (int)floor(indices.w + 0.5));
            }

            int4 ResolveBoneIndices(uint4 indices)
            {
                return int4(indices);
            }

            float3 SkinPosition(float4 positionOS, float4 weights, CROWD_BLENDINDICES_TYPE indices, float4 animData)
            {
                int4 boneIndices = ResolveBoneIndices(indices);
                float3 skinned = 0;
                skinned += mul(SampleAnimatedBone(boneIndices.x, animData), positionOS) * weights.x;
                skinned += mul(SampleAnimatedBone(boneIndices.y, animData), positionOS) * weights.y;
                skinned += mul(SampleAnimatedBone(boneIndices.z, animData), positionOS) * weights.z;
                skinned += mul(SampleAnimatedBone(boneIndices.w, animData), positionOS) * weights.w;
                return skinned;
            }

            float3 SkinNormal(float3 normalOS, float4 weights, CROWD_BLENDINDICES_TYPE indices, float4 animData)
            {
                int4 boneIndices = ResolveBoneIndices(indices);
                float3 skinned =
                    mul((float3x3)SampleAnimatedBone(boneIndices.x, animData), normalOS) * weights.x +
                    mul((float3x3)SampleAnimatedBone(boneIndices.y, animData), normalOS) * weights.y +
                    mul((float3x3)SampleAnimatedBone(boneIndices.z, animData), normalOS) * weights.z +
                    mul((float3x3)SampleAnimatedBone(boneIndices.w, animData), normalOS) * weights.w;

                return normalize(skinned);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 animData = GetAnimData();
                output.renderMode = animData.z;
                float debugMode = animData.w;

                if (animData.z > 0.5)
                {
                    VertexPositionInputs billboardPositionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                    output.positionCS = billboardPositionInputs.positionCS;
                    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                    output.uv = input.uv;
                    return output;
                }

                float3 skinnedPositionOS;
                float3 skinnedNormalOS;
                if (debugMode > 1.5 && debugMode < 3.5)
                {
                    skinnedPositionOS = input.positionOS.xyz;
                    skinnedNormalOS = normalize(input.normalOS);
                }
                else
                {
                    skinnedPositionOS = SkinPosition(input.positionOS, input.weights, input.indices, animData);
                    skinnedNormalOS = SkinNormal(input.normalOS, input.weights, input.indices, animData);
                }

                VertexPositionInputs positionInputs = GetVertexPositionInputs(skinnedPositionOS);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(skinnedNormalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half3 ResolveOutfitColor(float4 mask)
            {
                half3 colorR = GetColorR();
                half3 colorG = GetColorG();
                half3 colorB = GetColorB();
                half3 colorA = GetColorA();
                return colorR * mask.r + colorG * mask.g + colorB * mask.b + colorA * mask.a;
            }

            half GetTransitionFade()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _TransitionFade);
                #else
                    return _FallbackTransitionFade;
                #endif
            }

            half InterleavedNoise(float2 pixelPosition)
            {
                return frac(52.9829189h * frac(dot(pixelPosition, float2(0.06711056h, 0.00583715h))));
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float debugMode = GetAnimData().w;
                clip(GetTransitionFade() - InterleavedNoise(input.positionCS.xy));

                if (debugMode > 2.5 && debugMode < 3.5)
                {
                    return half4(1.0h, 0.0h, 1.0h, 1.0h);
                }

                if (debugMode > 3.5)
                {
                    return half4(0.0h, 1.0h, 0.35h, 1.0h);
                }

                half3 albedo;
                if (input.renderMode > 0.5)
                {
                    half4 billboardSample = SAMPLE_TEXTURE2D(_BillboardMap, sampler_BillboardMap, input.uv);
                    clip(billboardSample.a - 0.1h);
                    albedo = billboardSample.rgb;
                }
                else
                {
                    half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                    half4 maskSample = SAMPLE_TEXTURE2D(_OutfitDataMap, sampler_OutfitDataMap, input.uv);

                    half maskWeight = saturate(maskSample.r + maskSample.g + maskSample.b + maskSample.a);
                    half3 baseColor = baseSample.rgb * _BaseAreaBrightness;
                    half3 outfitColor = ResolveOutfitColor(maskSample);
                    albedo = lerp(baseColor, outfitColor, maskWeight);
                }

                Light mainLight = GetMainLight();
                half3 normalWS = normalize(input.normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS) * _AmbientScale;
                half3 directLighting = mainLight.color * ((0.2h + ndotl * 0.8h) * _DirectLightScale);
                half3 lighting = ambient + directLighting;
                half3 finalColor = albedo * lighting;
                finalColor *= _ColorTint.rgb * _Brightness;

                return half4(finalColor, 1.0h);
            }
            ENDHLSL
        }
    }
}
