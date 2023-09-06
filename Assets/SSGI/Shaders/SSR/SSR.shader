Shader "Hidden/SSGI/SSR/SSR" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "SSRPass.hlsl"
        ENDHLSL

        Pass {
            Name "ScreenSpaceRaymarching Pass"

            HLSLPROGRAM
            #pragma shader_feature _JITTER_ON
            #pragma shader_feature _HIZ_ON
            #pragma vertex Vert
            #pragma fragment SSRPassFragment
            ENDHLSL
        }

        Pass {
            Name "SSR Blur Pass"
            HLSLPROGRAM
            #pragma vertex SSRBlurPassVertex
            #pragma fragment SSRBlurPassFragment
            ENDHLSL
        }

        Pass {
            Name "SSR Addtive Pass"

            ZTest NotEqual
            ZWrite Off
            Cull Off
            Blend One One, One Zero

            HLSLPROGRAM
            #pragma multi_compile _ _GBUFFER_ON
            #pragma vertex Vert
            #pragma fragment SSRFinalPassFragment
            ENDHLSL
        }

        Pass {
            Name "SSR Balance Pass"

            ZTest NotEqual
            ZWrite Off
            Cull Off
            Blend SrcColor OneMinusSrcColor, One Zero

            HLSLPROGRAM
            #pragma multi_compile _ _GBUFFER_ON
            #pragma vertex Vert
            #pragma fragment SSRFinalPassFragment
            ENDHLSL
        }

    }
}