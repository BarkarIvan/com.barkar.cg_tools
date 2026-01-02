#ifndef GLTF_LIGHTING_INCLUDED
#define GLTF_LIGHTING_INCLUDED

float3 Gltf_GlossyEnvironmentReflection(float3 reflectVector, float3 positionWS, float perceptualRoughness, float occlusion, float2 normalizedScreenSpaceUV)
{
#if USE_CLUSTER_LIGHT_LOOP
    return GlossyEnvironmentReflection(reflectVector, positionWS, perceptualRoughness, occlusion, normalizedScreenSpaceUV);
#else
    return GlossyEnvironmentReflection(reflectVector, positionWS, perceptualRoughness, occlusion);
#endif
}

#endif
