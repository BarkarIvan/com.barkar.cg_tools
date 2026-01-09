#ifndef ARMLIT_FORWARD_PASS_INCLUDED
#define ARMLIT_FORWARD_PASS_INCLUDED

//#if defined(_SCREEN_SPACE_OCCLUSION)
//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ScreenSpaceOcclusion.hlsl"
//#endif

struct Attributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    half4 color : COLOR;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : NORMAL;
    float4 tangentWS : TEXCOORD2;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, SH, 5);
    float4 screenPos : TEXCOORD6;
    half4 color : COLOR;
};

const half kMinPerceptualRoughness = 0.04h;

Varyings ARMLitVertex(Attributes IN)
{
    Varyings OUT;
    VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS);
    OUT.positionWS.xyz = positionInputs.positionWS;
    OUT.positionCS = positionInputs.positionCS;

    float3 normalOS;
    float4 tangentOS;
    ARMLit_GetNormalTangentOS(IN.normalOS, IN.tangentOS, IN.color, normalOS, tangentOS);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(normalOS, tangentOS);
    OUT.normalWS = normalInputs.normalWS;
    real sign = tangentOS.w * GetOddNegativeScale();
    OUT.tangentWS = half4(normalInputs.tangentWS.xyz, sign);

    OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
    OUT.color = IN.color;
    OUT.screenPos = positionInputs.positionNDC;
    OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
    OUTPUT_SH(OUT.normalWS, OUT.SH);

    return OUT;
}

