#ifndef _BSDFLIBRARY_INCLUDED
#define _BSDFLIBRARY_INCLUDED


half DGGX(half NoH, half Roughness) {
    Roughness = pow(Roughness, 4);
    half D = (NoH * Roughness - NoH) * NoH + 1;
    return Roughness / (PI * D * D);
}

float Vis_SmithGGXCorrelated(half NoL, half NoV, half Roughness) {
    float a = Roughness * Roughness;
    float LambdaV = NoV * sqrt((-NoL * a + NoL) * NoL + a);
    float LambdaL = NoL * sqrt((-NoV * a + NoV) * NoV + a);
    return (0.5 / (LambdaL + LambdaV)) / PI;
}

half localBRDF(half3 v, half3 l, half3 n, half roughness) {
    half3 h = normalize(l + v);
    half nDotH = max(dot(n, h), 0);
    half nDotL = max(dot(n, l), 0);
    half nDotV = max(dot(n, v), 0);
    half D = DGGX(nDotH, roughness);
    half G = Vis_SmithGGXCorrelated(nDotL, nDotV, roughness);

    return max(0, D * G);
}

// GGX重要性采样，返回采样方向（世界空间）和PDF
float4 ImportanceSampleGGX(float2 E, float3 N, float Roughness) {
    float m = Roughness * Roughness;
    float m2 = m * m;

    float Phi = 2 * PI * E.x;
    float CosTheta = sqrt((1 - E.y) / (1 + (m2 - 1) * E.y));
    float SinTheta = sqrt(1 - CosTheta * CosTheta);

    float3 H = float3(SinTheta * cos(Phi), SinTheta * sin(Phi), CosTheta);

    // 切空间转换到世界空间
    float3 up = abs(N.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, N));
    float3 bitangent = cross(N, tangent);

    float3 sampleVec = tangent * H.x + bitangent * H.y + N * H.z;

    float d = (CosTheta * m2 - CosTheta) * CosTheta + 1;
    float D = m2 / (PI * d * d);

    float PDF = D * CosTheta;

    return float4(sampleVec, PDF);
}

// 半球均匀采样
float2 UniformSampleDiskConcentric(float2 E) {
    float2 p = 2 * E - 1;
    float Radius;
    float Phi;
    if (abs(p.x) > abs(p.y)) {
        Radius = p.x;
        Phi = (PI / 4) * (p.y / p.x);
    }
    else {
        Radius = p.y;
        Phi = (PI / 2) - (PI / 4) * (p.x / p.y);
    }
    return float2(Radius * cos(Phi), Radius * sin(Phi));
}


half4 PreintegratedDGF_LUT(TEXTURE2D_PARAM(lut, sampler_lut), inout half3 EnergyCompensation, half3 SpecularColor, half Roughness, half NoV) {
    half3 Enviorfilter_GFD = SAMPLE_TEXTURE2D_LOD(lut, sampler_lut, half2(Roughness, NoV), 0.0).rgb;
    half3 ReflectionGF = lerp(saturate(50.0 * SpecularColor.g) * Enviorfilter_GFD.ggg, Enviorfilter_GFD.rrr, SpecularColor);

    EnergyCompensation = 1.0;

    return half4(ReflectionGF, Enviorfilter_GFD.b);
}


#endif
