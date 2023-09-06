Shader "Hidden/Anti-Aliasing/FXAA" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "FXAAPass.hlsl"
        ENDHLSL

        Pass {
            Name "Luminance Prefilter Pass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LuminancePrefilterPassFragment
            ENDHLSL
        }

        Pass {
            Name "FXAA Pass"

            HLSLPROGRAM
            #pragma multi_compile _ _LOW_QUALITY
            #pragma vertex Vert
            #pragma fragment FXAAPassFragment
            ENDHLSL
        }
    }
}