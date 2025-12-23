Shader "Hidden/ACESFilmToneMapping"
{
    Properties {}

    SubShader
    {
        Tags {}

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BackgroundFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #if SHADER_API_GLES
                struct Attributes
                {
                float4 positionOS       : POSITION;
                half2 uv               : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                };
            #else
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #endif

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            uniform float4 _BlitScaleBias;
            uniform float4 _BlitScaleBiasRt;


            half3 ACESFIlm(half3 col)
            {
                half a = 2.51;
                half b = 0.03;
                half c = 2.43;
                half d = 0.59;
                half e = 0.14;
                return saturate((col * (a * col + b)) / (col * (c * col + d) + e));
            }


            Varyings Vert(Attributes IN)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                #if SHADER_API_GLES
                  float4 pos = IN.positionOS;
                  half2 uv  = IN.uv;
                #else
                float4 pos = GetFullScreenTriangleVertexPosition(IN.vertexID);
                half2 uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                #endif
                output.positionCS = pos;

                output.uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return output;
            }

            half4 BackgroundFragment(Varyings IN) : SV_Target
            {
                half4 result = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, IN.uv);
                result.rgb = ACESFIlm(result.rgb);
                return result;
            }
            ENDHLSL
        }
    }
}