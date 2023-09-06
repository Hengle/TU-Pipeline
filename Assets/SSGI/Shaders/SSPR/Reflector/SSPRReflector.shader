Shader "Hidden/SSGI/SSPRReflector" {
    Properties {
        _MainColor("Main Color", Color) = (1.0, 1.0, 1.0, 1.0)
        [Header(Reflection)]
        _ReflectIntensity ("Intensity", Range(0.0, 1.0)) = 1.0
        _Noise ("Noise", Range(0.0, 3.0)) = 0.3
        [Header(Skybox)]
        _Skybox ("Skybox", CUBE) = "_Skybox" {}
        _Smoothness ("Smoothness", Range(0.0, 8.0)) = 3.0
        _SkyboxIntensity ("Intensity", Range(0.0, 1.0)) = 0.5
    }
    SubShader {
        Tags {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
        }
        Pass {
            Name "SSPR Reflector Pass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SSPRReflectorPass.hlsl"

            #pragma vertex SSPRReflectorPassVertex
            #pragma fragment SSPRReflectorPassFragment
            ENDHLSL
        }
    }
}