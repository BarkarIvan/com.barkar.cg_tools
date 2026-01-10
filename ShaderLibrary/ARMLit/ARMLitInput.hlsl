#ifndef ARMLIT_INPUT_INCLUDED
#define ARMLIT_INPUT_INCLUDED

#include "Packages/com.barkar.cg_tools/ShaderLibrary/MeshQuantization.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_AdditionalMap);
SAMPLER(sampler_AdditionalMap);
TEXTURE2D(_SpecularMap);
SAMPLER(sampler_SpecularMap);
TEXTURE2D(_ClearcoatMap);
SAMPLER(sampler_ClearcoatMap);
TEXTURE2D(_ClearcoatNormalMap);
SAMPLER(sampler_ClearcoatNormalMap);
TEXTURE2D(_SheenColorMap);
SAMPLER(sampler_SheenColorMap);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);
#if defined(_GTAO_BENT_NORMALS)
TEXTURE2D(_GTAOBentNormalTexture);
#endif
#if defined(_GTAO_OCCLUSION)
TEXTURE2D(_GTAOOcclusionTexture);
SAMPLER(sampler_GTAOOcclusionTexture);
#endif

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half4 _BaseMap_ST;
    half4 _AdditionalMap_ST;
    half4 _SpecularColor;
    half4 _SheenColor;
    half3 _EmissionColor;
    half _Brightness;
    half _Metallic;
    half _Roughness;
    half _OcclusionStrength;
    half _SpecularFactor;
    half _ClearcoatFactor;
    half _ClearcoatRoughness;
    half _ClearcoatNormalScale;
    half _SheenRoughness;
    half _Cutoff;
    half _NormalMapScale;
    half _ToksvigStrength;
CBUFFER_END

inline float3 ARMLit_GetNormalOS(float3 normalOS, half4 color)
{
    #if defined(_MQ_QUANTIZED)
    return MQ_DecodeNormalFromColor(color);
    #else
    return normalOS;
    #endif
}

inline void ARMLit_GetNormalTangentOS(float3 normalOSIn, float4 tangentOSIn, half4 color,
    out float3 normalOS, out float4 tangentOS)
{
    #if defined(_MQ_QUANTIZED)
    normalOS = MQ_DecodeNormalFromColor(color);
    tangentOS = MQ_DecodeTangentFromColor(color, normalOS);
    #else
    normalOS = normalOSIn;
    tangentOS = tangentOSIn;
    #endif
}

inline void ARMLit_AlphaClip(float2 uv)
{
    #if defined(_USEALPHACLIP)
    half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    half alpha = col.a * _BaseColor.a;
    clip(alpha - _Cutoff);
    #endif
}

#endif
