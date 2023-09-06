#ifndef TEMPORALAALIBRARY_INCLUDED
#define TEMPORALAALIBRARY_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float4x4 _ViewProjMatrixWithoutJitter;
float4x4 _LastViewProjMatrix;

float _FrameInfluence;

TEXTURE2D(_MotionVectorTexture);
SAMPLER(sampler_MotionVectorTexture);

static const float2 kOffssets3x3[9] = {{-1, -1}, {-1, 0}, {-1, 1}, {0, -1}, {0, 0}, {0, 1}, {1, -1}, {1, 0}, {1, 1}};

half4 GetAccumulation(half2 uv, TEXTURE2D_PARAM(accum, sampler_accum)) {
    return SAMPLE_TEXTURE2D_LOD(accum, sampler_accum, uv, 0.0);
}

// 取得在YCoCg色彩空间下，Clip的范围
void AdjustColorBox(float2 uv,TEXTURE2D_PARAM(sourceTexture, sampler_sourceTexture), inout half4 boxMin, inout half4 boxMax, float2 invSourceSize) {
    boxMin = 1.0;
    boxMax = 0.0;

    UNITY_UNROLL
    for (int k = 0; k < 9; k++) {
        float4 source = SAMPLE_TEXTURE2D_X_LOD(sourceTexture, sampler_sourceTexture, uv + kOffssets3x3[k] * invSourceSize, 0.0);
        float4 C = float4(RGBToYCoCg(source.rgb), source.a);
        boxMin = min(boxMin, C);
        boxMax = max(boxMax, C);
    }
}


// 取得采样点附近距离相机最近的点偏移
float2 AdjustBestDepthOffset(float2 uv, float2 invSourceSize) {
    half bestDepth = 1.0f;
    float2 uvOffset = 0.0f;

    UNITY_UNROLL
    for (int k = 0; k < 9; k++) {
        half depth = SampleSceneDepth(uv + kOffssets3x3[k] * invSourceSize);
        #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
        #endif

        if (depth < bestDepth) {
            bestDepth = depth;
            uvOffset = kOffssets3x3[k] * invSourceSize;
        }
    }
    return uvOffset;
}


// 将accumulationTexture进行clip，进一步减少ghosting
// https://zhuanlan.zhihu.com/p/425233743
float4 ClipToAABBCenter(half4 accum, half4 boxMin, half4 boxMax) {
    accum.rgb = RGBToYCoCg(accum.rgb);
    float4 filtered = (boxMin + boxMax) * 0.5f;
    float4 ori = accum;
    float4 dir = filtered - accum;
    dir = abs(dir) < (1.0 / 65536.0) ? (1.0 / 65536.0) : dir;
    float4 invDir = rcp(dir);

    // 获取与box相交的位置
    float4 minIntersect = (boxMin - ori) * invDir;
    float4 maxIntersect = (boxMax - ori) * invDir;
    float4 enterIntersect = min(minIntersect, maxIntersect);
    float clipBlend = max(enterIntersect.x, max(enterIntersect.y, enterIntersect.z));
    clipBlend = saturate(clipBlend);

    // 取得与box的相交点
    float4 intersectionYCoCg = lerp(accum, filtered, clipBlend);
    // 还原到rgb空间，得到最终结果
    return float4(YCoCgToRGB(intersectionYCoCg.rgb), intersectionYCoCg.a);
}

float2 ComputeCameraVelocity(float2 uv) {
    float depth = SampleSceneDepth(uv).x;

    #if !UNITY_REVERSED_Z
    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv).x);
    #endif

    // 还原世界坐标
    float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

    // 还原本帧和上帧没有Jitter的裁剪坐标
    float4 posCS = mul(_ViewProjMatrixWithoutJitter, float4(posWS.xyz, 1.0));
    float4 prevPosCS = mul(_LastViewProjMatrix, float4(posWS.xyz, 1.0));

    // 计算出本帧和上帧没有Jitter的NDC坐标 [-1, 1]
    float2 posNDC = posCS.xy * rcp(posCS.w);
    float2 prevPosNDC = prevPosCS.xy * rcp(prevPosCS.w);

    // 计算NDC位置差
    float2 velocity = posNDC - prevPosNDC;
    #if UNITY_UV_STARTS_AT_TOP
    velocity.y = -velocity.y;
    #endif

    // 将速度从[-1, 1]映射到[0, 1]
    // ((posNDC * 0.5 + 0.5) - (prevPosNDC * 0.5 + 0.5)) = (velocity * 0.5)
    velocity.xy *= 0.5;

    return velocity;
}

float2 SampleMotionVector(float2 uv) {
    return SAMPLE_TEXTURE2D_LOD(_MotionVectorTexture, sampler_MotionVectorTexture, uv, 0.0);
}


#endif
