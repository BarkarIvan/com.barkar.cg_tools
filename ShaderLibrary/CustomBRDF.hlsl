#ifndef  CUSTOM_BRDF_INCLUDED
#define  CUSTOM_BRDF_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.barkar.cg_tools/ShaderLibrary/GltfExtensions/GltfExtensions.hlsl"

TEXTURE2D(_GltfBrdfLut);

// Overview:
// - glTF PBR BRDF based on Khronos glTF-Sample-Renderer.
// - Direct lighting uses GGX (D), Smith (G), Schlick (F) and Lambert diffuse.
// - IBL uses split-sum LUT + URP reflection probes (GlossyEnvironmentReflection).
// Notes:
// - _GltfBrdfLut is sampled with (NoV, 1 - perceptualRoughness).
// - Based on https://github.com/KhronosGroup/glTF-Sample-Renderer/tree/main/source/Renderer/shaders

// Summary: Lambertian diffuse BRDF.
// Args:
// - DiffuseColor: diffuse reflectance (linear).
// Refs: Lambert, "Photometria" (1760)
// https://archive.org/details/lambertsphotome00lambgoog
float3 Gltf_Diffuse_Lambert(float3 DiffuseColor)
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
// Note: Optional replacement for Gltf_Diffuse_Lambert in GltfDirectBRDF (requires LoH).
float3 Gltf_Diffuse_Burley(float3 albedo, float pr, float NoV, float NoL, float LoH)
{
    // pr = perceptual roughness (0..1)
    float FD90 = 0.5 + 2.0 * pr * LoH * LoH;
    float FdV  = 1.0 + (FD90 - 1.0) * Gltf_Pow5(1.0 - NoV);
    float FdL  = 1.0 + (FD90 - 1.0) * Gltf_Pow5(1.0 - NoL);
    return albedo * (FdV * FdL) * INV_PI;
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

float3 Gltf_GetIBLGGXFresnel(float NoV, float perceptualRoughness, float3 F0, float specularWeight)
{
    float2 f_ab = Gltf_SampleGGXLUT(NoV, perceptualRoughness);
    float3 Fr = max(float3(1.0 - perceptualRoughness, 1.0 - perceptualRoughness, 1.0 - perceptualRoughness), F0) - F0;
    float3 k_S = F0 + Fr * Gltf_Pow5(1.0 - NoV);
    float3 FssEss = specularWeight * (k_S * f_ab.x + f_ab.y);

    float Ems = 1.0 - (f_ab.x + f_ab.y);
    float3 F_avg = specularWeight * (F0 + (1.0 - F0) / 21.0);
    float3 FmsEms = Ems * FssEss * F_avg / max(1.0 - F_avg * Ems, 1e-5);

    return FssEss + FmsEms;
}

// Summary: glTF IBL using split-sum LUT and URP reflection probes.
// Args:
// - ld: lighting data (expects N, V).
// - sd: surface data (albedo, metallic, roughness, specular).
// - envRotation: unused (kept for compatibility).
// - positionWS: world position for reflection probe sampling.
// - normalizedScreenSpaceUV: screen UV for Forward+ reflection probes.
// - indirectDiffuse: baked GI or SH diffuse.
half3 GltfIBL(CustomLitData ld, CustomSurfaceData sd, float envRotation, float3 positionWS, float2 normalizedScreenSpaceUV, half3 indirectDiffuse)
{
    float3 N = ld.N;
    float3 V = ld.V;

    float NoV = clamp(abs(dot(N, V)), 0.001, 1.0);
    float pr  = saturate(sd.roughness);

    float3 R = reflect(-V, N);

    // Diffuse IBL (glTF split-sum)
    float3 diffuseColor = sd.albedo * (1.0 - sd.metallic);
    float3 diffuseTerm  = indirectDiffuse * diffuseColor;

    // Specular IBL
#if defined(_MATERIAL_ANISOTROPY)
    float3 specularLD = Gltf_GetIBLRadianceAnisotropy(N, V, pr, sd.anisotropyStrength, sd.anisotropicB, positionWS, normalizedScreenSpaceUV);
#else
    float3 specularLD = Gltf_GlossyEnvironmentReflection(R, positionWS, pr, 1.0, normalizedScreenSpaceUV);
#endif
    float3 fresnelMetal = Gltf_GetIBLGGXFresnel(NoV, pr, sd.albedo, 1.0);
    float3 fresnelDiel  = Gltf_GetIBLGGXFresnel(NoV, pr, sd.specularColor, sd.specularWeight);
    float3 dielectricIBL = lerp(diffuseTerm, specularLD, fresnelDiel);
    float3 metalIBL      = specularLD * fresnelMetal;

#if defined(_MATERIAL_IRIDESCENCE)
    float iridescenceFactor = sd.iridescenceFactor;
    if (sd.iridescenceThickness <= 0.0)
    {
        iridescenceFactor = 0.0;
    }
    if (iridescenceFactor > 0.0)
    {
        float3 iridescenceFresnelDielectric = Gltf_EvalIridescence(1.0, sd.iridescenceIor, NoV, sd.iridescenceThickness, sd.specularColor);
        float3 iridescenceFresnelMetal = Gltf_EvalIridescence(1.0, sd.iridescenceIor, NoV, sd.iridescenceThickness, sd.albedo);
        float3 dielectricIri = Gltf_RgbMix(diffuseTerm, specularLD, iridescenceFresnelDielectric);
        float3 metalIri = specularLD * iridescenceFresnelMetal;
        dielectricIBL = lerp(dielectricIBL, dielectricIri, iridescenceFactor);
        metalIBL = lerp(metalIBL, metalIri, iridescenceFactor);
    }
#endif

    float3 color = lerp(dielectricIBL, metalIBL, sd.metallic);

#if defined(_MATERIAL_SHEEN)
    float maxSheen = Gltf_Max3(sd.sheenColor);
    if (maxSheen > 0.0)
    {
        float albedoSheenScaling = 1.0 - maxSheen * Gltf_SampleSheenELUT(NoV, sd.sheenRoughness);
        float sheenBrdf = Gltf_SampleCharlieLUT(NoV, sd.sheenRoughness);
        float3 sheenSpecular = Gltf_GlossyEnvironmentReflection(R, positionWS, sd.sheenRoughness, 1.0, normalizedScreenSpaceUV) *
            (sd.sheenColor * sheenBrdf);
        color = sheenSpecular + color * albedoSheenScaling;
    }
#endif

#if defined(_MATERIAL_CLEARCOAT)
    if (sd.clearcoatFactor > 0.0)
    {
        float3 Rc = reflect(-V, sd.clearcoatNormal);
        float ccNoV = clamp(abs(dot(sd.clearcoatNormal, V)), 0.001, 1.0);
        float3 clearcoatF = Gltf_F_Schlick(kDielectricSpec.rgb, float3(1.0, 1.0, 1.0), ccNoV);
        half3 clearcoatSpecular = Gltf_GlossyEnvironmentReflection(Rc, positionWS, sd.clearcoatRoughness, 1.0, normalizedScreenSpaceUV);
        color = lerp(color, clearcoatSpecular, sd.clearcoatFactor * clearcoatF);
    }
#endif

    return color;
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

    float3 diffuseColor = sd.albedo * (1.0 - sd.metallic);
    float3 dielectricF0 = sd.specularColor * sd.specularWeight;
    float3 dielectricF = Gltf_F_Schlick(dielectricF0, float3(sd.specularWeight, sd.specularWeight, sd.specularWeight), VoH);
    float3 metalF = Gltf_F_Schlick(sd.albedo, float3(1.0, 1.0, 1.0), VoH);
    float3 diffuseContrib = Gltf_Diffuse_Burley(diffuseColor, pr, NoV, NoL, LoH);

#if defined(_MATERIAL_ANISOTROPY)
    float3 specContrib = Gltf_BRDF_SpecularGGX_Anisotropy(alphaRoughness, sd.anisotropyStrength, ld.N, ld.V, L, H, sd.anisotropicT, sd.anisotropicB);
#else
    float G = Gltf_G_Smith(NoL, NoV, alphaRoughness);
    float D = Gltf_D_GGX(NoH, alphaRoughness);
    float3 specContrib = G * D / (4.0 * NoL * NoV);
#endif

    float3 dielectricBrdf = lerp(diffuseContrib, specContrib, dielectricF);
    float3 metalBrdf = specContrib * metalF;

#if defined(_MATERIAL_IRIDESCENCE)
    float iridescenceFactor = sd.iridescenceFactor;
    if (sd.iridescenceThickness <= 0.0)
    {
        iridescenceFactor = 0.0;
    }
    if (iridescenceFactor > 0.0)
    {
        float3 iridescenceFresnelDielectric = Gltf_EvalIridescence(1.0, sd.iridescenceIor, NoV, sd.iridescenceThickness, sd.specularColor);
        float3 iridescenceFresnelMetal = Gltf_EvalIridescence(1.0, sd.iridescenceIor, NoV, sd.iridescenceThickness, sd.albedo);
        float3 dielectricIri = Gltf_RgbMix(diffuseContrib, specContrib, iridescenceFresnelDielectric);
        float3 metalIri = specContrib * iridescenceFresnelMetal;
        dielectricBrdf = lerp(dielectricBrdf, dielectricIri, iridescenceFactor);
        metalBrdf = lerp(metalBrdf, metalIri, iridescenceFactor);
    }
#endif

    float3 color = lerp(dielectricBrdf, metalBrdf, sd.metallic);

#if defined(_MATERIAL_SHEEN)
    float maxSheen = Gltf_Max3(sd.sheenColor);
    if (maxSheen > 0.0)
    {
        float albedoSheenScalingV = 1.0 - maxSheen * Gltf_SampleSheenELUT(NoV, sd.sheenRoughness);
        float albedoSheenScalingL = 1.0 - maxSheen * Gltf_SampleSheenELUT(NoL, sd.sheenRoughness);
        float albedoSheenScaling = min(albedoSheenScalingV, albedoSheenScalingL);
        float3 sheenBrdf = Gltf_BRDF_SpecularSheen(sd.sheenColor, sd.sheenRoughness, NoL, NoV, NoH);
        color = sheenBrdf + color * albedoSheenScaling;
    }
#endif

#if defined(_MATERIAL_CLEARCOAT)
    if (sd.clearcoatFactor > 0.0)
    {
        float3 Hc = SafeNormalize(ld.V + L);
        float ccNoL = clamp(dot(sd.clearcoatNormal, L), 0.001, 1.0);
        float ccNoV = clamp(abs(dot(sd.clearcoatNormal, ld.V)), 0.001, 1.0);
        float ccNoH = saturate(dot(sd.clearcoatNormal, Hc));
        float3 clearcoatBrdf = Gltf_ClearcoatSpecular(sd.clearcoatRoughness, ccNoL, ccNoV, ccNoH);
        float3 clearcoatF = Gltf_F_Schlick(kDielectricSpec.rgb, float3(1.0, 1.0, 1.0), ccNoV);
        color = lerp(color, clearcoatBrdf, sd.clearcoatFactor * clearcoatF);
    }
#endif

    return color * radiance * NoL;
}
#endif
