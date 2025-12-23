#ifndef  CUSTOM_BRDF_INCLUDED
#define  CUSTOM_BRDF_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

half Pow5 (half x)
{
    return x*x * x*x * x;
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

float3 Diffuse_Lambert(float3 DiffuseColor)
{
    return DiffuseColor * (1 / PI);
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
    float Vis = Vis_SmithJointApprox(a2, NoV, NoL);
    float3 F = F_Schlick_UE5(specular, VoH);
    return (D * Vis) * F;
}


half3 StandardBRDF(CustomLitData customLitData, CustomSurfaceData customSurfaceData, half3 L, half3 lightColor,
                   float shadow)
{
    float a2 = Pow4(customSurfaceData.roughness) + 0.001;

    half3 H = normalize(customLitData.V + L);
    half NoH = saturate(dot(customLitData.N, H));
    half NoV = saturate(abs(dot(customLitData.N, customLitData.V)) + 1e-5);
    half NoL = saturate(dot(customLitData.N, L));
    half VoH = saturate(dot(customLitData.V, H)); //LoH
    float3 radiance = NoL * lightColor * shadow * PI;
    float3 diffuseTerm = Diffuse_Lambert(customSurfaceData.albedo);
    float3 specularTerm = SpecularGGX(a2, customSurfaceData.specular, NoH, NoV, NoL, VoH);
    return (diffuseTerm + specularTerm) * radiance;
}

half3 EnvBRDF(CustomLitData customLitData, CustomSurfaceData customSurfaceData, float envRotation, float3 positionWS,
              half3 indirectDiffuse)
{
    half NoV = saturate(abs(dot(customLitData.N, customLitData.V)) + 1e-5);
    half3 R = reflect(-customLitData.V, customLitData.N);
   // R = RotateDirection(R, envRotation);

    //SH
    float3 diffuseAO = GTAOMultiBounce(customSurfaceData.occlusion, customSurfaceData.albedo);
    float3 indirectDiffuseTerm = indirectDiffuse * customSurfaceData.albedo * diffuseAO;

    //IBL
    //The Split Sum: 1nd Stage
    half3 specularLD = GlossyEnvironmentReflection(R, positionWS, customSurfaceData.roughness,
                                                   customSurfaceData.occlusion);
    //The Split Sum: 2nd Stage
    half3 specularDFG = EnvBRDFApprox(customSurfaceData.specular, customSurfaceData.roughness, NoV);
    //AO
    float specularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(NoV, customSurfaceData.occlusion,
                                                                       customSurfaceData.roughness);
    float3 specularAO = GTAOMultiBounce(specularOcclusion, customSurfaceData.specular);
    float3 indirectSpecularTerm = specularLD * specularDFG * specularAO;
    return indirectDiffuseTerm + indirectSpecularTerm;
}
#endif