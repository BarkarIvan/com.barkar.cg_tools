#ifndef  CUSTOM_LIGHTING
#define  CUSTOM_LIGHTING

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

half3 CalculateFog(half4 color, float3 positionWS)
{
    float viewZ = -(mul(UNITY_MATRIX_V, float4(positionWS, 1)).z);
    float nearToFarZ = max(viewZ - _ProjectionParams.y, 0);
    half fogFactor = ComputeFogFactorZ0ToFar(nearToFarZ);
    half intensity = ComputeFogIntensity(fogFactor);
    return lerp(color.rgb, unity_FogColor.rgb, (1.0 - intensity));
}

half3 GetDiffuseLighting(Light light, CustomLitData litData)
{
    half3 attenuatedLightCol = light.color * light.shadowAttenuation;
    half NoL = saturate(dot(litData.N, light.direction));
    half3 lightDiffuse = attenuatedLightCol * NoL;
    return lightDiffuse;
}

half3 GetDiffuseLighting(Light light, half NoL)
{
    half3 attenuatedLightCol = light.color * light.shadowAttenuation;
    half3 lightDiffuse = attenuatedLightCol * NoL;
    return lightDiffuse;
}

half3 GetDiffuseLightingHalfLambert(Light light, CustomLitData litData)
{
    half3 attenuatedLightCol = light.color * light.shadowAttenuation;
    half NoL = saturate(dot(litData.N, light.direction)) * 0.5 + 0.5;
    half3 lightDiffuse = attenuatedLightCol * NoL;
    return lightDiffuse;
}

half3 GetReflectionProbe(CustomSurfaceData surface, CustomLitData litData)
{
    half3 rV = reflect(-litData.V, litData.N);
    half4 probe = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, rV,
                                         (surface.roughness) * UNITY_SPECCUBE_LOD_STEPS);
    half3 envirReflection = DecodeHDREnvironment(probe, unity_SpecCube0_HDR);
    return envirReflection;
}


half LinearStep(half minVal, half maxVal, half In)
{
    return saturate((In - minVal) / (maxVal - minVal));
}



#endif
