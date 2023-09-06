#ifndef _SSR_PASS_INCLUDED
#define _SSR_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "../Common/TemporalAALibrary.hlsl"


TEXTURE2D(_TaaAccumulationTexture);
SAMPLER(sampler_TaaAccumulationTexture);

float4 _SourceSize;

half4 GetSource(half2 uv) {
    // 访问最低层的mipmap
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearRepeat, uv, 0);
}

half4 TAAPassFragment(Varyings input) : SV_Target {
    // 计算出上一帧的位置
    float2 depthOffsetUV = AdjustBestDepthOffset(input.texcoord, _SourceSize.zw);
    float2 velocity = 0.0;
    #if defined (_MOTION_CAMERA)
    velocity =  ComputeCameraVelocity(input.texcoord + depthOffsetUV);
    #elif defined(_MOTION_OBJECT)
    velocity = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_MotionVectorTexture, input.texcoord + depthOffsetUV);
    #endif
    float2 historyUV = input.texcoord - velocity;

    // 采样上一帧和这一帧
    float4 accum = GetAccumulation(historyUV, TEXTURE2D_ARGS(_TaaAccumulationTexture, sampler_TaaAccumulationTexture));
    float4 source = GetSource(input.texcoord);

    // 得到这一帧的颜色范围，防止ghosting
    half4 boxMin, boxMax;
    AdjustColorBox(input.texcoord,TEXTURE2D_ARGS(_BlitTexture, sampler_LinearClamp), boxMin, boxMax, _SourceSize.zw);

    // clip current frame color
    accum.rgb = ClipToAABBCenter(accum, boxMin, boxMax);

    // 与上帧相比移动距离越远，就越倾向于使用当前的像素的值
    float frameInfluence = saturate(_FrameInfluence + length(velocity) * 100);

    // lerp
    return accum * (1.0 - frameInfluence) + source * frameInfluence;
}

#endif
