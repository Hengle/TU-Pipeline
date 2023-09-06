#ifndef _SCREENSPACELIBRARY_INCLUDED
#define _SCREENSPACELIBRARY_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

TEXTURE2D(_HierarchicalZBufferTexture);
SAMPLER(sampler_HierarchicalZBufferTexture);

float _MaxHierarchicalZBufferTextureMipLevel;

float4 _ProjectionParams2;
float4 _CameraViewTopLeftCorner;
float4 _CameraViewXExtent;
float4 _CameraViewYExtent;

// jitter dither map
static half dither[16] = {
    0.0, 0.5, 0.125, 0.625,
    0.75, 0.25, 0.875, 0.375,
    0.187, 0.687, 0.0625, 0.562,
    0.937, 0.437, 0.812, 0.312
};

//
// Utilities
//

void swap(inout float v0, inout float v1) {
    float temp = v0;
    v0 = v1;
    v1 = temp;
}

inline float GetScreenFadeBord(float2 pos, float value) {
    float borderDist = min(1 - max(pos.x, pos.y), min(pos.x, pos.y));
    return saturate(borderDist > value ? 1 : borderDist / value);
}

//
// Position Reconstruction
//

// 还原世界空间下，相对于相机的位置
half3 ReconstructViewPos(float2 uv, float linearEyeDepth) {
    // Screen is y-inverted
    uv.y = 1.0 - uv.y;

    float zScale = linearEyeDepth * _ProjectionParams2.x; // divide by near plane
    float3 viewPos = _CameraViewTopLeftCorner.xyz + _CameraViewXExtent.xyz * uv.x + _CameraViewYExtent.xyz * uv.y;
    viewPos *= zScale;

    return viewPos;
}

// 从世界空间坐标转片元uv和深度
float4 TransformViewToHScreen(float3 vpos, float2 screenSize) {
    float4 cpos = mul(UNITY_MATRIX_P, vpos);
    cpos.xy = float2(cpos.x, cpos.y * _ProjectionParams.x) * 0.5 + 0.5 * cpos.w;
    cpos.xy *= screenSize;
    return cpos;
}

//
// RayTracing
//

float4 LinearScreenSpaceRayMarching(inout float2 P, inout float K, float2 dp, float dk, float rayZ, bool permute, float4 sourceSize, float stepCount, out float depthDistance) {
    float rayZMin = rayZ;
    float rayZMax = rayZ;
    float preZ = rayZ;

    float2 hitUV = 0.0;

    // 进行屏幕空间射线步近
    UNITY_LOOP
    for (int i = 0; i < stepCount; i++) {
        // 步近
        P += dp;
        K += dk;

        // 得到步近前后两点的深度
        rayZMin = preZ;
        rayZMax = -1.0 / (dk * 0.5 + K);
        preZ = rayZMax;
        if (rayZMin > rayZMax)
            swap(rayZMin, rayZMax);

        // 得到交点uv
        hitUV = permute ? P.yx : P;
        hitUV *= sourceSize.zw;

        if (any(hitUV < 0.0) || any(hitUV > 1.0))
            return float4(hitUV, rayZMin, 0.0);

        float surfaceDepth = -LinearEyeDepth(SampleSceneDepth(hitUV), _ZBufferParams);


        bool isBehind = (rayZMin + 0.1 <= surfaceDepth); // 加一个bias 防止stride过小，自反射

        depthDistance = abs(surfaceDepth - rayZMax);

        if (isBehind) {
            return float4(hitUV, rayZMin, 1.0);
        }
    }

    return float4(hitUV, rayZMin, 0.0);
}

float4 BinarySearchRayMarching(float3 startView, float3 rDir, float4 sourceSize, float binaryCount, float maxDistance, float stride, float stepCount, float thickness) {
    float magnitude = maxDistance;

    float end = startView.z + rDir.z * magnitude;
    if (end > -_ProjectionParams.y)
        magnitude = (-_ProjectionParams.y - startView.z) / rDir.z;
    float3 endView = startView + rDir * magnitude;

    // 齐次屏幕空间坐标
    float4 startHScreen = TransformViewToHScreen(startView, sourceSize.xy);
    float4 endHScreen = TransformViewToHScreen(endView, sourceSize.xy);

    // inverse w
    float startK = 1.0 / startHScreen.w;
    float endK = 1.0 / endHScreen.w;

    //  结束屏幕空间坐标
    float2 startScreen = startHScreen.xy * startK;
    float2 endScreen = endHScreen.xy * endK;

    float depthDistance = 0.0;

    bool permute = false;

    // 根据斜率将dx=1 dy = delta
    float2 diff = endScreen - startScreen;
    if (abs(diff.x) < abs(diff.y)) {
        permute = true;

        diff = diff.yx;
        startScreen = startScreen.yx;
        endScreen = endScreen.yx;
    }

    // 计算屏幕坐标、齐次视坐标、inverse-w的线性增量
    float dir = sign(diff.x);
    float invdx = dir / diff.x;
    float2 dp = float2(dir, invdx * diff.y);
    float dk = (endK - startK) * invdx;

    dp *= stride;
    dk *= stride;

    // 缓存当前深度和位置
    float rayZ = startView.z;

    float2 P = startScreen;
    float K = startK;

    #ifdef _JITTER_ON
    float2 ditherUV = fmod(P, 4) * 0.5;
    float jitter = dither[ditherUV.x * 4 + ditherUV.y];

    P += dp * jitter;
    K += dk * jitter;
    #endif

    float4 hitData = 0.0;

    UNITY_LOOP
    for (int i = 0; i < binaryCount; i++) {
        hitData = LinearScreenSpaceRayMarching(P, K, dp, dk, rayZ, permute, sourceSize, stepCount, depthDistance);
        if (hitData.w == 1.0) {
            if (depthDistance < thickness)
                return hitData;
            P -= dp;
            K -= dk;
            rayZ = -1.0 / K;

            dp *= 0.5;
            dk *= 0.5;
        }
        else {
            return hitData;
        }
    }

    return float4(hitData.xyz, 0.0);
}

