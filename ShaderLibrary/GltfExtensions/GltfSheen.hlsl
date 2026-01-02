#ifndef GLTF_SHEEN_INCLUDED
#define GLTF_SHEEN_INCLUDED

#include "Packages/com.barkar.cg_tools/ShaderLibrary/GltfExtensions/GltfCore.hlsl"

TEXTURE2D(_GltfCharlieLut);
TEXTURE2D(_GltfSheenELut);

float Gltf_Max3(float3 v)
{
    return max(max(v.x, v.y), v.z);
}

float Gltf_LambdaSheenNumericHelper(float x, float alphaG)
{
    float oneMinusAlphaSq = (1.0 - alphaG) * (1.0 - alphaG);
    float a = lerp(21.5473, 25.3245, oneMinusAlphaSq);
    float b = lerp(3.82987, 3.32435, oneMinusAlphaSq);
    float c = lerp(0.19823, 0.16801, oneMinusAlphaSq);
    float d = lerp(-1.97760, -1.27393, oneMinusAlphaSq);
    float e = lerp(-4.32054, -4.85967, oneMinusAlphaSq);
    return a / (1.0 + b * pow(x, c)) + d * x + e;
}

float Gltf_LambdaSheen(float cosTheta, float alphaG)
{
    if (abs(cosTheta) < 0.5)
    {
        return exp(Gltf_LambdaSheenNumericHelper(cosTheta, alphaG));
    }

    return exp(2.0 * Gltf_LambdaSheenNumericHelper(0.5, alphaG) -
        Gltf_LambdaSheenNumericHelper(1.0 - cosTheta, alphaG));
}

float Gltf_V_Sheen(float NoL, float NoV, float sheenRoughness)
{
    sheenRoughness = max(sheenRoughness, 0.000001);
    float alphaG = sheenRoughness * sheenRoughness;

    return saturate(1.0 / ((1.0 + Gltf_LambdaSheen(NoV, alphaG) + Gltf_LambdaSheen(NoL, alphaG)) *
        (4.0 * NoV * NoL)));
}

float Gltf_D_Charlie(float sheenRoughness, float NoH)
{
    sheenRoughness = max(sheenRoughness, 0.000001);
    float alphaG = sheenRoughness * sheenRoughness;
    float invR = 1.0 / alphaG;
    float cos2h = NoH * NoH;
    float sin2h = 1.0 - cos2h;
    return (2.0 + invR) * pow(sin2h, invR * 0.5) / (2.0 * PI);
}

float3 Gltf_BRDF_SpecularSheen(float3 sheenColor, float sheenRoughness, float NoL, float NoV, float NoH)
{
    float sheenDistribution = Gltf_D_Charlie(sheenRoughness, NoH);
    float sheenVisibility = Gltf_V_Sheen(NoL, NoV, sheenRoughness);
    return sheenColor * sheenDistribution * sheenVisibility;
}

float Gltf_SampleCharlieLUT(float NoV, float sheenRoughness)
{
    float2 uv = saturate(float2(NoV, sheenRoughness));
    return SAMPLE_TEXTURE2D(_GltfCharlieLut, sampler_LinearClamp, uv).b;
}

float Gltf_SampleSheenELUT(float NoV, float sheenRoughness)
{
    float2 uv = saturate(float2(NoV, sheenRoughness));
    return SAMPLE_TEXTURE2D(_GltfSheenELut, sampler_LinearClamp, uv).r;
}

#endif
