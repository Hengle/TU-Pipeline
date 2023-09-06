#ifndef _SSAO_PASS_INCLUDED
#define _SSAO_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

float _HierarchicalZBufferTextureFromMipLevel;
float _HierarchicalZBufferTextureToMipLevel;
float4 _SourceSize;

half4 GetSource(half2 uv, float2 offset = 0.0, float mipLevel = 0.0) {
    offset *= _SourceSize.zw;
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearRepeat, uv + offset, mipLevel);
}

half4 SSAOPassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;

    half4 minDepth = half4(
        GetSource(uv, float2(-1, -1), _HierarchicalZBufferTextureFromMipLevel).r,
        GetSource(uv, float2(-1, 1), _HierarchicalZBufferTextureFromMipLevel).r,
        GetSource(uv, float2(1, -1), _HierarchicalZBufferTextureFromMipLevel).r,
        GetSource(uv, float2(1, 1), _HierarchicalZBufferTextureFromMipLevel).r
    );

    return max(max(minDepth.r, minDepth.g), max(minDepth.b, minDepth.a));
}


#endif
