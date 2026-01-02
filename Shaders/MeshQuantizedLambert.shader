Shader "CGTools/MeshQuantization/Lambert"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        [Toggle(_MQ_QUANTIZED)] _MQQuantized ("Use Quantized Normals", Float) = 1
        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalMapScale ("Normal Map Scale", Range(0, 2)) = 1
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
            #pragma vertex MQVert
            #pragma fragment MQFrag

            #pragma shader_feature_local _MQ_QUANTIZED
            #pragma shader_feature_local _NORMALMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/MeshQuantization.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _NormalMap_ST;
                half _NormalMapScale;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                #if defined(_NORMALMAP)
                float3 tangentWS : TEXCOORD1;
                float3 bitangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                #endif
            };

            Varyings MQVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS);
                OUT.positionCS = pos.positionCS;

                #if defined(_MQ_QUANTIZED)
                float3 normalOS = MQ_DecodeNormalFromColor(IN.color);
                #else
                float3 normalOS = IN.normalOS;
                #endif
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                OUT.normalWS = normalWS;

                #if defined(_NORMALMAP)
                #if defined(_MQ_QUANTIZED)
                float4 tangentOS = MQ_DecodeTangentFromColor(IN.color, normalOS);
                #else
                float4 tangentOS = IN.tangentOS;
                #endif
                float3 tangentWS = normalize(TransformObjectToWorldDir(tangentOS.xyz));
                float tangentSign = tangentOS.w * GetOddNegativeScale();
                float3 bitangentWS = MQ_DecodeBitangent(normalWS, tangentWS, tangentSign);
                OUT.tangentWS = tangentWS;
                OUT.bitangentWS = bitangentWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _NormalMap);
                #endif

                return OUT;
            }

            half4 MQFrag(Varyings IN) : SV_Target
            {
                #if defined(_NORMALMAP)
                half3 tnormal = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalMapScale);
                half3 n = normalize(IN.tangentWS * tnormal.x + IN.bitangentWS * tnormal.y + IN.normalWS * tnormal.z);
                #else
                half3 n = normalize(IN.normalWS);
                #endif
                Light mainLight = GetMainLight();
                half nl = saturate(dot(n, mainLight.direction));
                half3 diffuse = _BaseColor.rgb * (mainLight.color * nl);
                return half4(diffuse, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
