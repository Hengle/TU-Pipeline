#ifndef _SSSR_PASS_INCLUDED
#define _SSSR_PASS_INCLUDED

#include "../Common/ScreenSpaceLibrary.hlsl"
#include "../Common/BSDFLibrary.hlsl"
#include "../Common/TemporalAALibrary.hlsl"
#include "../Common/CameraGBufferLibrary.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


TEXTURE2D(_BlueNoise);
SAMPLER(sampler_BlueNoise);

TEXTURE2D(_SSRColorPDFTexture);
SAMPLER(sampler_SSRColorPDFTexture);

TEXTURE2D(_MaskDepthHitUVTexture);
SAMPLER(sampler_MaskDepthHitUVTexture);

TEXTURE2D(_PreintegratedGF_LUT);
SAMPLER(sampler_PreintegratedGF_LUT);

TEXTURE2D(_SSSRTemporalAATexture);
SAMPLER(sampler_SSSRTemporalAATexture);


float4 _SourceSize;

float4 _SSSRParams0;
float4 _SSSRParams1;

float4 _SSSRJitter;

float4 _SSSRBlueNoiseSize;

#define MAXDISTANCE _SSSRParams0.x
#define STRIDE _SSSRParams0.y
// 遍历次数
#define STEPCOUNT _SSSRParams0.z
// 能反射和不可能的反射之间的界限
#define THICKNESS _SSSRParams0.w

#define SPP _SSSRParams1.y
#define BRDFBIAS _SSSRParams1.z
#define SCREENFADE _SSSRParams1.w

half4 GetSource(float2 uv) {
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
}

