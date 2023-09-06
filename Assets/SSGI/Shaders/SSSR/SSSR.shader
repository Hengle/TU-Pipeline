Shader "Hidden/SSGI/SSR/SSSR" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "SSSRPass.hlsl"
        ENDHLSL

        Pass {
            Name "ScreenSpaceRaymarching Pass"

            HLSLPROGRAM
            #pragma shader_feature _JITTER_ON
            #pragma shader_feature _HIZ_ON
            #pragma vertex Vert
            #pragma fragment SSSRPassFragment
            ENDHLSL
        }

        Pass {
            Name "Spatio Filter Sampler Pass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SpatioFilterPassFragment
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
            Name "Combine Pass"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CombinePassFragment
            ENDHLSL
        }

    }
}