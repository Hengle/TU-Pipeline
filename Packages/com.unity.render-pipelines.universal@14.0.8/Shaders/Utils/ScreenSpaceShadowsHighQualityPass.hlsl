#ifndef _SCREENSPACESHADOWSHIGHQUALITY_PASS_INCLUDED
#define _SCREENSPACESHADOWSHIGHQUALITY_PASS_INCLUDED

//Keep compiler quiet about Shadows.hlsl.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
// Core.hlsl for XR dependencies
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// 读取深度的Shadowmap
TEXTURE2D(_MainLightShadowmapTexture2);
SAMPLER(sampler_MainLightShadowmapTexture2);

TEXTURE2D(_ScreenSpaceShadowmapMaskTexture);
SAMPLER(sampler_ScreenSpaceShadowmapMaskTexture);

float _ShadowPenumbraScale;

#define N_SAMPLE 64
static float2 poissonDisk[N_SAMPLE] = {
    float2(-0.5119625f, -0.4827938f),
    float2(-0.2171264f, -0.4768726f),
    float2(-0.7552931f, -0.2426507f),
    float2(-0.7136765f, -0.4496614f),
    float2(-0.5938849f, -0.6895654f),
    float2(-0.3148003f, -0.7047654f),
    float2(-0.42215f, -0.2024607f),
    float2(-0.9466816f, -0.2014508f),
    float2(-0.8409063f, -0.03465778f),
    float2(-0.6517572f, -0.07476326f),
    float2(-0.1041822f, -0.02521214f),
    float2(-0.3042712f, -0.02195431f),
    float2(-0.5082307f, 0.1079806f),
    float2(-0.08429877f, -0.2316298f),
    float2(-0.9879128f, 0.1113683f),
    float2(-0.3859636f, 0.3363545f),
    float2(-0.1925334f, 0.1787288f),
    float2(0.003256182f, 0.138135f),
    float2(-0.8706837f, 0.3010679f),
    float2(-0.6982038f, 0.1904326f),
    float2(0.1975043f, 0.2221317f),
    float2(0.1507788f, 0.4204168f),
    float2(0.3514056f, 0.09865579f),
    float2(0.1558783f, -0.08460935f),
    float2(-0.0684978f, 0.4461993f),
    float2(0.3780522f, 0.3478679f),
    float2(0.3956799f, -0.1469177f),
    float2(0.5838975f, 0.1054943f),
    float2(0.6155105f, 0.3245716f),
    float2(0.3928624f, -0.4417621f),
    float2(0.1749884f, -0.4202175f),
    float2(0.6813727f, -0.2424808f),
    float2(-0.6707711f, 0.4912741f),
    float2(0.0005130528f, -0.8058334f),
    float2(0.02703013f, -0.6010728f),
    float2(-0.1658188f, -0.9695674f),
    float2(0.4060591f, -0.7100726f),
    float2(0.7713396f, -0.4713659f),
    float2(0.573212f, -0.51544f),
    float2(-0.3448896f, -0.9046497f),
    float2(0.1268544f, -0.9874692f),
    float2(0.7418533f, -0.6667366f),
    float2(0.3492522f, 0.5924662f),
    float2(0.5679897f, 0.5343465f),
    float2(0.5663417f, 0.7708698f),
    float2(0.7375497f, 0.6691415f),
    float2(0.2271994f, -0.6163502f),
    float2(0.2312844f, 0.8725659f),
    float2(0.4216993f, 0.9002838f),
    float2(0.4262091f, -0.9013284f),
    float2(0.2001408f, -0.808381f),
    float2(0.149394f, 0.6650763f),
    float2(-0.09640376f, 0.9843736f),
    float2(0.7682328f, -0.07273844f),
    float2(0.04146584f, 0.8313184f),
    float2(0.9705266f, -0.1143304f),
    float2(0.9670017f, 0.1293385f),
    float2(0.9015037f, -0.3306949f),
    float2(-0.5085648f, 0.7534177f),
    float2(0.9055501f, 0.3758393f),
    float2(0.7599946f, 0.1809109f),
    float2(-0.2483695f, 0.7942952f),
    float2(-0.4241052f, 0.5581087f),
    float2(-0.1020106f, 0.6724468f)
};


float2 RotateVec2(float2 v, float angle) {
    float s = sin(angle);
    float c = cos(angle);

    return float2(v.x * c + v.y * s, -v.x * s + v.y * c);
}

