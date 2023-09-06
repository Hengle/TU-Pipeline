#ifndef _SMOOTHNESS_PASS_INCLUDED
#define _SMOOTHNESS_PASS_INCLUDED


#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes {
    float3 positionOS : POSITION;
    float4 texcoord : TEXCOORD0;
    float3 normalOS : NORMAL;
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
};


Varyings Vert(Attributes input) {
    Varyings output;

    output.positionCS = TransformObjectToHClip(input.positionOS);
    output.positionWS = TransformObjectToWorld(input.positionOS);
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);

    return output;
}

half4 SpecularGBufferPassFragment(Varyings input) : SV_Target {
    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    return half4(brdfData.specular, brdfData.perceptualRoughness);
}

half4 ReflectionGBufferPassFragment(Varyings input) : SV_Target {
    float2 uv = input.uv;
    half3 nDirWS = normalize(input.normalWS);
    half3 vDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 rDirWS = reflect(-vDirWS, nDirWS);
    float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

    half NoV = saturate(dot(nDirWS, vDirWS));
    half fresnelTerm = Pow4(1.0 - NoV);

    SurfaceData surfaceData;
    BRDFData brdfData;
    InitializeStandardLitSurfaceData(uv, surfaceData);
    InitializeBRDFData(surfaceData, brdfData);

    half3 indirectSpecular = GlossyEnvironmentReflection(rDirWS, input.positionWS, brdfData.perceptualRoughness, 1.0h, normalizedScreenSpaceUV);

    indirectSpecular *= EnvironmentBRDFSpecular(brdfData, fresnelTerm);

    #ifdef _SSR_ON
    return half4(indirectSpecular, _SSRIntensity);
    #endif
    return half4(indirectSpecular, 1.0);
}

half4 BaseColorGBufferPassFragment(Varyings input) : SV_Target {
    return _BaseColor;
}

#endif
