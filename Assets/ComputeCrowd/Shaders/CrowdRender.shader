Shader "ComputeCrowd/CrowdRender"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BillboardMap("Billboard Map", 2D) = "black" {}
        _OutfitDataMap("Outfit Data Map", 2D) = "black" {}
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
                int _BoneCount;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorR)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorG)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorB)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorA)
                UNITY_DEFINE_INSTANCED_PROP(float4, _AnimData)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 weights : BLENDWEIGHTS;
                uint4 indices : BLENDINDICES;
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

            float3 SkinPosition(float4 positionOS, float4 weights, uint4 indices, float4 animData)
            {
                float3 skinned = 0;
                skinned += mul(SampleAnimatedBone(indices.x, animData), positionOS) * weights.x;
                skinned += mul(SampleAnimatedBone(indices.y, animData), positionOS) * weights.y;
                skinned += mul(SampleAnimatedBone(indices.z, animData), positionOS) * weights.z;
                skinned += mul(SampleAnimatedBone(indices.w, animData), positionOS) * weights.w;
                return skinned;
            }

            float3 SkinNormal(float3 normalOS, float4 weights, uint4 indices, float4 animData)
            {
                float3 skinned =
                    mul((float3x3)SampleAnimatedBone(indices.x, animData), normalOS) * weights.x +
                    mul((float3x3)SampleAnimatedBone(indices.y, animData), normalOS) * weights.y +
                    mul((float3x3)SampleAnimatedBone(indices.z, animData), normalOS) * weights.z +
                    mul((float3x3)SampleAnimatedBone(indices.w, animData), normalOS) * weights.w;

                return normalize(skinned);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 animData = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _AnimData);
                output.renderMode = animData.z;

                if (animData.z > 0.5)
                {
                    VertexPositionInputs billboardPositionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                    output.positionCS = billboardPositionInputs.positionCS;
                    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                    output.uv = input.uv;
                    return output;
                }

                float3 skinnedPositionOS = SkinPosition(input.positionOS, input.weights, input.indices, animData);
                float3 skinnedNormalOS = SkinNormal(input.normalOS, input.weights, input.indices, animData);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(skinnedPositionOS);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(skinnedNormalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half3 ResolveOutfitColor(float4 mask)
            {
                half3 colorR = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorR).rgb;
                half3 colorG = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorG).rgb;
                half3 colorB = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorB).rgb;
                half3 colorA = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _ColorA).rgb;
                return colorR * mask.r + colorG * mask.g + colorB * mask.b + colorA * mask.a;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

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
                    half3 outfitColor = ResolveOutfitColor(maskSample);
                    albedo = lerp(baseSample.rgb, outfitColor, maskWeight);
                }

                Light mainLight = GetMainLight();
                half3 normalWS = normalize(input.normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + (mainLight.color * (0.2h + ndotl * 0.8h));

                return half4(albedo * lighting, 1.0h);
            }
            ENDHLSL
        }
    }
}
