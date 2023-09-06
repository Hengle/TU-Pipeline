Shader "Hidden/Anti-Aliasing/TAA" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "TAAPass.hlsl"
        ENDHLSL

        Pass {
            Name "TAA Pass"

            HLSLPROGRAM
            #pragma multi_compile _ _MOTION_CAMERA _MOTION_OBJECT
            #pragma vertex Vert
            #pragma fragment TAAPassFragment
            ENDHLSL
        }
    }
}