#ifndef  CUSTOM_BRDF_INCLUDED
#define  CUSTOM_BRDF_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

float Pow5(float x)
{
    float x2 = x * x;
    return x2 * x2 * x;
}


half3 RotateDirection(half3 R, half degrees)
{
    float3 reflUVW = R;
    half theta = degrees * PI / 180.0f;
    half costha = cos(theta);
    half sintha = sin(theta);
    reflUVW = half3(reflUVW.x * costha - reflUVW.z * sintha, reflUVW.y, reflUVW.x * sintha + reflUVW.z * costha);
    return reflUVW;
}

//BRDF
//Based on https://github.com/Nuomi-Chobits/Unity-URP-PBR/tree/main

//D
// GGX / Trowbridge-Reitz
// [Walter et al. 2007, "Microfacet models for refraction through rough surfaces"]
float D_GGX_UE5(float a2, float NoH)
{
    float d = (NoH * a2 - NoH) * NoH + 1; // 2 mad
    return a2 / (PI * d * d); // 4 mul, 1 rcp
}

//Vis
float Vis_Implicit()
{
    return 0.25;
}

// Appoximation of joint Smith term for GGX
// [Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"]
float Vis_SmithJointApprox(float a2, float NoV, float NoL)
{
    float a = sqrt(a2);
    float Vis_SmithV = NoL * (NoV * (1 - a) + a);
    float Vis_SmithL = NoV * (NoL * (1 - a) + a);
    return 0.5 * rcp(Vis_SmithV + Vis_SmithL);
}

float Vis_SmithGGXCorrelated(float a2, float NoV, float NoL)
{
    float tV = max(NoV * NoV * (1.0 - a2) + a2, 0.0);
    float tL = max(NoL * NoL * (1.0 - a2) + a2, 0.0);
    float GGXV = NoL * sqrt(tV);
    float GGXL = NoV * sqrt(tL);
    return 0.5 * rcp(GGXV + GGXL);
}

//F
float3 F_None(float3 SpecularColor)
{
    return SpecularColor;
}

// [Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"]
float3 F_Schlick_UE5(float3 SpecularColor, float VoH)
{
    float Fc = Pow5(1 - VoH); // 1 sub, 3 mul
    //return Fc + (1 - Fc) * SpecularColor;		// 1 add, 3 mad

    // Anything less than 2% is physically impossible and is instead considered to be shadowing
    return saturate(50.0 * SpecularColor.g) * Fc + (1 - Fc) * SpecularColor;
}

float3 F_Schlick_Another(float3 F0, float VoH)
{
    return F0 + (1 - F0) * Pow5(1 - VoH);
}

float3 F_Schlick_2(float3 F0, float cosTheta)
{
    return F0 + (1.0 - F0) * Pow5(1.0 - cosTheta);
}

float3 Diffuse_Lambert(float3 DiffuseColor)
{
    return DiffuseColor * (1 / PI);
}

float3 Diffuse_Burley(float3 albedo, float pr, float NoV, float NoL, float VoH)
{
    // pr = perceptual roughness (0..1)
    float FD90 = 0.5 + 2.0 * pr * VoH * VoH;
    float FdV  = 1.0 + (FD90 - 1.0) * Pow5(1.0 - NoV);
    float FdL  = 1.0 + (FD90 - 1.0) * Pow5(1.0 - NoL);
    return albedo * (FdV * FdL) * (1.0 / PI);
}

half3 EnvBRDFApprox(half3 SpecularColor, half Roughness, half NoV)
{
    // [ Lazarov 2013, "Getting More Physical in Call of Duty: Black Ops II" ]
    // Adaptation to fit our G term.
    const half4 c0 = {-1, -0.0275, -0.572, 0.022};
    const half4 c1 = {1, 0.0425, 1.04, -0.04};
    half4 r = Roughness * c0 + c1;
    half a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    half2 AB = half2(-1.04, 1.04) * a004 + r.zw;

    // Anything less than 2% is physically impossible and is instead considered to be shadowing
    // Note: this is needed for the 'specular' show flag to work, since it uses a SpecularColor of 0
    AB.y *= saturate(50.0 * SpecularColor.g);

    return SpecularColor * AB.x + AB.y;
}