// 计算光源到遮挡物的垂直距离，取blocker范围的平均垂直距离
float2 SampleBlockerAvgDepth(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoords, float searchWidth, float bias, float random) {
    float count = 0.00001; // 防止/0
    float blockDepth = 0.0;

    for (int i = 0; i < N_SAMPLE; i++) {
        float2 offset = poissonDisk[i];
        offset = RotateVec2(offset, random);
        float2 uvoffset = shadowCoords.xy + offset * searchWidth * _MainLightShadowmapSize.xy;
        float sampleDepth = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, uvoffset);
        if (sampleDepth > shadowCoords.z + bias) {
            blockDepth += sampleDepth;
            count += 1.0;
        }
    }

    return float2(blockDepth / count, count);
}

float SampleShadowmapPCF(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoords, float filterWidth, float random) {
    // Tuft10 过大PCF核解决方案
    // Can't work ???
    // float ux = ddx(shadowCoords.x), vy = ddy(shadowCoords.y), vx = ddx(shadowCoords.y), uy = ddy(shadowCoords.x);
    // float2x2 bias = float2x2(vy, -uy, -vx, ux) / (ux * vy - vx * uy);

    // Isdoro06 过大PCF核解决方案
    // Can't work + 走样 ???
    // float ux = ddx(shadowCoords.x), vx = ddx(shadowCoords.y), zx = ddx(shadowCoords.z), uy = ddy(shadowCoords.x), vy = ddy(shadowCoords.y), zy = ddy(shadowCoords.z);
    // float down = ux * vy - uy * vx;
    // float2x1 a = float2x1(zx, zy);
    // float2x2 b = float2x2(vy, -uy, -vx, ux);
    // float2x1 biasMat = mul(b, a) / down;

    float shadow = 0.0;
    for (int i = 0; i < N_SAMPLE; i++) {
        float2 offset = poissonDisk[i];
        offset = RotateVec2(offset, random) * filterWidth * _MainLightShadowmapSize.xy; // uv offset
        float2 uvoffset = shadowCoords.xy + offset;
        float sampleDepth = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, uvoffset);
        if (sampleDepth < shadowCoords.z) {
            shadow += 1.0;
        }
    }
    return shadow / N_SAMPLE;
}

float SampleShadowmap(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoords) {
    float sampleDepth = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, shadowCoords.xy);
    return sampleDepth < shadowCoords.z;
}

float Random1DTo1D(float value, float a, float b) {
    //make value more random by making it bigger
    float random = frac(sin(value + b) * a);
    return random;
}

float GetShadow(float3 wpos, uint cascadeIndex, float4 coords) {
    float random = Random1DTo1D(wpos.x + wpos.y, 14375.5964, 0.546);
    // 包围球半径
    float orthHalfWidth = _CascadeShadowSplitSpheresArray[cascadeIndex].w;
    float orthHalfWidth0 = _CascadeShadowSplitSpheresArray[0].w;
    // 搜寻半径：lightSize / orthWidth
    float blockerSearchWidth = 5 * _ShadowPenumbraScale * 0.5 * orthHalfWidth0 / orthHalfWidth;

    // 计算bias
    float3 lightDir = normalize(_MainLightPosition.xyz);
    float tan = lightDir.y / sqrt(1 - lightDir.y * lightDir.y + 0.00001);
    float texelSize = orthHalfWidth * _ScreenSpaceShadowmapTextureSize.z;
    float deltaZ_WS = blockerSearchWidth * texelSize / tan;
    float deltaZ_LS = deltaZ_WS / _CascadeZDistanceArray[cascadeIndex]; // blocker bias

    // TODO：fix penumbra calculation
    float2 blocker = SampleBlockerAvgDepth(TEXTURE2D_ARGS(_MainLightShadowmapTexture2, sampler_MainLightShadowmapTexture2), coords, blockerSearchWidth, deltaZ_LS, random);
    float blockerDepth = blocker.x;
    float blockerCount = blocker.y;

    if (blockerCount < 1) return 1.0; // 没有遮挡则直接返回

    // _CascadeZDistanceArray：每个cascade正交投影的far到near距离，相乘得到世界空间下的深度差值
    float penumbra = abs(coords.z - blockerDepth) * _CascadeZDistanceArray[cascadeIndex] / orthHalfWidth;
    penumbra = min(10 * penumbra, 100.0 / orthHalfWidth);
    penumbra *= (2.0 + _ShadowPenumbraScale);

    // return 0.0;

    return SampleShadowmapPCF(TEXTURE2D_ARGS(_MainLightShadowmapTexture2, sampler_MainLightShadowmapTexture2), coords, penumbra, random);
}

float GetSSShadowMask(float2 uv) {
    half mask = SAMPLE_TEXTURE2D(_ScreenSpaceShadowmapMaskTexture, sampler_ScreenSpaceShadowmapMaskTexture, uv);
    return mask;
}

