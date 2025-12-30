#ifndef CUSTOM_META_PASS_INCLUDED
#define CUSTOM_META_PASS_INCLUDED

#define kDielectricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"

///#define MetaInput UnityMetaInput
//#define MetaFragment UnityMetaFragment
float4 MetaVertexPosition(float4 positionOS, float2 uv1, float2 uv2, float4 uv1ST, float4 uv2ST)
{
    return UnityMetaVertexPosition(positionOS.xyz, uv1, uv2, uv1ST, uv2ST);
}

struct Attributes
{
    float4 positionOS : POSITION;
    half3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : POSITION;
    float2 testColor : COLOR;
    float2 uv : TEXCOORD0;
};


Varyings MetaPassVertex(Attributes IN)
{
    Varyings OUT;
    OUT.positionCS = UnityMetaVertexPosition(IN.positionOS.xyz, IN.uv1, IN.uv2);
    OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
    OUT.testColor = IN.uv2;
    return OUT;
}

half4 MetaPassFragment(Varyings IN) : SV_TARGET
{
    half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
    albedo *= _BaseColor;
    //albedo *= IN.color;
    albedo *= _Brightness;

    CustomSurfaceData surfaceData;
    surfaceData.metallic = _Metallic;
    surfaceData.roughness = _Roughness;
    surfaceData.albedo = albedo.rgb;
    surfaceData.alpha = albedo.a;
    
    //additional map
    #if defined (_ADDITIONALMAP)
    half4 additionalMaps = SAMPLE_TEXTURE2D(_AdditionalMap, sampler_AdditionalMap, IN.uv);
    half roughnessMask = additionalMaps.g;
    half metallicMask = additionalMaps.b;
    surfaceData.metallic = metallicMask * _Metallic;
    surfaceData.roughness = roughnessMask * _Roughness;
    
    #endif
    
    surfaceData.albedo = albedo.rgb;
    surfaceData.specular = lerp(kDielectricSpec.rgb, surfaceData.albedo, surfaceData.metallic);

    // TODO alpha
    //  #if defined (_USEALPHACLIP)
    // surface.alpha = step(_AlphaClip, surface.alpha);
    //  #endif

    half3 emissionColor = _EmissionColor.rgb;
    #if defined(_EMISSION)
    half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb;
    emissionColor *= emissionMap;
     
    #endif
    UnityMetaInput metaIput;
    metaIput.Albedo = surfaceData.albedo  + surfaceData.specular * surfaceData.roughness * 0.5;
    metaIput.Emission = emissionColor;
    return UnityMetaFragment(metaIput);
}


#endif
