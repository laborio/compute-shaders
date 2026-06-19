Shader "ComputeCrowd/MeshFallbackTextured"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _OutfitDataMap("Outfit Data Map", 2D) = "black" {}
        _ColorR("Color R", Color) = (1, 0, 0, 1)
        _ColorG("Color G", Color) = (0, 1, 0, 1)
        _ColorB("Color B", Color) = (0, 0, 1, 1)
        _ColorA("Color A", Color) = (1, 1, 1, 1)
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
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OutfitDataMap);
            SAMPLER(sampler_OutfitDataMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _ColorR;
                float4 _ColorG;
                float4 _ColorB;
                float4 _ColorA;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half3 ResolveOutfitColor(half4 mask)
            {
                return _ColorR.rgb * mask.r +
                    _ColorG.rgb * mask.g +
                    _ColorB.rgb * mask.b +
                    _ColorA.rgb * mask.a;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 maskSample = SAMPLE_TEXTURE2D(_OutfitDataMap, sampler_OutfitDataMap, input.uv);
                half maskWeight = saturate(maskSample.r + maskSample.g + maskSample.b + maskSample.a);
                half3 outfitColor = ResolveOutfitColor(maskSample);
                half3 albedo = lerp(baseSample.rgb, outfitColor, maskWeight);
                return half4(albedo, 1.0h);
            }
            ENDHLSL
        }
    }
}
