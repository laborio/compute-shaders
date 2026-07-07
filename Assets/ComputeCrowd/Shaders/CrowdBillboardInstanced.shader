Shader "ComputeCrowd/BillboardInstanced"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _Tint("Tint", Color) = (0.8, 0.82, 0.86, 1)
        _Brightness("Brightness", Range(0, 1)) = 0.5
        _BillboardScale("Billboard Scale", Float) = 1
        _BillboardDepthOffset("Billboard Depth Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float _Brightness;
                float _BillboardScale;
                float _BillboardDepthOffset;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float, _TransitionFade)
                UNITY_DEFINE_INSTANCED_PROP(float, _BillboardHeightOffset)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            half GetTransitionFade()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _TransitionFade);
                #else
                    return 1.0h;
                #endif
            }

            float GetBillboardHeightOffset()
            {
                #if defined(UNITY_INSTANCING_ENABLED)
                    return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _BillboardHeightOffset);
                #else
                    return 0.0;
                #endif
            }

            half InterleavedNoise(float2 pixelPosition)
            {
                return frac(52.9829189h * frac(dot(pixelPosition, float2(0.06711056h, 0.00583715h))));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 centerWS = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);

                float3 sourceForward = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22);
                sourceForward.y = 0.0;
                float sourceForwardLengthSq = dot(sourceForward, sourceForward);
                if (sourceForwardLengthSq < 0.0001)
                {
                    sourceForward = float3(0.0, 0.0, 1.0);
                }
                else
                {
                    sourceForward *= rsqrt(sourceForwardLengthSq);
                }

                centerWS += sourceForward * _BillboardDepthOffset;
                centerWS.y += GetBillboardHeightOffset();

                float3 toCamera = _WorldSpaceCameraPos - centerWS;
                toCamera.y = 0.0;
                float toCameraLengthSq = dot(toCamera, toCamera);
                if (toCameraLengthSq < 0.0001)
                {
                    toCamera = float3(0.0, 0.0, 1.0);
                }
                else
                {
                    toCamera *= rsqrt(toCameraLengthSq);
                }

                float3 billboardRight = normalize(cross(float3(0.0, 1.0, 0.0), toCamera));
                float3 billboardUp = float3(0.0, 1.0, 0.0);

                float3 objectScale = float3(
                    length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20)),
                    length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21)),
                    length(float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22)));

                float2 scaledOffset = input.positionOS.xy * objectScale.xy * _BillboardScale;
                float3 positionWS = centerWS + billboardRight * scaledOffset.x + billboardUp * scaledOffset.y;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(color.a - 0.1h);
                clip(GetTransitionFade() - InterleavedNoise(input.positionCS.xy));
                color.rgb *= _Tint.rgb * _Brightness;
                return color;
            }
            ENDHLSL
        }
    }
}
