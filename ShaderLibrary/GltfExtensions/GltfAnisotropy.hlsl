#ifndef GLTF_ANISOTROPY_INCLUDED
#define GLTF_ANISOTROPY_INCLUDED

#include "Packages/com.barkar.cg_tools/ShaderLibrary/GltfExtensions/GltfCore.hlsl"

// Integration hint (ARMLit):
// - Add shader_feature_local _MATERIAL_ANISOTROPY and _ANISOTROPY_MAP.
// - Sample map RG for direction (remap 0..1 to -1..1) and B for strength.
// - Rotate direction, build anisotropic T/B from mesh T/B and N, then use
//   Gltf_BRDF_SpecularGGX_Anisotropy and Gltf_GetIBLRadianceAnisotropy.

float Gltf_D_GGX_Anisotropic(float NoH, float ToH, float BoH, float anisotropy, float at, float ab)
{
    float a2 = at * ab;
    float3 f = float3(ab * ToH, at * BoH, a2 * NoH);
    float w2 = a2 / dot(f, f);
    return a2 * w2 * w2 * INV_PI;
}

float Gltf_V_GGX_Anisotropic(float NoL, float NoV, float BoV, float ToV, float ToL, float BoL, float at, float ab)
{
    float GGXV = NoL * length(float3(at * ToV, ab * BoV, NoV));
    float GGXL = NoV * length(float3(at * ToL, ab * BoL, NoL));
    float v = 0.5 / (GGXV + GGXL);
    return saturate(v);
}

float3 Gltf_BRDF_SpecularGGX_Anisotropy(float alphaRoughness, float anisotropy, float3 N, float3 V, float3 L, float3 H, float3 T, float3 B)
{
    float at = lerp(alphaRoughness, 1.0, anisotropy * anisotropy);
    float ab = clamp(alphaRoughness, 0.001, 1.0);

    float NoL = saturate(dot(N, L));
    float NoH = clamp(dot(N, H), 0.001, 1.0);
    float NoV = dot(N, V);

    float Vterm = Gltf_V_GGX_Anisotropic(NoL, NoV, dot(B, V), dot(T, V), dot(T, L), dot(B, L), at, ab);
    float Dterm = Gltf_D_GGX_Anisotropic(NoH, dot(T, H), dot(B, H), anisotropy, at, ab);

    return float3(Vterm * Dterm, Vterm * Dterm, Vterm * Dterm);
}

float3 Gltf_GetIBLRadianceAnisotropy(float3 N, float3 V, float roughness, float anisotropy, float3 anisotropyDirection, float3 positionWS, float2 normalizedScreenSpaceUV)
{
    float3 anisotropicTangent = cross(anisotropyDirection, V);
    float3 anisotropicNormal = cross(anisotropicTangent, anisotropyDirection);
    float bendFactor = 1.0 - anisotropy * (1.0 - roughness);
    float bendFactorPow4 = bendFactor * bendFactor;
    bendFactorPow4 *= bendFactorPow4;
    float3 bentNormal = SafeNormalize(lerp(anisotropicNormal, N, bendFactorPow4));

    float3 R = reflect(-V, bentNormal);
    return Gltf_GlossyEnvironmentReflection(R, positionWS, roughness, 1.0, normalizedScreenSpaceUV);
}

#endif
