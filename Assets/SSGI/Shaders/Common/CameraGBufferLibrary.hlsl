#ifndef _CAMERAGBUFFER_INCLUDED
#define _CAMERAGBUFFER_INCLUDED

TEXTURE2D(_BaseColorGBuffer);
SAMPLER(sampler_BaseColorGBuffer);

TEXTURE2D(_SpecularGBuffer);
SAMPLER(sampler_SpecularGBuffer);

TEXTURE2D(_ReflectionGBuffer);
SAMPLER(sampler_ReflectionGBuffer);

half4 SampleSceneBaseColor(float2 uv) {
    return SAMPLE_TEXTURE2D_LOD(_BaseColorGBuffer, sampler_BaseColorGBuffer, uv, 0.0);
}

half4 SampleSceneSpecular(float2 uv) {
    return SAMPLE_TEXTURE2D_LOD(_SpecularGBuffer, sampler_SpecularGBuffer, uv, 0.0);
}

half SampleSceneRoughness(float2 uv) {
    half roughness = SampleSceneSpecular(uv).a;
    return clamp(roughness, 0.02, 1.0);
}

half GetSceneRoughness(half4 specular) {
    half roughness = specular.a;
    return clamp(roughness, 0.02, 1.0);
}

half4 SampleSceneReflection(float2 uv) {
    return SAMPLE_TEXTURE2D_LOD(_ReflectionGBuffer, sampler_ReflectionGBuffer, uv, 0.0);
}

half SampleSceneSSRIntensity(float2 uv) {
    return SampleSceneReflection(uv).a;
}

#endif
