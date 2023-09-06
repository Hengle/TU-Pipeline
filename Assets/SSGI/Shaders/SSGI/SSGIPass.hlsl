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

TEXTURE2D(_SSGIBilateralFilteredTexture);
SAMPLER(sampler_SSGIBilateralFilteredTexture);

float4 _SourceSize;

float4 _SSGIParams0;
float4 _SSGIParams1;

float4 _SSGIJitter;

float4 _SSGIBlueNoiseSize;

float2 _SSGIBilateralFilterSize;

#define MAXDISTANCE _SSGIParams0.x
#define STRIDE _SSGIParams0.y
// 遍历次数
#define STEPCOUNT _SSGIParams0.z
// 能反射和不可能的反射之间的界限
#define THICKNESS _SSGIParams0.w

#define INTENSITY _SSGIParams1.x
#define SPP _SSGIParams1.y
#define SCREENFADE _SSGIParams1.w

half4 GetSource(float2 uv) {
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
}

float3x3 GetTangentBasis(float3 TangentZ) {
    float3 UpVector = abs(TangentZ.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 TangentX = normalize(cross(UpVector, TangentZ));
    float3 TangentY = cross(TangentZ, TangentX);
    return float3x3(TangentX, TangentY, TangentZ);
}

//
// Ray Tracing
//

half4 SSGIPassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;

    float rawDepth = SampleSceneDepth(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float3 vpos = ReconstructViewPos(uv, linearDepth);
    // 视空间坐标
    vpos = _WorldSpaceCameraPos + vpos;
    float3 startView = TransformWorldToView(vpos);

    half roughness = SampleSceneRoughness(uv);
    float3 normal = SampleSceneNormals(uv);

    float3x3 tangentBias = GetTangentBasis(normal);

    half outMask = 0.0;
    half3 outColor = 0.0;

    UNITY_LOOP
    for (uint i = 0; i < SPP; i++) {
        half2 hash = SAMPLE_TEXTURE2D_LOD(_BlueNoise, sampler_BlueNoise, (uv + sin(i + _SSGIJitter.xy) * _SourceSize.xy * _SSGIBlueNoiseSize.zw), 0);

        float3 l;
        l.xy = UniformSampleDiskConcentric(hash);
        l.z = sqrt(1 - dot(l.xy, l.xy));
        float3 lDirWS = mul(l, tangentBias); // world space light direction
        float3 lDirVS = TransformWorldToViewDir(lDirWS, true); // view space light direction

        float4 hitData = HierarchicalZScreenSpaceRayMarching(startView, lDirVS, _SourceSize, MAXDISTANCE, STRIDE, STEPCOUNT, THICKNESS);

        float3 sampleColor = GetSource(hitData.xy);
        float3 sampleNormal = SampleSceneNormals(hitData.xy);
        float occlusion = 1 - max(dot(lDirWS, sampleNormal), 0.0);

        sampleColor *= occlusion;
        sampleColor *= rcp(1 + Luminance(sampleColor));

        outColor += sampleColor;
        outMask += hitData.w * GetScreenFadeBord(hitData.xy, SCREENFADE);
    }
    outColor /= SPP;
    outColor *= rcp(1 - Luminance(outColor));
    outMask /= SPP;

    #define _SSGI_MASK

    #ifdef _SSGI_MASK
    return half4(outColor * saturate(outMask * outMask * 2), linearDepth);
    #else
    return half4(outColor, linearDepth);
    #endif
}

//
// Temporal AA
//

TEXTURE2D(_SSGIAccumulationTexture);
SAMPLER(sampler_SSGIAccumulationTexture);

half4 TemporalAAPassFragment(Varyings input) : SV_Target {
    float2 depthOffsetUV = AdjustBestDepthOffset(input.texcoord, _SourceSize.zw);
    float2 velocity = 0.0;
    #if defined (_MOTION_CAMERA)
    velocity =  ComputeCameraVelocity(input.texcoord + depthOffsetUV);
    #elif defined(_MOTION_OBJECT)
    velocity = SampleMotionVector(input.texcoord + depthOffsetUV);
    #endif

    float2 historyUV = input.texcoord - velocity;

    // 采样上一帧和这一帧
    float4 accum = GetAccumulation(historyUV, TEXTURE2D_ARGS(_SSGIAccumulationTexture, sampler_SSGIAccumulationTexture));
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
// Bilateral Filter Sampler
//

void GetAo_Depth(float2 uv, inout float3 AO_RO, inout float AO_Depth) {
    float4 SourceColor = GetSource(uv);
    AO_RO = SourceColor.xyz;
    AO_Depth = SourceColor.w;
}

float CrossBilateralWeight(float BLUR_RADIUS, float r, float Depth, float originDepth) {
    const float BlurSigma = BLUR_RADIUS * 0.5;
    const float BlurFalloff = 1.0 / (2.0 * BlurSigma * BlurSigma);

    float dz = (originDepth - Depth) * _ProjectionParams.z * 0.25;
    return exp2(-r * r * BlurFalloff - dz * dz);
}

void ProcessSample(float4 AO_RO_Depth, float BLUR_RADIUS, float r, float originDepth, inout float3 totalAO_RO, inout float totalWeight) {
    float weight = CrossBilateralWeight(BLUR_RADIUS, r, originDepth, AO_RO_Depth.w);
    totalWeight += weight;
    totalAO_RO += weight * AO_RO_Depth.xyz;
}

void ProcessRadius(float2 uv0, float2 deltaUV, float BLUR_RADIUS, float originDepth, inout float3 totalAO_RO, inout float totalWeight) {
    float r = 1.0;
    float z = 0.0;
    float2 uv = 0.0;
    float3 AO_RO = 0.0;

    UNITY_UNROLL
    for (; r <= BLUR_RADIUS / 2.0; r += 1.0) {
        uv = uv0 + r * deltaUV;
        GetAo_Depth(uv, AO_RO, z);
        ProcessSample(float4(AO_RO, z), BLUR_RADIUS, r, originDepth, totalAO_RO, totalWeight);
    }

    UNITY_UNROLL
    for (; r <= BLUR_RADIUS; r += 2.0) {
        uv = uv0 + (r + 0.5) * deltaUV;
        GetAo_Depth(uv, AO_RO, z);
        ProcessSample(float4(AO_RO, z), BLUR_RADIUS, r, originDepth, totalAO_RO, totalWeight);
    }
}

float4 BilateralBlur(float BLUR_RADIUS, float2 uv0, float2 deltaUV) {
    float totalWeight = 1.0;
    float Depth = 0.0;
    float3 totalAOR = 0.0;
    GetAo_Depth(uv0, totalAOR, Depth);

    ProcessRadius(uv0, -deltaUV, BLUR_RADIUS, Depth, totalAOR, totalWeight);
    ProcessRadius(uv0, deltaUV, BLUR_RADIUS, Depth, totalAOR, totalWeight);

    totalAOR /= totalWeight;
    return float4(totalAOR, Depth);
}

half4 BilateralFilterPassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;
    static const float radius = 12.0;
    return BilateralBlur(radius, uv, _SourceSize.zw * _SSGIBilateralFilterSize);
}

//
// Combine Pass
//

half4 CombinePassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;

    half4 baseColor = SampleSceneBaseColor(uv);
    half4 sceneColor = GetSource(uv);
    half4 reflectionColor = SampleSceneReflection(uv);
    half4 deferredLighting = sceneColor - reflectionColor;
    half4 directIrradiance = deferredLighting / baseColor;
    half4 indirectIrradiance = SAMPLE_TEXTURE2D(_SSGIBilateralFilteredTexture, sampler_SSGIBilateralFilteredTexture, uv) * INTENSITY;

    return indirectIrradiance * baseColor + deferredLighting + reflectionColor;

}

#endif
