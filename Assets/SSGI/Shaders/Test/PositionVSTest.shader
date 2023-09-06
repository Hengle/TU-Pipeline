Shader "My Shader/PositionVSTest" {
    Properties {}
    SubShader {
        Tags {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }
        Pass {
            Name "Pass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma vertex vert
            #pragma fragment frag


            struct Attributes {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionVS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };


            Varyings vert(Attributes input) {
                Varyings output;

                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInputs.positionCS;
                output.positionVS = vertexInputs.positionVS;
                output.normalWS = TransformObjectToWorldNormal(input.normal);

                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                input.normalWS = TransformWorldToViewNormal(input.normalWS);
                input.normalWS = normalize(input.normalWS);
                half3 finalCol = input.normalWS * 0.5 + 0.5;

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
}