void SSSRPassFragment(Varyings input, out half4 SSRColorPDF : SV_Target0, out half4 MaskDepthHitUV : SV_Target1) {
    float2 uv = input.texcoord;

    float rawDepth = SampleSceneDepth(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float3 vpos = ReconstructViewPos(uv, linearDepth);
    float3 vDir = normalize(vpos);
    // 视空间坐标
    vpos = _WorldSpaceCameraPos + vpos;
    float3 startView = TransformWorldToView(vpos);

    half roughness = SampleSceneRoughness(uv);
    float3 normal = SampleSceneNormals(uv);

    half4 outColor = 0.0;
    half2 outUV = 0.0;
    half outMask = 0.0;
    half outDepth = 0.0;
    half outPDF = 0.0;

    UNITY_LOOP
    for (uint i = 0; i < SPP; i++) {
        // GGX重要性采样世界空间法线
        half4 h = 0.0;
        half2 hash = SAMPLE_TEXTURE2D_LOD(_BlueNoise, sampler_BlueNoise, (uv + sin(i + _SSSRJitter.xy) * _SourceSize.xy * _SSSRBlueNoiseSize.zw), 0);
        hash.y = lerp(hash.y, 0.0, BRDFBIAS);
        if (roughness > 0.1) {
            h = ImportanceSampleGGX(hash, normal, roughness);
        }
        else {
            h = half4(normal, 1.0);
        }

        float3 rDir = TransformWorldToViewDir(reflect(vDir, h.xyz), true);

        float4 hitData = HierarchicalZScreenSpaceRayMarching(startView, rDir, _SourceSize, MAXDISTANCE, STRIDE, STEPCOUNT, THICKNESS);

        half4 sampleColor = GetSource(hitData.xy);
        sampleColor.rgb /= 1 + Luminance(sampleColor.rgb);

        outColor += sampleColor * hitData.w;
        outMask += hitData.w * GetScreenFadeBord(hitData.xy, SCREENFADE);
        outDepth += hitData.z;
        outUV += hitData.xy;
        outPDF += h.a;
    }

    // output
    outColor /= SPP;
    outColor.rgb /= 1 - Luminance(outColor.rgb);
    outMask /= SPP;
    outDepth /= SPP;
    outUV /= SPP;
    outPDF /= SPP;

    SSRColorPDF = half4(outColor.rgb, outPDF);
    MaskDepthHitUV = half4(outMask * outMask, outDepth, outUV);
}

static const int2 offset[9] = {int2(-2.0, -2.0), int2(0.0, -2.0), int2(2.0, -2.0), int2(-2.0, 0.0), int2(0.0, 0.0), int2(2.0, 0.0), int2(-2.0, 2.0), int2(0.0, 2.0), int2(2.0, 2.0)};

half4 SpatioFilterPassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;

    float rawDepth = SampleSceneDepth(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    
    float3 wpos = ReconstructViewPos(uv, linearDepth);
    float3 vDir = normalize(wpos);

    wpos = _WorldSpaceCameraPos + wpos;

    half roughness = SampleSceneRoughness(uv);
    float3 normal = SampleSceneNormals(uv);

    float2 BlueNoise = SAMPLE_TEXTURE2D(_BlueNoise, sampler_BlueNoise, (uv + _SSSRJitter) * _SourceSize.xy / 1024) * 2 - 1;
    float2x2 OffsetRotationMatrix = float2x2(BlueNoise.x, BlueNoise.y, -BlueNoise.y, -BlueNoise.x);

    float numWeight = 0.0, weight = 0.0;
    float2 offsetUV = 0.0, neighborUV = 0.0;
    float4 sampleColor = 0.0, reflectionColor = 0.0;

    for (int i = 0; i < 9; i++) {
        offsetUV = mul(OffsetRotationMatrix, offset[i] * _SourceSize.zw);
        neighborUV = uv + offsetUV;

        half4 colorPDF = SAMPLE_TEXTURE2D(_SSRColorPDFTexture, sampler_SSRColorPDFTexture, neighborUV);
        half4 maskDepthHitUV = SAMPLE_TEXTURE2D(_MaskDepthHitUVTexture, sampler_MaskDepthHitUVTexture, neighborUV);
        half3 hitWorldPos = ReconstructViewPos(maskDepthHitUV.ba, maskDepthHitUV.g) + _WorldSpaceCameraPos;

        // Spatio Filter Sampler
        weight = localBRDF(vDir, normalize(hitWorldPos - wpos), normal, roughness) / max(1e-5, colorPDF.a);
        sampleColor.rgb = colorPDF.rgb;
        sampleColor.a = maskDepthHitUV.r;

        reflectionColor += sampleColor * weight;
        numWeight += weight;
    }

    reflectionColor /= numWeight;
    reflectionColor = max(1e-5, reflectionColor);

    return reflectionColor;
}

TEXTURE2D(_SSSRAccumulationTexture);
SAMPLER(sampler_SSSRAccumulationTexture);

// 取得采样点附近距离相机最近的点偏移 用hit的depth数据
float2 AdjustBestDepthOffsetOfHitDepth(float2 uv) {
    half bestDepth = 1.0f;
    float2 uvOffset = 0.0f;

    UNITY_UNROLL
    for (int k = 0; k < 9; k++) {
        half depth = SAMPLE_TEXTURE2D_LOD(_MaskDepthHitUVTexture, sampler_MaskDepthHitUVTexture, uv + kOffssets3x3[k] * _SourceSize.zw, 0.0).g;
        #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
        #endif

        if (depth < bestDepth) {
            bestDepth = depth;
            uvOffset = kOffssets3x3[k] * _SourceSize.zw;
        }
    }
    return uvOffset;
}

half4 TemporalAAPassFragment(Varyings input) : SV_Target {
    float2 depthOffsetUV = AdjustBestDepthOffsetOfHitDepth(input.texcoord);
    float2 velocity = 0.0;
    #if defined (_MOTION_CAMERA)
    velocity =  ComputeCameraVelocity(input.texcoord + depthOffsetUV);
    #elif defined(_MOTION_OBJECT)
    velocity = SampleMotionVector(input.texcoord + depthOffsetUV);
    #endif
    float2 historyUV = input.texcoord - velocity;

    // 采样上一帧和这一帧
    float4 accum = GetAccumulation(historyUV, TEXTURE2D_ARGS(_SSSRAccumulationTexture, sampler_SSSRAccumulationTexture));
    float4 source = GetSource(input.texcoord);

    // 得到这一帧的AABB
    half4 boxMin, boxMax;
    AdjustColorBox(input.texcoord, TEXTURE2D_ARGS(_BlitTexture, sampler_LinearClamp), boxMin, boxMax, _SourceSize.zw);

    // clip
    accum = ClipToAABBCenter(accum, boxMin, boxMax);

    float frameInfluence = saturate(_FrameInfluence + length(velocity) * 100);

    return accum * (1.0 - frameInfluence) + source * frameInfluence;
}

//
// Combine Pass
//

half4 CombinePassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;

    float3 normal = SampleSceneNormals(uv);
    half4 specular = SampleSceneSpecular(uv);
    half roughness = GetSceneRoughness(specular);

    float rawDepth = SampleSceneDepth(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float3 wpos = ReconstructViewPos(uv, linearDepth);
    float3 vDir = normalize(wpos);

    half nDotV = max(dot(normal, vDir), 0.0);
    half3 EnergyCompensation;
    half4 PreintegratedGF = half4(PreintegratedDGF_LUT(TEXTURE2D_ARGS(_PreintegratedGF_LUT, sampler_PreintegratedGF_LUT), EnergyCompensation, specular.rgb, roughness, nDotV).rgb, 1);

    half4 SceneColor = GetSource(uv);
    half4 CubemapColor = SampleSceneReflection(uv);
    SceneColor.rgb = max(1e-5, SceneColor.rgb - CubemapColor.rgb);

    half4 SSRColor = SAMPLE_TEXTURE2D_LOD(_SSSRTemporalAATexture, sampler_SSSRTemporalAATexture, uv, 0.0);
    half SSRMask = SSRColor.a * SampleSceneSSRIntensity(uv);

    half4 ReflectionColor = CubemapColor * (1 - SSRMask) + SSRColor * PreintegratedGF * SSRMask;

    return SceneColor + ReflectionColor;
}

#endif
