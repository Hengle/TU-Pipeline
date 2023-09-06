#ifndef _SSPRREFLECTOR_PASS_INCLUDED
#define _SSPRREFLECTOR_PASS_INCLUDED

struct Attributes {
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float4 positionNDC : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
};

CBUFFER_START(UnityPerMaterial)
half4 _MainColor;
float _Smoothness;
float _ReflectIntensity;
float _SkyboxIntensity;
float _Noise;

float4 _SSPRReflectionSize;
CBUFFER_END

TEXTURE2D(_SSPRReflectionTexture);
SAMPLER(sampler_SSPRReflectionTexture);

TEXTURECUBE(_Skybox);
SAMPLER(sampler_Skybox);

Varyings SSPRReflectorPassVertex(Attributes input) {
    Varyings output;

    VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = vertexInputs.positionCS;
    output.positionNDC = vertexInputs.positionNDC;
    output.positionWS = vertexInputs.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);

    return output;
}

half4 SSPRReflectorPassFragment(Varyings input) : SV_Target {
    float2 suv = input.positionNDC.xy / input.positionNDC.w;

    float3 vDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float3 nDirWS = normalize(input.normalWS);
    float3 rDirWS = normalize(reflect(-vDirWS, nDirWS));

    static const float2 poisson[12] = {
        float2(-0.326212f, -0.40581f),
        float2(-0.840144f, -0.07358f),
        float2(-0.695914f, 0.457137f),
        float2(-0.203345f, 0.620716f),
        float2(0.96234f, -0.194983f),
        float2(0.473434f, -0.480026f),
        float2(0.519456f, 0.767022f),
        float2(0.185461f, -0.893124f),
        float2(0.507431f, 0.064425f),
        float2(0.89642f, 0.412458f),
        float2(-0.32194f, -0.932615f),
        float2(-0.791559f, -0.59771f)
    };

    half4 reflect = 0.0;
    UNITY_UNROLL
    for (int k = 0; k < 12; k++) {
        reflect += SAMPLE_TEXTURE2D(_SSPRReflectionTexture, sampler_SSPRReflectionTexture, suv + poisson[k] * _Noise * _SSPRReflectionSize.zw * 10);
    }

    reflect /= 12;

    half3 skybox = SAMPLE_TEXTURECUBE_LOD(_Skybox, sampler_Skybox, rDirWS, _Smoothness);

    half3 finalCol = reflect.rgb * _ReflectIntensity * reflect.w + _MainColor.rgb * _MainColor.a + skybox * _SkyboxIntensity;

    return half4(finalCol, 1.0);
}

#endif
