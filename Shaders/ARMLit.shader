Shader "CGTools/ARMLit"
{
    Properties
    {

        _BaseMap ("Albedo", 2D) = "white"{}
        _BaseColor ("Color", Color) = (1,1,1,1)

        _AdditionalMap ("ARM Map", 2D) = "white"{}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0

        _NormalMap ("Normal Map", 2D) = "gray"{}
        [Toggle(_USETOGSVIK)] _UseToksvig ("Use Toksvig", Float) = 0
        _ToksvigStrength ("ToksvigStrength", Range(0,1)) = 0.5
        _NormalMapScale("Normal Map Scale", Range(0,3)) = 1

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Roughness( "Roughness", Range(0,1)) = 0.0

        [HDR] _EmissionColor ("Emission", Color) = (1,1,1,1)
        _EmissionMap ("EmissionMap", 2D) = "black"{}

        _Brightness("Brightness", Range(0,2)) = 1

        [Toggle(_USEALPHACLIP)] _UseAlphaClip ("Use Alpha Clip", Float) = 0
        _Cutoff ("ClipAlha", Range(0,1)) = 0
        _OffsetFactor ("Offset Factor", Range(-1,1)) = 0
        _OffsetUnits ("Offset Units", Range(-1,1)) = 0


        [Space(40)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Int) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _Blend1 ("Blend mode", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _Blend2 ("Blend mode", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }


        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull [_Cull]
            Blend [_Blend1] [_Blend2]
            ZWrite [_ZWrite]
            Offset [_OffsetFactor], [_OffsetUnits]


            HLSLPROGRAM
            #pragma vertex BeresnevStylizedVertex
            #pragma fragment BeresnevStylizedFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _USETOKSVIG
            #pragma shader_feature_local _ADDITIONALMAP
            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_fragment _USEALPHACLIP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTE
            // #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Surface.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/CustomBRDF.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif


            ///TODO lit input
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AdditionalMap);
            SAMPLER(sampler_AdditionalMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BaseMap_ST;
                half4 _AdditionalMap_ST;
                half3 _EmissionColor;
                half _Brightness;
                half _Metallic;
                half _Roughness;
                half _OcclusionStrength;
                half _Cutoff;
                half _NormalMapScale;
                half _ToksvigStrength;
            CBUFFER_END

            ///

            //TODO LIT PASS     
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

            Varyings BeresnevStylizedVertex(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS);
                OUT.positionWS.xyz = positionInputs.positionWS;
                OUT.positionCS = positionInputs.positionCS;

                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.normalWS = normalInputs.normalWS;
                real sign = IN.tangentOS.w * GetOddNegativeScale();
                OUT.tangentWS = half4(normalInputs.tangentWS.xyz, sign);

                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                OUT.screenPos = positionInputs.positionNDC;
                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                    OUTPUT_SH(OUT.normalWS, OUT.SH); //vertex SH

                return OUT;
            }

            half4 BeresnevStylizedFragment(Varyings IN): SV_Target
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
                litData.N = SafeNormalize(IN.normalWS);
                half sgn = IN.tangentWS.w;
                litData.B = sgn * cross(litData.N.xyz, litData.T.xyz);;

                //additional map
                #if defined (_ADDITIONALMAP)
                half4 additionalMaps = SAMPLE_TEXTURE2D(_AdditionalMap, sampler_AdditionalMap, IN.uv);
                half roughnessMask = additionalMaps.g;
                half metallicMask = additionalMaps.b;
                surfaceData.metallic = metallicMask ;
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

                surfaceData.specular = lerp(kDielectricSpec.rgb, surfaceData.albedo, surfaceData.metallic);


                #if defined (_USEALPHACLIP)
                surfaceData.alpha = step(_Cutoff, surfaceData.alpha);
                #endif

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));

                half3 indirectDiffuse = SAMPLE_GI(IN.lightmapUV, IN.SH, litData.N);

                MixRealtimeAndBakedGI(mainLight, litData.N, indirectDiffuse);
                half3 envPbr = GltfIBL(litData, surfaceData, 0, IN.positionWS, indirectDiffuse);
                half3 directPbr = GltfDirectBRDF(litData, surfaceData, mainLight.direction, mainLight.color,
                                                                   mainLight.shadowAttenuation);

                //WIP!
                //TODO Forward + 
                #if defined(_ADDITIONAL_LIGHTS)
                half3 additionalLights = 0;
                int lightCount = GetAdditionalLightsCount();


                LIGHT_LOOP_BEGIN(lightCount)
                    Light addlight = GetAdditionalPerObjectLight(lightIndex, IN.positionWS);
                    directPbr += GltfDirectBRDF(litData, surfaceData, addlight.direction, addlight.color,
      addlight.distanceAttenuation);
                LIGHT_LOOP_END


                #endif
                
                result.rgb = directPbr + envPbr; //saturate only for Metal api?
                #if defined (_ADDITIONALMAP)
                result.rgb = lerp(result.rgb, result.rgb * surfaceData.occlusion, _OcclusionStrength);
                #endif
                //Emission
                half3 emissionColor = _EmissionColor.rgb;
                #if defined(_EMISSION)
                half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb;
                emissionColor *= emissionMap;
                #endif

                result.rgb += emissionColor;

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
            ENDHLSL
        }

        //to shadowcaster hlsl

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode"="ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_fragment _USEALPHACLIP

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

            ///TODO lit input
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AdditionalMap);
            SAMPLER(sampler_AdditionalMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BaseMap_ST;
                half4 _AdditionalMap_ST;
                half3 _EmissionColor;
                half _Brightness;
                half _Metallic;
                half _Roughness;
                half _Cutoff;
                half _NormalMapScale;
            CBUFFER_END

            ///

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                #if defined(_USEALPHACLIP)
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = col.a * _BaseColor.a;

                clip(alpha - _Cutoff);
                #endif

                #ifdef LOD_FADE_CROSSFADE
                LODFadeCrossFade(input.positionCS);
                #endif
                return 0;
            }
            ENDHLSL
        }


        Pass
        {
            Name "Meta"
            Tags
            {
                "LightMode"="Meta"
            }

            // ZWrite On
            //ZTest LEqual
            Cull Off


            HLSLPROGRAM
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ADDITIONALMAP
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local _USEALPHACLIP
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma vertex MetaPassVertex
            #pragma fragment MetaPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Surface.hlsl"


            ///TODO lit input
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AdditionalMap);
            SAMPLER(sampler_AdditionalMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BaseMap_ST;
                half4 _AdditionalMap_ST;
                half3 _EmissionColor;
                half _Brightness;
                half _Metallic;
                half _Roughness;
                half _Cutoff;
                half _NormalMapScale;
            CBUFFER_END

            //  #include "Packages/com.barkar.bsrp/ShaderLibrary/LitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/CustomMetaPass.hlsl"
            ENDHLSL
        }
    }



    CustomEditor "ARMLitShaderEditor"
}
