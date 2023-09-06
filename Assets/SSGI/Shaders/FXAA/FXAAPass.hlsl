// https://catlikecoding.com/unity/tutorials/advanced-rendering/fxaa/
// https://zhuanlan.zhihu.com/p/431384101

#ifndef _FXAA_PASS_INCLUDED
#define _FXAA_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

#if defined(_LOW_QUALITY)
    #define EDGE_STEP_COUNT 4
    #define EDGE_STEPS 1, 1.5, 2, 4
    #define EDGE_GUESS 12
#else
#define EDGE_STEP_COUNT 10
#define EDGE_STEPS 1, 1.5, 2, 2, 2, 2, 2, 2, 2, 4
#define EDGE_GUESS 8
#endif

#define CONTRASTTHRESHOLD _FXAAParams.x
#define RELATIVETHRESHOLD _FXAAParams.y
#define SUBPIXELBLENDING _FXAAParams.z

static const float edgeSteps[EDGE_STEP_COUNT] = {EDGE_STEPS};

float4 _SourceSize;

float4 _FXAAParams;

half4 GetSource(half2 uv) {
    // 访问最低层的mipmap
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearRepeat, uv, 0);
}

half4 LuminancePrefilterPassFragment(Varyings input) : SV_Target {
    half3 color = GetSource(input.texcoord);
    return half4(saturate(color), Luminance(color));
}

// nw n ne
// w  m e
// sw s se
struct LuminanceData {
    // 8个邻域像素的亮度值
    float m, n, e, s, w;
    float ne, nw, se, sw;
    // 最高，最低亮度值以及对比度
    float highest, lowest, contrast;
};

struct EdgeData {
    bool isHorizontal;
    float pixelStep;
    float oppositeLuminance, gradient;
};

half4 SampleLuminance(float2 uv, float2 offset = 0.0) {
    offset *= _SourceSize.zw;
    return GetSource(uv + offset).a;
}

// 采样邻域亮度，计算对比度
LuminanceData SampleLuminanceNeighborhood(float2 uv) {
    LuminanceData l;

    l.m = SampleLuminance(uv);
    l.n = SampleLuminance(uv, float2(0.0, 1.0));
    l.e = SampleLuminance(uv, float2(1.0, 0.0));
    l.s = SampleLuminance(uv, float2(0.0, -1.0));
    l.w = SampleLuminance(uv, float2(-1.0, 0.0));

    l.ne = SampleLuminance(uv, float2(1.0, 1.0));
    l.nw = SampleLuminance(uv, float2(-1.0, 1.0));
    l.se = SampleLuminance(uv, float2(1.0, -1.0));
    l.sw = SampleLuminance(uv, float2(-1.0, -1.0));

    l.highest = max(max(max(max(l.n, l.e), l.s), l.w), l.m);
    l.lowest = min(min(min(min(l.n, l.e), l.s), l.w), l.m);
    l.contrast = l.highest - l.lowest;

    return l;
}

// 计算3x3混合因子
float ComputePixelBlendFactor(LuminanceData l) {
    // 计算低通滤波器（模糊图像）
    float filter = 2.0 * (l.n + l.e + l.s + l.w);
    filter += l.ne + l.nw + l.se + l.sw;
    filter *= 1.0 / 12.0;
    // 减去样本亮度，使其变为高通滤波器（保留图像边缘）
    filter = abs(filter - l.m);
    // normalize filter
    filter = saturate(filter / l.contrast);
    // 平滑并变慢
    float blendFactor = smoothstep(0, 1, filter);
    return blendFactor * blendFactor * SUBPIXELBLENDING;
}

