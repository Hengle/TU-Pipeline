Shader "Hidden/SSGI/SSGI" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "SSGIPass.hlsl"
        ENDHLSL

        Pass {
            Name "ScreenSpaceRaymarching Pass"

            HLSLPROGRAM
            #pragma shader_feature _JITTER_ON
            #pragma shader_feature _HIZ_ON
            #pragma vertex Vert
            #pragma fragment SSGIPassFragment
            ENDHLSL
        }

        Pass {
            Name "Temporal Anti-Aliasing Pass"

            HLSLPROGRAM
            #pragma multi_compile _ _MOTION_CAMERA _MOTION_OBJECT
            #pragma vertex Vert
            #pragma fragment TemporalAAPassFragment
            ENDHLSL
        }

        Pass {
            Name "Bilateral Filter Pass"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BilateralFilterPassFragment
            ENDHLSL
        }

        Pass {
            Name "Combine Pass"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CombinePassFragment
            ENDHLSL
        }

    }
}