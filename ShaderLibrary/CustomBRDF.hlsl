#ifndef  CUSTOM_BRDF_INCLUDED
#define  CUSTOM_BRDF_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_GltfBrdfLut);

// Overview:
// - glTF PBR BRDF based on Khronos glTF-Sample-Renderer.
// - Direct lighting uses GGX (D), Smith (G), Schlick (F) and Lambert diffuse.
// - IBL uses split-sum LUT + URP reflection probes (GlossyEnvironmentReflection).
// Notes:
// - _GltfBrdfLut is sampled with (NoV, 1 - perceptualRoughness).
// - Based on https://github.com/KhronosGroup/glTF-Sample-Renderer/tree/main/source/Renderer/shaders

// Summary: Raise x to the 5th power (x^5).
// Args:
// - x: input scalar.
float Pow5(float x)
{
    float x2 = x * x;
    return x2 * x2 * x;
}

// Summary: GGX normal distribution function (NDF).
// Args:
// - NoH: dot(N, H) in [0..1].
// - alphaRoughness: roughness^2 in [0..1].
// Refs: Walter et al. 2007, "Microfacet models for refraction through rough surfaces" (GGX/Trowbridge-Reitz)
// https://www.cs.cornell.edu/~srm/publications/EGSR07-btdf.pdf
float Gltf_D_GGX(float NoH, float alphaRoughness)
{
    float alphaRoughnessSq = alphaRoughness * alphaRoughness;
    float f = (NoH * NoH) * (alphaRoughnessSq - 1.0) + 1.0;
    return alphaRoughnessSq / (PI * f * f);
}

// Summary: Schlick Fresnel approximation.
// Args:
// - f0: reflectance at normal incidence.
// - f90: reflectance at grazing angle.
// - VoH: dot(V, H) in [0..1].
// Refs: Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"
// https://hal.science/inria-00075599/document
float3 Gltf_F_Schlick(float3 f0, float3 f90, float VoH)
{
    float x = saturate(1.0 - VoH);
    float x2 = x * x;
    float x5 = x * x2 * x2;
    return f0 + (f90 - f0) * x5;
}

// Summary: Lambertian diffuse BRDF.
// Args:
// - DiffuseColor: diffuse reflectance (linear).
// Refs: Lambert, "Photometria" (1760)
// https://archive.org/details/lambertsphotome00lambgoog
float3 Diffuse_Lambert(float3 DiffuseColor)
{
    return DiffuseColor * INV_PI;
}

// Summary: Burley diffuse BRDF (Disney 2012).
// Args:
// - albedo: base color (linear).
// - pr: perceptual roughness in [0..1].
// - NoV: dot(N, V) in [0..1].
// - NoL: dot(N, L) in [0..1].
// - LoH: dot(L, H) in [0..1].
// Refs: Burley 2012, "Physically-Based Shading at Disney"
// https://disneyanimation.com/publications/physically-based-shading-at-disney/
// Note: Optional replacement for Diffuse_Lambert in GltfDirectBRDF (requires LoH).
float3 Diffuse_Burley(float3 albedo, float pr, float NoV, float NoL, float LoH)
{
    // pr = perceptual roughness (0..1)
    float FD90 = 0.5 + 2.0 * pr * LoH * LoH;
    float FdV  = 1.0 + (FD90 - 1.0) * Pow5(1.0 - NoV);
    float FdL  = 1.0 + (FD90 - 1.0) * Pow5(1.0 - NoL);
    return albedo * (FdV * FdL) * INV_PI;
}

// Summary: Smith masking-shadowing for GGX.
// Args:
// - NoL: dot(N, L) in [0..1].
// - NoV: dot(N, V) in [0..1].
// - alphaRoughness: roughness^2 in [0..1].
// Refs: Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"
// https://jcgt.org/published/0003/02/03/
float Gltf_G_Smith(float NoL, float NoV, float alphaRoughness)
{
    float r = alphaRoughness;
    float attenuationL = 2.0 * NoL / (NoL + sqrt(r * r + (1.0 - r * r) * (NoL * NoL)));
    float attenuationV = 2.0 * NoV / (NoV + sqrt(r * r + (1.0 - r * r) * (NoV * NoV)));
    return attenuationL * attenuationV;
}

