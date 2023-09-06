#ifndef _SSR_PASS_INCLUDED
#define _SSR_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "../Common/ScreenSpaceLibrary.hlsl"
#include "../Common/CameraGbufferLibrary.hlsl"


TEXTURE2D(_ReflectorTexture);
SAMPLER(sampler_ReflectorTexture);

float4 _SSRParams0;
float4 _SSRParams1;

#define MAXDISTANCE _SSRParams0.x
#define STRIDE _SSRParams0.y
// 遍历次数
#define STEP_COUNT _SSRParams0.z
// 能反射和不可能的反射之间的界限
#define THICKNESS _SSRParams0.w

#define BINARY_COUNT _SSRParams1.x
#define INTENSITY _SSRParams1.y

half4 GetSource(half2 uv) {
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearRepeat, uv, _BlitMipLevel);
}

float4 _SourceSize;


half4 SSRPassFragment(Varyings input) : SV_Target {
    float rawDepth = SampleSceneDepth(input.texcoord);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float3 vpos = ReconstructViewPos(input.texcoord, linearDepth);
    float3 normal = SampleSceneNormals(input.texcoord);
    float3 vDir = normalize(vpos);
    float3 rDir = TransformWorldToViewDir(normalize(reflect(vDir, normal)));

    // 视空间坐标
    vpos = _WorldSpaceCameraPos + vpos;
    float3 startView = TransformWorldToView(vpos);

    float4 hitData = 0.0;

    #ifdef _HIZ_ON
    hitData = HierarchicalZScreenSpaceRayMarching(startView, rDir, _SourceSize, MAXDISTANCE, STRIDE, STEP_COUNT, THICKNESS);
    #else
    hitData = BinarySearchRayMarching(startView, rDir, _SourceSize, BINARY_COUNT, MAXDISTANCE, STRIDE, STEP_COUNT, THICKNESS);
    #endif

    return GetSource(hitData.xy) * hitData.w;
}

float4 _SSRBlurRadius;

struct BlurVaryings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 uv01 : TEXCOORD1;
    float4 uv23 : TEXCOORD2;
    float4 uv45 : TEXCOORD3;
};

BlurVaryings SSRBlurPassVertex(Attributes input) {
    BlurVaryings output;

    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
    output.uv01 = output.uv.xyxy + _SSRBlurRadius.xyxy * float4(1.0, 1.0, -1.0, -1.0) * _SourceSize.zwzw;
    output.uv23 = output.uv.xyxy + _SSRBlurRadius.xyxy * float4(2.0, 2.0, -2.0, -2.0) * _SourceSize.zwzw;
    output.uv45 = output.uv.xyxy + _SSRBlurRadius.xyxy * float4(3.0, 3.0, -3.0, -3.0) * _SourceSize.zwzw;

    return output;
}

half4 SSRBlurPassFragment(BlurVaryings input) : SV_Target {
    half4 color = 0.0;
    color += 0.40 * GetSource(input.uv);
    color += 0.15 * GetSource(input.uv01.xy);
    color += 0.15 * GetSource(input.uv01.zw);
    color += 0.10 * GetSource(input.uv23.xy);
    color += 0.10 * GetSource(input.uv23.zw);
    color += 0.05 * GetSource(input.uv45.xy);
    color += 0.05 * GetSource(input.uv45.zw);

    return color;
}

TEXTURE2D(_SSRResultTexture);
SAMPLER(sampler_SSRResultTexture);

half4 SSRFinalPassFragment(Varyings input) : SV_Target {
    #ifdef _GBUFFER_ON
    return half4(GetSource(input.texcoord).rgb * INTENSITY * SampleSceneSSRIntensity(input.texcoord) * (1.0 - SampleSceneRoughness(input.texcoord)), 1.0);
    #endif
    return half4(GetSource(input.texcoord).rgb * INTENSITY * SampleSceneSSRIntensity(input.texcoord), 1.0);
}

#endif
