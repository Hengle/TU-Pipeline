Shader "Hidden/SSGI/HierarchicalZBuffer" {
    SubShader {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "HierarchicalZBufferPass.hlsl"
        ENDHLSL

        Pass {
            Name "SSAO Occlusion Pass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SSAOPassFragment
            ENDHLSL
        }
    }
}