float3 SpecularGGX(float a2, float3 specular, float NoH, float NoV, float NoL, float VoH)
{
    float D = D_GGX_UE5(a2, NoH);
    float Vis = Vis_SmithGGXCorrelated(a2, NoV, NoL);
    float3 F = F_Schlick_Another(specular, VoH);
    return (D * Vis) * F;
}

//DEPRECATED
half3 StandardBRDF(CustomLitData customLitData, CustomSurfaceData customSurfaceData, half3 L, half3 lightColor,
                   float shadow)
{
    float pr = saturate(customSurfaceData.roughness);
    pr = max(pr, 0.02);       // perceptual roughness
    float alpha = pr * pr;    // linear roughness (α)
    float a2 = alpha * alpha; // α²

    half3 H = normalize(customLitData.V + L);
    half NoH = saturate(dot(customLitData.N, H));
    half NoV = saturate(abs(dot(customLitData.N, customLitData.V)) + 1e-5);
    half NoL = saturate(dot(customLitData.N, L));
    half VoH = saturate(dot(customLitData.V, H)); 
    float3 radiance = NoL * lightColor * shadow * PI;
    float3 diffuseTerm = Diffuse_Lambert(customSurfaceData.albedo);
    float3 specularTerm = SpecularGGX(a2, customSurfaceData.specular, NoH, NoV, NoL, VoH);
    return (diffuseTerm + specularTerm) * radiance;
}

half3 EnvBRDF(CustomLitData customLitData, CustomSurfaceData customSurfaceData, float envRotation, float3 positionWS,
              half3 indirectDiffuse)
{
    
    half3 R = reflect(-customLitData.V, customLitData.N);
    float NoV = saturate(dot(customLitData.N, customLitData.V));
    float3 Fd = F_Schlick_Another(customSurfaceData.specular, NoV);
    float3 kD = (1.0 - Fd);

    float3 diffuseAO = GTAOMultiBounce(customSurfaceData.occlusion, customSurfaceData.albedo);
    float3 indirectDiffuseTerm = indirectDiffuse * customSurfaceData.albedo * kD * diffuseAO;
    //IBL
    //The Split Sum: 1nd Stage
    half3 specularLD = GlossyEnvironmentReflection(R, positionWS, customSurfaceData.roughness,
                                                   1.0);
    //The Split Sum: 2nd Stage
    half3 specularDFG = EnvBRDFApprox(customSurfaceData.specular, customSurfaceData.roughness, NoV);
    //AO
    float specularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(NoV, customSurfaceData.occlusion,
                                                                       customSurfaceData.roughness);
    float3 specularAO = GTAOMultiBounce(specularOcclusion, customSurfaceData.specular);
    float3 indirectSpecularTerm = specularLD * specularDFG * specularAO;
    return indirectDiffuseTerm + indirectSpecularTerm;
}


half3 StandardBRDF_New(CustomLitData ld, CustomSurfaceData sd, half3 L, half3 lightColor, float atten)
{
    float pr    = saturate(sd.roughness);
    float alpha = max(pr * pr, 1e-4);
    float a2    = alpha * alpha;

    float NoL = saturate(dot(ld.N, L));
    float NoV = saturate(dot(ld.N, ld.V));
    if (NoL <= 0 || NoV <= 0) return 0;

    float3 H  = normalize(ld.V + L);
    float NoH = saturate(dot(ld.N, H));
    float VoH = saturate(dot(ld.V, H));

    float3 radiance = lightColor * atten * NoL;

    float  D   = D_GGX_UE5(a2, NoH);
    float  Vis = Vis_SmithGGXCorrelated(a2, NoV, NoL);

    // Fresnel для спекуляра (F0 = sd.specular)
    float3 F   = F_Schlick_Another(sd.specular, VoH);
    float3 spec = (D * Vis) * F;

    // Diffuse (sd.albedo у тебя уже “diffuseColor”, т.к. ты гасишь её metallic-ом выше)
    float3 diff = Diffuse_Burley(sd.albedo, pr, NoV, NoL, VoH);

    // Energy split: лучше по NoV
    float3 Fd = F_Schlick_Another(sd.specular, NoV);
    float3 kD = (1.0 - Fd); // !!! без (1 - metallic), т.к. он уже в sd.albedo

    return (diff * kD + spec) * radiance;
}
#endif