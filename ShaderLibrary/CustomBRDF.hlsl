#ifndef  CUSTOM_BRDF_INCLUDED
#define  CUSTOM_BRDF_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_GltfBrdfLut);
SAMPLER(sampler_GltfBrdfLut);

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
//Based on https://github.com/KhronosGroup/glTF-Sample-Renderer/tree/main/source/Renderer/shaders

//D
// GGX / Trowbridge-Reitz
// [Walter et al. 2007, "Microfacet models for refraction through rough surfaces"]
float Gltf_D_GGX(float NoH, float alphaRoughness)
{
    float alphaRoughnessSq = alphaRoughness * alphaRoughness;
    float f = (NoH * NoH) * (alphaRoughnessSq - 1.0) + 1.0;
    return alphaRoughnessSq / (PI * f * f);
}

//Vis
// Smith Joint GGX, correlated
// [Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"]
float Gltf_V_GGX(float NoL, float NoV, float alphaRoughness)
{
    float alphaRoughnessSq = alphaRoughness * alphaRoughness;
    float GGXV = NoL * sqrt(NoV * NoV * (1.0 - alphaRoughnessSq) + alphaRoughnessSq);
    float GGXL = NoV * sqrt(NoL * NoL * (1.0 - alphaRoughnessSq) + alphaRoughnessSq);
    float GGX = GGXV + GGXL;
    return (GGX > 0.0) ? (0.5 / GGX) : 0.0;
}

//F
float3 Gltf_F_None(float3 SpecularColor)
{
    return SpecularColor;
}

// [Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"]
float3 Gltf_F_Schlick(float3 f0, float3 f90, float VoH)
{
    float x = saturate(1.0 - VoH);
    float x2 = x * x;
    float x5 = x * x2 * x2;
    return f0 + (f90 - f0) * x5;
}

float3 Gltf_F_Schlick(float3 f0, float VoH)
{
    return Gltf_F_Schlick(f0, 1.0, VoH);
}

float3 Diffuse_Lambert(float3 DiffuseColor)
{
    return DiffuseColor * INV_PI;
}

float3 Diffuse_Burley(float3 albedo, float pr, float NoV, float NoL, float LoH)
{
    // pr = perceptual roughness (0..1)
    float FD90 = 0.5 + 2.0 * pr * LoH * LoH;
    float FdV  = 1.0 + (FD90 - 1.0) * Pow5(1.0 - NoV);
    float FdL  = 1.0 + (FD90 - 1.0) * Pow5(1.0 - NoL);
    return albedo * (FdV * FdL) * INV_PI;
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

float3 Gltf_SpecularGGX(float alphaRoughness, float NoH, float NoV, float NoL)
{
    float D = Gltf_D_GGX(NoH, alphaRoughness);
    float Vis = Gltf_V_GGX(NoL, NoV, alphaRoughness);
    return float3(D * Vis, D * Vis, D * Vis);
}

float2 Gltf_SampleGGXLUT(float NoV, float perceptualRoughness)
{
    float2 uv = saturate(float2(NoV, perceptualRoughness));
    return SAMPLE_TEXTURE2D(_GltfBrdfLut, sampler_GltfBrdfLut, uv).rg;
}

float3 Gltf_GetIBLGGXFresnel(float NoV, float perceptualRoughness, float3 F0, float specularWeight)
{
    float2 f_ab = Gltf_SampleGGXLUT(NoV, perceptualRoughness);
    float3 Fr = max(float3(1.0 - perceptualRoughness, 1.0 - perceptualRoughness, 1.0 - perceptualRoughness), F0) - F0;
    float3 k_S = F0 + Fr * Pow5(1.0 - NoV);
    float3 FssEss = specularWeight * (k_S * f_ab.x + f_ab.y);

    float Ems = 1.0 - (f_ab.x + f_ab.y);
    float3 F_avg = specularWeight * (F0 + (1.0 - F0) / 21.0);
    float3 FmsEms = Ems * FssEss * F_avg / max(1.0 - F_avg * Ems, 1e-5);

    return FssEss + FmsEms;
}


float2 EnvBRDFApproxAB_GGX(float perceptualRoughness, float NoV)
{
    // Karis/Lazarov approx (UE4), works well with correlated GGX
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4( 1.0,  0.0425,  1.04, -0.04);

    float4 r = perceptualRoughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    return float2(-1.04, 1.04) * a004 + r.zw;
}

float3 EnvBRDFSpecular_GGX(float3 F0, float perceptualRoughness, float NoV)
{
    float2 AB = EnvBRDFApproxAB_GGX(perceptualRoughness, NoV);
    return F0 * AB.x + AB.y;
}
half3 EnvBRDF(CustomLitData ld, CustomSurfaceData sd, float envRotation, float3 positionWS, half3 indirectDiffuse)
{
    float3 N = ld.N;
    float3 V = ld.V;

    float NoV = saturate(abs(dot(N, V))+ 1e-5);;
    float pr  = saturate(sd.roughness);

    float3 R = reflect(-V, N);

    // Diffuse IBL
    float3 diffuseAO           = GTAOMultiBounce(sd.occlusion, sd.albedo);
    float3 indirectDiffuseTerm = indirectDiffuse * sd.albedo * diffuseAO;

    // Specular IBL
    half3  specularLD   = GlossyEnvironmentReflection(R, positionWS, pr, 1.0);
    float  specOcc      = GetSpecularOcclusionFromAmbientOcclusion(NoV, sd.occlusion, pr);
    float3 specAO       = GTAOMultiBounce(specOcc, sd.specular);
    float3 specularTerm = specularLD * specAO;
    float3 fresnelMetal = Gltf_GetIBLGGXFresnel(NoV, pr, sd.albedo, 1.0);
    float3 fresnelDiel  = Gltf_GetIBLGGXFresnel(NoV, pr, kDielectricSpec.rgb, 1.0);

    float3 dielectricIBL = lerp(indirectDiffuseTerm, specularTerm, fresnelDiel);
    float3 metalIBL      = specularTerm * fresnelMetal;

    return lerp(dielectricIBL, metalIBL, sd.metallic);
}

half3 StandardBRDF_New(CustomLitData ld, CustomSurfaceData sd, half3 L, half3 lightColor, float atten)
{
    float pr = saturate(sd.roughness);
    float alphaRoughness = pr * pr;
    
    float NoL = saturate(dot(ld.N, L));
    float NoV = saturate(abs(dot(ld.N, ld.V)) + 1e-5); /// или нормаль глянуть

    float3 H  = SafeNormalize(ld.V + L);
    float NoH = saturate(dot(ld.N, H));
    float VoH = saturate(dot(ld.V, H));
    float3 radiance = lightColor * atten; 
    float3 specDVis = Gltf_SpecularGGX(alphaRoughness, NoH, NoV, NoL);
    float3 diff = Diffuse_Lambert(sd.albedo);
    float3 dielectricF = Gltf_F_Schlick(kDielectricSpec.rgb, abs(VoH));
    float3 metalF = Gltf_F_Schlick(sd.albedo, abs(VoH));
    float3 dielectricBrdf = lerp(diff, specDVis, dielectricF);
    float3 metalBrdf = metalF * specDVis;
    float3 color = lerp(dielectricBrdf, metalBrdf, sd.metallic);

    return color * radiance * NoL;
}
#endif