half4 ScreenSpaceShadowsPassFragment(Varyings input) : SV_Target {
    half mask = GetSSShadowMask(input.texcoord);
    if (mask < 0.1) return 0.0;
    if (mask > 0.9) return 1.0;

    #if UNITY_REVERSED_Z
    float deviceDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, input.texcoord.xy).r;
    #else
    float deviceDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, input.texcoord.xy).r;
    deviceDepth = deviceDepth * 2.0 - 1.0;
    #endif

    // sky TODO: delete this
    if (deviceDepth == 0.0)
        return 0.0;

    //Fetch shadow coordinates for cascade.
    float3 wpos = ComputeWorldSpacePosition(input.texcoord.xy, deviceDepth, unity_MatrixInvVP);
    float4 coords = TransformWorldToShadowCoord(wpos);
    uint cascadeIndex = coords.w;
    float shadow = cascadeIndex < 4 ? GetShadow(wpos, cascadeIndex, coords) : MainLightRealtimeShadow(coords);

    return shadow;
}

// 生成mask
static const float2 maskOffsets[16] = {float2(-1.5, -1.5), float2(-0.5, -1.5), float2(0.5, 1.5), float2(1.5, 1.5), float2(-1.5, 0.5), float2(-0.5, 0.5), float2(0.5, 0.5), float2(1.5, 0.5), float2(-1.5, -0.5), float2(-0.5, -0.5), float2(0.5, -0.5), float2(1.5, -0.5), float2(-1.5, -1.5), float2(-0.5, -1.5), float2(0.5, -1.5), float2(1.5, -1.5)};

// TODO: 抄https://zhuanlan.zhihu.com/p/588991753
half4 ScreenSpaceShadowmapMaskFragment(Varyings input) : SV_Target {
    float sum = 0.0;

    float2 uv = saturate(input.texcoord);
    #if UNITY_REVERSED_Z
    float deviceDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv).r;
    #else
    float deviceDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv).r;
    deviceDepth = deviceDepth * 2.0 - 1.0;
    #endif

    //Fetch shadow coordinates for cascade.
    float3 wpos = ComputeWorldSpacePosition(uv, deviceDepth, unity_MatrixInvVP);
    float4 coords = TransformWorldToShadowCoord(wpos);
    if (coords.w > 4.0) return SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture, coords.xyz);

    float dis = distance(wpos, _WorldSpaceCameraPos.xyz);
    float radius = 1.0 / (dis * dis * 0.01 + 0.01);

    for (int i = 0; i < 16; i++) {
        float offset = maskOffsets[i] * radius;
        float2 uv2 = uv + offset * _ScreenSize.zw * 4;
        #if UNITY_REVERSED_Z
        float deviceDepth2 = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv2).r;
        #else
        float deviceDepth2 = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv2).r;
        deviceDepth2 = deviceDepth2 * 2.0 - 1.0;
        #endif

        //Fetch shadow coordinates for cascade.
        float3 wpos2 = ComputeWorldSpacePosition(uv2, deviceDepth2, unity_MatrixInvVP);
        float4 coords2 = TransformWorldToShadowCoord(wpos2);

        float shadow = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture, coords2.xyz);

        sum += deviceDepth2 == 0.0 ? 1.0 : shadow;
    }
    return sum / 16.0;
}

float4 _SSShadowmapBlurRadius;

half4 GetSource(float2 uv) {
    return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
}

half4 BlurPassFragment(Varyings input) : SV_Target {
    float2 uv = saturate(input.texcoord);
    #if UNITY_REVERSED_Z
    float deviceDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv).r;
    #else
    float deviceDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv).r;
    deviceDepth = deviceDepth * 2.0 - 1.0;
    #endif

    float3 wpos = ComputeWorldSpacePosition(uv, deviceDepth, unity_MatrixInvVP);

    if (ComputeCascadeIndex(wpos) > 4) return GetSource(uv).r;

    float shadow = 0;
    float weight = 0;

    float toCam = distance(_WorldSpaceCameraPos, wpos);
    float2 blurRadius = _SSShadowmapBlurRadius.xy * _CascadeShadowSplitSpheresArray[0].w / toCam;

    for (int i = -3; i <= 3; i++) {
        float2 offset = float2(i, i) * blurRadius * _ScreenSize.zw;
        float2 uv2 = uv + offset;

        #if UNITY_REVERSED_Z
        float deviceDepth2 = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv2).r;
        #else
        float deviceDepth2 = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv2).r;
        deviceDepth2 = deviceDepth2 * 2.0 - 1.0;
        #endif

        float3 wpos2 = ComputeWorldSpacePosition(uv2, deviceDepth2, unity_MatrixInvVP);

        float w = 1.0 / (1.0 + distance(wpos, wpos2) * 10.0);

        shadow += w * GetSource(uv2).r;
        weight += w;
    }

    shadow /= weight;
    return shadow;
}
#endif
