Shader "CGTools/BRDF Factorization/Example"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Metallic ("Metallic (IBL)", Range(0,1)) = 0.0
        _Roughness ("Roughness (IBL)", Range(0,1)) = 0.5
        _SpecularWeight ("Specular Weight (IBL)", Range(0,1)) = 1.0
        _SpecularF0 ("Specular F0 (IBL)", Range(0,1)) = 0.04
        [NoScaleOffset] _BrdfFactorP ("BRDF Factor P", 2D) = "white" {}
        [NoScaleOffset] _BrdfFactorQ ("BRDF Factor Q", 2D) = "white" {}
        _BrdfFactorScale ("BRDF Scale (RGB)", Vector) = (1,1,1,0)
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
                "LightMode"="UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex BrdfFactorizationVert
            #pragma fragment BrdfFactorizationFrag

            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTE
            #pragma multi_compile _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _REFLECTION_PROBE_ATLAS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Surface.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/CustomBRDF.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/BrdfFactorization.hlsl"

            TEXTURE2D(_BrdfFactorP);
            TEXTURE2D(_BrdfFactorQ);
            SAMPLER(sampler_BrdfFactorP);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Metallic;
                half _Roughness;
                half _SpecularWeight;
                half _SpecularF0;
                float3 _BrdfFactorScale;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, SH, 2);
            };

            float3 BuildTangent(float3 n)
            {
                float3 up = (abs(n.z) < 0.999) ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                return normalize(cross(up, n));
            }

            Varyings BrdfFactorizationVert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.SH);
                return output;
            }

            half4 BrdfFactorizationFrag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 t = BuildTangent(n);
                float3 b = normalize(cross(n, t));
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));

                Light mainLight = GetMainLight();
                float3 color = 0.0;

                float3 brdfMain = BrdfFactorization_Evaluate(_BrdfFactorP, _BrdfFactorQ, 
                    v, mainLight.direction, t, b, n, _BrdfFactorScale);
                float nlMain = saturate(dot(n, mainLight.direction));
                color += brdfMain * (mainLight.color * nlMain);

                CustomSurfaceData surfaceData = (CustomSurfaceData)0;
                surfaceData.albedo = _BaseColor.rgb;
                surfaceData.metallic = saturate(_Metallic);
                surfaceData.roughness = clamp(_Roughness, 0.04, 1.0);
                surfaceData.alpha = _BaseColor.a;
                surfaceData.occlusion = 1.0;
                surfaceData.specularWeight = saturate(_SpecularWeight);
                surfaceData.specularColor = _SpecularF0.xxx;
                surfaceData.specular = lerp(surfaceData.specularColor * surfaceData.specularWeight,
                    surfaceData.albedo, surfaceData.metallic);

                CustomLitData litData = (CustomLitData)0;
                litData.positionWS = input.positionWS;
                litData.V = v;
                litData.N = n;
                litData.T = t;
                litData.B = b;

                half3 indirectDiffuse = SAMPLE_GI(input.lightmapUV, input.SH, n);
                MixRealtimeAndBakedGI(mainLight, n, indirectDiffuse);
                float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 envPbr = GltfIBL(litData, surfaceData, 0, input.positionWS, normalizedScreenSpaceUV, indirectDiffuse);
                color += envPbr;

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < additionalCount; i++)
                {
                    Light light = GetAdditionalLight(i, input.positionWS);
                    float3 brdf = BrdfFactorization_Evaluate(_BrdfFactorP, _BrdfFactorQ, 
                        v, light.direction, t, b, n, _BrdfFactorScale);
                    float nl = saturate(dot(n, light.direction));
                    float3 lightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                    color += brdf * (lightColor * nl);
                }
                #endif

                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