half4 ARMLitFragment(Varyings IN) : SV_Target
{
    half4 result = 1;
    half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
    albedo *= _BaseColor;
    //albedo *= IN.color;

    CustomSurfaceData surfaceData;
    surfaceData.metallic = _Metallic;
    surfaceData.roughness = _Roughness;
    surfaceData.albedo = albedo.rgb * _Brightness;
    surfaceData.alpha = albedo.a;
    surfaceData.occlusion = 1.0;

    CustomLitData litData;
    litData.V = normalize(_WorldSpaceCameraPos - IN.positionWS);
    litData.positionWS = IN.positionWS;
    litData.T = SafeNormalize(IN.tangentWS.xyz);
    float3 geomNormalWS = SafeNormalize(IN.normalWS);
    litData.N = geomNormalWS;
    half sgn = IN.tangentWS.w;
    litData.B = sgn * cross(litData.N.xyz, litData.T.xyz);

    //additional map
    #if defined (_ADDITIONALMAP)
    half4 additionalMaps = SAMPLE_TEXTURE2D(_AdditionalMap, sampler_AdditionalMap, IN.uv);
    half roughnessMask = additionalMaps.g;
    half metallicMask = additionalMaps.b;
    surfaceData.metallic = metallicMask;
    surfaceData.roughness = roughnessMask;
    surfaceData.occlusion = additionalMaps.r;
    #endif

    #if !defined(_ADDITIONALMAP)
    surfaceData.roughness = clamp(surfaceData.roughness, kMinPerceptualRoughness, 1.0);
    surfaceData.metallic = saturate(surfaceData.metallic);
    #endif

    //normal map
    #if defined (_NORMALMAP)
    half4 n = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv);
    half3 normalTS = UnpackNormalScale(n, _NormalMapScale);
    half3x3 tangentToWorld = half3x3(litData.T.xyz, litData.B.xyz, litData.N.xyz);
    litData.N = SafeNormalize(mul(normalTS, tangentToWorld));

    #if defined (_USETOKSVIG)
    float tok = saturate(n.a);
    float m = max(tok, 1e-3);
    float sigma2 = (1.0 - m) / m;
    float r = surfaceData.roughness;
    float a2 = r * r;
    a2 *= a2;
    a2 = saturate(a2 + _ToksvigStrength * sigma2);
    r = pow(a2, 0.25);
    surfaceData.roughness = r;
    #endif

    #endif

    surfaceData.specularWeight = 1.0;
    surfaceData.specularColor = kDielectricSpec.rgb;
    surfaceData.clearcoatFactor = 0.0;
    surfaceData.clearcoatRoughness = 0.0;
    surfaceData.clearcoatNormal = geomNormalWS;
    surfaceData.sheenColor = float3(0.0, 0.0, 0.0);
    surfaceData.sheenRoughness = 0.0;

    #if defined(_MATERIAL_SPECULAR)
    surfaceData.specularWeight = _SpecularFactor;
    surfaceData.specularColor = kDielectricSpec.rgb * _SpecularColor.rgb;
    #if defined(_SPECULAR_MAP)
    half4 specularMap = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, IN.uv);
    surfaceData.specularWeight *= specularMap.a;
    surfaceData.specularColor *= specularMap.rgb;
    #endif
    surfaceData.specularColor = min(surfaceData.specularColor, 1.0);
    #endif

    #if defined(_MATERIAL_CLEARCOAT)
    surfaceData.clearcoatFactor = _ClearcoatFactor;
    surfaceData.clearcoatRoughness = _ClearcoatRoughness;
    #if defined(_CLEARCOAT_MAP)
    half4 clearcoatMap = SAMPLE_TEXTURE2D(_ClearcoatMap, sampler_ClearcoatMap, IN.uv);
    surfaceData.clearcoatFactor *= clearcoatMap.r;
    surfaceData.clearcoatRoughness *= clearcoatMap.g;
    #endif
    #if defined(_CLEARCOAT_NORMALMAP)
    half3x3 clearcoatTBN = half3x3(litData.T.xyz, litData.B.xyz, geomNormalWS);
    half4 clearcoatNormalSample = SAMPLE_TEXTURE2D(_ClearcoatNormalMap, sampler_ClearcoatNormalMap, IN.uv);
    half3 clearcoatNormalTS = UnpackNormalScale(clearcoatNormalSample, _ClearcoatNormalScale);
    surfaceData.clearcoatNormal = SafeNormalize(mul(clearcoatNormalTS, clearcoatTBN));
    #endif
    surfaceData.clearcoatRoughness = saturate(surfaceData.clearcoatRoughness);
    #endif

    #if defined(_MATERIAL_SHEEN)
    surfaceData.sheenColor = _SheenColor.rgb;
    surfaceData.sheenRoughness = _SheenRoughness;
    #if defined(_SHEEN_COLOR_MAP)
    half4 sheenMap = SAMPLE_TEXTURE2D(_SheenColorMap, sampler_SheenColorMap, IN.uv);
    surfaceData.sheenColor *= sheenMap.rgb;
    surfaceData.sheenRoughness *= sheenMap.a;
    #endif
    surfaceData.sheenRoughness = saturate(surfaceData.sheenRoughness);
    #endif

    surfaceData.specular = lerp(surfaceData.specularColor * surfaceData.specularWeight,
                                surfaceData.albedo,
                                surfaceData.metallic);

    #if defined (_USEALPHACLIP)
    surfaceData.alpha = step(_Cutoff, surfaceData.alpha);
    #endif

    Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));

    half3 indirectDiffuse = SAMPLE_GI(IN.lightmapUV, IN.SH, litData.N);

    MixRealtimeAndBakedGI(mainLight, litData.N, indirectDiffuse);
    float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
    half occlusion = 1.0h;
    #if defined(_ADDITIONALMAP)
    occlusion = lerp(1.0h, surfaceData.occlusion, _OcclusionStrength);
    #endif
    #if defined(_SCREEN_SPACE_OCCLUSION)
    half ssao = SampleAmbientOcclusion(normalizedScreenSpaceUV);
    occlusion *= ssao;
    #endif
    CustomLitData iblData = litData;
    #if defined(_GTAO_BENT_NORMALS)
    float3 bentNormalVS = SAMPLE_TEXTURE2D(_GTAOBentNormalTexture, sampler_LinearClamp, normalizedScreenSpaceUV).xyz * 2.0 - 1.0;
    float3 bentNormalWS = SafeNormalize(mul((float3x3)UNITY_MATRIX_I_V, bentNormalVS));
    iblData.N = bentNormalWS;
    #endif
    half3 envPbr = GltfIBL(iblData, surfaceData, 0, IN.positionWS, normalizedScreenSpaceUV, indirectDiffuse);
    envPbr *= occlusion;
    half3 directPbr = GltfDirectBRDF(litData, surfaceData, mainLight.direction, mainLight.color,
                                       mainLight.shadowAttenuation);

    #if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = IN.positionWS;
    inputData.normalWS = litData.N;
    inputData.viewDirectionWS = litData.V;
    inputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;

    #if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        Light additionalLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
        directPbr += GltfDirectBRDF(litData, surfaceData, additionalLight.direction, additionalLight.color,
            additionalLight.distanceAttenuation * additionalLight.shadowAttenuation);
    }
    #endif

    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light additionalLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
        directPbr += GltfDirectBRDF(litData, surfaceData, additionalLight.direction, additionalLight.color,
            additionalLight.distanceAttenuation * additionalLight.shadowAttenuation);
    LIGHT_LOOP_END
    #endif

    result.rgb = directPbr + envPbr;
    //Emission
    half3 emissionColor = _EmissionColor.rgb;
    #if defined(_EMISSION)
    half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb;
    emissionColor *= emissionMap;
    #endif

    result.rgb += emissionColor;
    result.a = surfaceData.alpha;

    //LOD
    #ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(IN.positionCS);
    #endif

    //FOG
    #if (defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2))
    result.rgb = CalculateFog(result, IN.positionWS);
    #endif
    return result;
}

#endif
