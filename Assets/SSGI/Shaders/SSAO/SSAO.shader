Shader "Hidden/SSGI/AO/SSAO" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "SSAOPass.hlsl"
        ENDHLSL

        Pass {
            Name "SSAO Occlusion Pass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SSAOPassFragment
            ENDHLSL
        }

        Pass {
            Name "SSAO Bilateral Blur Pass"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurPassFragment
            ENDHLSL
        }

        Pass {
            Name "SSAO Final Pass"

            ZTest NotEqual
            ZWrite Off
            Cull Off
            Blend One SrcAlpha, Zero One
            BlendOp Add, Add


            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FinalPassFragment
            ENDHLSL
        }

        Pass {
            Name "SSAO Preview Pass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment PreviewPassFragment
            ENDHLSL
        }
    }
}