float4 HierarchicalZScreenSpaceRayMarching(float3 startView, float3 rDir, float4 sourceSize, float maxDistance, float stride, float stepCount, float thickness) {
    float magnitude = maxDistance;

    float end = startView.z + rDir.z * magnitude;
    if (end > -_ProjectionParams.y)
        magnitude = (-_ProjectionParams.y - startView.z) / rDir.z;
    float3 endView = startView + rDir * magnitude;

    // 齐次屏幕空间坐标
    float4 startHScreen = TransformViewToHScreen(startView, sourceSize.xy);
    float4 endHScreen = TransformViewToHScreen(endView, sourceSize.xy);

    // inverse w
    float startK = 1.0 / startHScreen.w;
    float endK = 1.0 / endHScreen.w;

    //  结束屏幕空间坐标
    float2 startScreen = startHScreen.xy * startK;
    float2 endScreen = endHScreen.xy * endK;

    float depthDistance = 0.0;

    bool permute = false;

    // 根据斜率将dx=1 dy = delta
    float2 diff = endScreen - startScreen;
    if (abs(diff.x) < abs(diff.y)) {
        permute = true;

        diff = diff.yx;
        startScreen = startScreen.yx;
        endScreen = endScreen.yx;
    }

    // 计算屏幕坐标、齐次视坐标、inverse-w的线性增量
    float dir = sign(diff.x);
    float invdx = dir / diff.x;
    float2 dp = float2(dir, invdx * diff.y);
    float dk = (endK - startK) * invdx;

    dp *= stride;
    dk *= stride;

    // 缓存当前深度和位置
    float rayZ = startView.z;

    float2 P = startScreen;
    float K = startK;

    #ifdef _JITTER_ON
    float2 ditherUV = fmod(P, 4) * 0.5;
    float jitter = dither[ditherUV.x * 4 + ditherUV.y];

    P += dp * jitter;
    K += dk * jitter;
    #endif

    float rayZMin = rayZ;
    float rayZMax = rayZ;
    float preZ = rayZ;

    float mipLevel = 0.0;

    float2 hitUV = 0.0;

    // 进行屏幕空间射线步近
    UNITY_LOOP
    for (int i = 0; i < stepCount; i++) {
        // 步近
        P += dp * exp2(mipLevel);
        K += dk * exp2(mipLevel);

        // 得到步近前后两点的深度
        rayZMin = preZ;
        // 1/Zview在屏幕空间是线性的，直接累加K=1/w=-1/Z，得到Z=-1/K
        rayZMax = -1.0 / (dk * exp2(mipLevel) * 0.5 + K);
        preZ = rayZMax;
        if (rayZMin > rayZMax)
            swap(rayZMin, rayZMax);

        // 得到交点uv
        hitUV = permute ? P.yx : P;
        hitUV *= sourceSize.zw;

        if (any(hitUV < 0.0) || any(hitUV > 1.0))
            return false;

        float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_HierarchicalZBufferTexture, sampler_HierarchicalZBufferTexture, hitUV, mipLevel);
        float surfaceDepth = -LinearEyeDepth(rawDepth, _ZBufferParams);

        bool behind = rayZMin + 0.1 <= surfaceDepth;

        if (!behind) {
            mipLevel = min(mipLevel + 1, _MaxHierarchicalZBufferTextureMipLevel);
        }
        else {
            if (mipLevel == 0) {
                if (abs(surfaceDepth - rayZMax) < thickness)
                    return float4(hitUV, rayZMin, 1.0);
            }
            else {
                P -= dp * exp2(mipLevel);
                K -= dk * exp2(mipLevel);
                preZ = -1.0 / K;

                mipLevel--;
            }
        }

    }

    return float4(hitUV, rayZMin, 0.0);
}


#endif