// Summary: Sample BRDF integration LUT (split-sum).
// Args:
// - NoV: dot(N, V) in [0..1].
// - perceptualRoughness: roughness in [0..1].
// Refs: Karis 2013, "Real Shading in Unreal Engine 4" (BRDF integration map)
// https://blog.selfshadow.com/publications/s2013-shading-course/karis/s2013_pbs_epic_slides.pdf
float2 Gltf_SampleGGXLUT(float NoV, float perceptualRoughness)
{
    float2 uv = saturate(float2(NoV, 1.0 - perceptualRoughness));
    return SAMPLE_TEXTURE2D(_GltfBrdfLut, sampler_LinearClamp, uv).rg;
}

// Summary: glTF IBL using split-sum LUT and URP reflection probes.
// Args:
// - ld: lighting data (expects N, V).
// - sd: surface data (albedo, metallic, roughness, specular).
// - envRotation: unused (kept for compatibility).
// - positionWS: world position for reflection probe sampling.
// - indirectDiffuse: baked GI or SH diffuse.
half3 GltfIBL(CustomLitData ld, CustomSurfaceData sd, float envRotation, float3 positionWS, half3 indirectDiffuse)
{
    float3 N = ld.N;
    float3 V = ld.V;

    float NoV = clamp(abs(dot(N, V)), 0.001, 1.0);
    float pr  = saturate(sd.roughness);

    float3 R = reflect(-V, N);

    // Diffuse IBL (glTF split-sum)
    float3 diffuseColor = sd.albedo * (1.0 - kDielectricSpec.rgb) * (1.0 - sd.metallic);
    float3 diffuseTerm  = indirectDiffuse * diffuseColor;

    // Specular IBL
    half3  specularLD   = GlossyEnvironmentReflection(R, positionWS, pr, 1.0);
    float2 brdf         = Gltf_SampleGGXLUT(NoV, pr);
    float3 specularTerm = specularLD * (sd.specular * brdf.x + brdf.y);

    return diffuseTerm + specularTerm;
}

// Summary: Direct lighting BRDF for one light (glTF spec).
// Args:
// - ld: lighting data (expects N, V).
// - sd: surface data (albedo, metallic, roughness, specular).
// - L: normalized light direction (toward the surface).
// - lightColor: light color/intensity.
// - atten: attenuation (shadow * distance).
half3 GltfDirectBRDF(CustomLitData ld, CustomSurfaceData sd, half3 L, half3 lightColor, float atten)
{
    float pr = saturate(sd.roughness);
    float alphaRoughness = pr * pr;
    
    float NoL = clamp(dot(ld.N, L), 0.001, 1.0);
    float NoV = clamp(abs(dot(ld.N, ld.V)), 0.001, 1.0); 
    float3 H  = SafeNormalize(ld.V + L);
    float NoH = saturate(dot(ld.N, H));
    float VoH = saturate(dot(ld.V, H));
    float LoH = saturate(dot(H, L));
    float3 radiance = lightColor * atten; 

    float3 diffuseColor = sd.albedo * (1.0 - kDielectricSpec.rgb) * (1.0 - sd.metallic);
    float3 specularColor = sd.specular;

    float reflectance = max(max(specularColor.r, specularColor.g), specularColor.b);
    float reflectance90 = saturate(reflectance * 25.0);
    float3 F = Gltf_F_Schlick(specularColor, reflectance90.xxx, VoH);
    float G = Gltf_G_Smith(NoL, NoV, alphaRoughness);
    float D = Gltf_D_GGX(NoH, alphaRoughness);

    float3 diffuseContrib = (1.0 - F) * Diffuse_Burley(diffuseColor, pr, NoV, NoL, LoH );//Diffuse_Lambert(diffuseColor);
    float3 specContrib = F * G * D / (4.0 * NoL * NoV);
    float3 color = (diffuseContrib + specContrib) * radiance * NoL;

    return color;
}
#endif
