#ifndef GLTF_CLEARCOAT_INCLUDED
#define GLTF_CLEARCOAT_INCLUDED

#include "Packages/com.barkar.cg_tools/ShaderLibrary/GltfExtensions/GltfCore.hlsl"

float3 Gltf_ClearcoatSpecular(float clearcoatRoughness, float NoL, float NoV, float NoH)
{
    float alphaRoughness = clearcoatRoughness * clearcoatRoughness;
    float D = Gltf_D_GGX(NoH, alphaRoughness);
    float G = Gltf_G_Smith(NoL, NoV, alphaRoughness);
    float spec = D * G / (4.0 * NoL * NoV);
    return float3(spec, spec, spec);
}

#endif
