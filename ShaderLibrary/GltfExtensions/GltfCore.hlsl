#ifndef GLTF_CORE_INCLUDED
#define GLTF_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

// Summary: Raise x to the 5th power (x^5).
// Args:
// - x: input scalar.
float Gltf_Pow5(float x)
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

float Gltf_F_Schlick(float f0, float f90, float VoH)
{
    float x = saturate(1.0 - VoH);
    float x2 = x * x;
    float x5 = x * x2 * x2;
    return f0 + (f90 - f0) * x5;
}

float Gltf_F_Schlick(float f0, float VoH)
{
    return Gltf_F_Schlick(f0, 1.0, VoH);
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

#endif