// 隔线的长度不一定只有3个像素大小，我们可以通过计算当前像素和分隔线另一侧的像素的亮度平均值，
// 作为分隔线的亮度，然后不断地沿着这条线向两端进行采样，
// 当采样得到的亮度和分隔线的亮度有明显差异时，就认为找到了这条线的末端
float ComputeEdgeBlendFactor(LuminanceData l, EdgeData e, float2 uv) {
    float2 uvEdge = uv;
    float2 edgeStep;
    if (e.isHorizontal) {
        uvEdge.y += e.pixelStep * 0.5;
        edgeStep = float2(_SourceSize.z, 0.0);
    }
    else {
        uvEdge.x += e.pixelStep * 0.5;
        edgeStep = float2(0.0, _SourceSize.w);
    }

    float edgeLuminance = (l.m + e.oppositeLuminance) * 0.5;
    float gradientThreshold = e.gradient * 0.25;

    // 找到10像素内的终点
    float2 puv = uvEdge + edgeStep * edgeSteps[0];
    float pLuminanceDelta = SampleLuminance(puv) - edgeLuminance;
    bool pAtEnd = abs(pLuminanceDelta) >= gradientThreshold;

    // 沿正方向步近
    UNITY_UNROLL
    for (int i = 1; i < EDGE_STEP_COUNT && !pAtEnd; i++) {
        puv += edgeStep * edgeSteps[i];
        pLuminanceDelta = SampleLuminance(uv) - edgeLuminance;
        pAtEnd = abs(pLuminanceDelta) >= gradientThreshold;
    }
    // 如果没到终点 则猜测的向前一段
    if (!pAtEnd) {
        puv += edgeStep * EDGE_GUESS;
    }

    // 沿反方向步近
    float2 nuv = uvEdge - edgeStep * edgeStep[0];
    float nLuminanceDelta = SampleLuminance(nuv) - edgeLuminance;
    bool nAtEnd = abs(nLuminanceDelta) >= gradientThreshold;

    UNITY_UNROLL
    for (int i = 1; i < EDGE_STEP_COUNT && !nAtEnd; i++) {
        nuv -= edgeStep * edgeSteps[i];
        nLuminanceDelta = SampleLuminance(nuv) - edgeLuminance;
        nAtEnd = abs(nLuminanceDelta) >= gradientThreshold;
    }
    if (!nAtEnd) {
        nuv -= edgeStep * EDGE_GUESS;
    }

    float pDistance, nDistance;
    if (e.isHorizontal) {
        pDistance = puv.x - uv.x;
        nDistance = uv.x - nuv.x;
    }
    else {
        pDistance = puv.y - uv.y;
        nDistance = uv.y - nuv.y;
    }

    float shortestDistance;
    float deltaSign;
    if (pDistance <= nDistance) {
        shortestDistance = pDistance;
        deltaSign = pLuminanceDelta >= 0;
    }
    else {
        shortestDistance = nDistance;
        deltaSign = nLuminanceDelta >= 0;
    }

    // 边界方向不符合
    if (deltaSign == (l.m - edgeLuminance >= 0))
        return 0;

    return 0.5 - shortestDistance / (pDistance + nDistance);
}

// 跳过那些对比度较低（低于阈值）的片段
bool ShouldSkipPixel(LuminanceData l) {
    float threshold = max(CONTRASTTHRESHOLD, RELATIVETHRESHOLD * l.highest);
    return l.contrast < threshold;
}

EdgeData ComputeEdge(LuminanceData l) {
    EdgeData e;
    // 根据水平和竖直方向的亮度梯度判断边的方向
    float horizontal =
        abs(l.n + l.s - 2 * l.m) * 2 +
        abs(l.ne + l.se - 2 * l.e) +
        abs(l.nw + l.sw - 2 * l.w);
    float vertical =
        abs(l.e + l.w - 2 * l.m) * 2 +
        abs(l.ne + l.nw - 2 * l.n) +
        abs(l.se + l.sw - 2 * l.s);

    // 判断边的方向
    e.isHorizontal = horizontal >= vertical;
    // 判断边的正负点
    float pLuminance = e.isHorizontal ? l.n : l.e;
    float nLuminance = e.isHorizontal ? l.s : l.w;
    // 计算边亮度正负梯度
    float pGradient = abs(pLuminance - l.m);
    float nGradient = abs(nLuminance - l.m);
    // 如果是水平边，则混合垂直方向
    e.pixelStep = e.isHorizontal ? _SourceSize.w : _SourceSize.z;
    // 选择梯度更大的混合方向
    if (pGradient < nGradient) {
        e.pixelStep = -e.pixelStep;
        // 缓存梯度方向的亮度和梯度
        e.oppositeLuminance = nLuminance;
        e.gradient = nGradient;
    }
    else {
        e.oppositeLuminance = pLuminance;
        e.gradient = pGradient;
    }

    return e;
}

half4 FXAAPassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;

    LuminanceData l = SampleLuminanceNeighborhood(uv);
    if (ShouldSkipPixel(l))
        return GetSource(uv);
    float pixelBlend = ComputePixelBlendFactor(l);
    EdgeData e = ComputeEdge(l);

    float edgeBlend = ComputeEdgeBlendFactor(l, e, uv);
    float finalBlend = max(pixelBlend, edgeBlend);

    // 根据边缘方向和混合因子计算偏移
    if (e.isHorizontal) {
        uv.y += e.pixelStep * finalBlend;
    }
    else {
        uv.x += e.pixelStep * finalBlend;
    }

    return GetSource(uv);
}


#endif
