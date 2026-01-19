#ifndef ARMLIT_INPUT_INCLUDED
#define ARMLIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
    half _SpecularAAStrength;
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

// DOTS instancing support for material properties. Keep the CBUFFER layout intact for SRP Batcher.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseMap_ST)
    UNITY_DOTS_INSTANCED_PROP(float4, _AdditionalMap_ST)
    UNITY_DOTS_INSTANCED_PROP(float4, _SpecularColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _SheenColor)
    UNITY_DOTS_INSTANCED_PROP(float3, _EmissionColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Brightness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _Roughness)
    UNITY_DOTS_INSTANCED_PROP(float , _SpecularAAStrength)
    UNITY_DOTS_INSTANCED_PROP(float , _OcclusionStrength)
    UNITY_DOTS_INSTANCED_PROP(float , _SpecularFactor)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearcoatFactor)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearcoatRoughness)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearcoatNormalScale)
    UNITY_DOTS_INSTANCED_PROP(float , _SheenRoughness)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _NormalMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _ToksvigStrength)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

static float4 unity_DOTS_Sampled_BaseColor;
static float4 unity_DOTS_Sampled_BaseMap_ST;
static float4 unity_DOTS_Sampled_AdditionalMap_ST;
static float4 unity_DOTS_Sampled_SpecularColor;
static float4 unity_DOTS_Sampled_SheenColor;
static float3 unity_DOTS_Sampled_EmissionColor;
static float  unity_DOTS_Sampled_Brightness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_Roughness;
static float  unity_DOTS_Sampled_SpecularAAStrength;
static float  unity_DOTS_Sampled_OcclusionStrength;
static float  unity_DOTS_Sampled_SpecularFactor;
static float  unity_DOTS_Sampled_ClearcoatFactor;
static float  unity_DOTS_Sampled_ClearcoatRoughness;
static float  unity_DOTS_Sampled_ClearcoatNormalScale;
static float  unity_DOTS_Sampled_SheenRoughness;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_NormalMapScale;
static float  unity_DOTS_Sampled_ToksvigStrength;

void SetupDOTSARMLitMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_BaseMap_ST = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseMap_ST);
    unity_DOTS_Sampled_AdditionalMap_ST = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AdditionalMap_ST);
    unity_DOTS_Sampled_SpecularColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecularColor);
    unity_DOTS_Sampled_SheenColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SheenColor);
    unity_DOTS_Sampled_EmissionColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float3, _EmissionColor);
    unity_DOTS_Sampled_Brightness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Brightness);
    unity_DOTS_Sampled_Metallic = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Metallic);
    unity_DOTS_Sampled_Roughness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Roughness);
    unity_DOTS_Sampled_SpecularAAStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SpecularAAStrength);
    unity_DOTS_Sampled_OcclusionStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _OcclusionStrength);
    unity_DOTS_Sampled_SpecularFactor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SpecularFactor);
    unity_DOTS_Sampled_ClearcoatFactor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ClearcoatFactor);
    unity_DOTS_Sampled_ClearcoatRoughness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ClearcoatRoughness);
    unity_DOTS_Sampled_ClearcoatNormalScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ClearcoatNormalScale);
    unity_DOTS_Sampled_SheenRoughness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SheenRoughness);
    unity_DOTS_Sampled_Cutoff = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Cutoff);
    unity_DOTS_Sampled_NormalMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _NormalMapScale);
    unity_DOTS_Sampled_ToksvigStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ToksvigStrength);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSARMLitMaterialPropertyCaches()

#define _BaseColor unity_DOTS_Sampled_BaseColor
#define _BaseMap_ST unity_DOTS_Sampled_BaseMap_ST
#define _AdditionalMap_ST unity_DOTS_Sampled_AdditionalMap_ST
#define _SpecularColor unity_DOTS_Sampled_SpecularColor
#define _SheenColor unity_DOTS_Sampled_SheenColor
#define _EmissionColor unity_DOTS_Sampled_EmissionColor
#define _Brightness unity_DOTS_Sampled_Brightness
#define _Metallic unity_DOTS_Sampled_Metallic
#define _Roughness unity_DOTS_Sampled_Roughness
#define _SpecularAAStrength unity_DOTS_Sampled_SpecularAAStrength
#define _OcclusionStrength unity_DOTS_Sampled_OcclusionStrength
#define _SpecularFactor unity_DOTS_Sampled_SpecularFactor
#define _ClearcoatFactor unity_DOTS_Sampled_ClearcoatFactor
#define _ClearcoatRoughness unity_DOTS_Sampled_ClearcoatRoughness
#define _ClearcoatNormalScale unity_DOTS_Sampled_ClearcoatNormalScale
#define _SheenRoughness unity_DOTS_Sampled_SheenRoughness
#define _Cutoff unity_DOTS_Sampled_Cutoff
#define _NormalMapScale unity_DOTS_Sampled_NormalMapScale
#define _ToksvigStrength unity_DOTS_Sampled_ToksvigStrength

#endif

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
