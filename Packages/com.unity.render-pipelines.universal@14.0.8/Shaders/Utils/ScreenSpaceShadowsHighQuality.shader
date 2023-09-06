Shader "Hidden/Universal Render Pipeline/ScreenSpaceShadowsHighQuality" {
    SubShader {
        Tags {
            "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"
        }
        
        Pass {
            Name "ScreenSpaceShadow PreMask Pass"
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma multi_compile _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "ScreenSpaceShadowsHighQualityPass.hlsl"

            #pragma vertex Vert
            #pragma fragment ScreenSpaceShadowmapMaskFragment
            ENDHLSL
        }

        Pass {
            Name "ScreenSpaceShadowmap Blur Pass"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #include "ScreenSpaceShadowsHighQualityPass.hlsl"

            #pragma vertex Vert
            #pragma fragment BlurPassFragment
            ENDHLSL
        }

        Pass {
            Name "ScreenSpaceShadows Pass"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "ScreenSpaceShadowsHighQualityPass.hlsl"

            #pragma vertex Vert
            #pragma fragment ScreenSpaceShadowsPassFragment
            ENDHLSL
        }

    }
}