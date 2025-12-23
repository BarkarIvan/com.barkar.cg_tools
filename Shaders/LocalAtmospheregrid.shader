Shader "Barkar/VFX/LocalAtmospheregrid"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ParticlesPerAxis("Grid Step", Float) = 5
        _Color ("Color", Color) = (1,1,1,1)

    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "LightMode" = "UniversalForward"
        }

        Pass
        {
            Cull Off
            Name "Local Atmosphere Pass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 color : COLOR;
                half3 normal : NORMAL;
                half2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                half2 uv : TEXCOORD0;
                half3 color : COLOR;
                float4 positionCS : SV_POSITION;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float3 _TempPosWS;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _ParticlesPerAxis;
            CBUFFER_END


            Varyings vert(Attributes IN)
            {
                Varyings o;
o.uv = TRANSFORM_TEX(IN.uv, _MainTex);
float3 pivot = float3(IN.uv1.x, IN.uv1.y, IN.uv2.x);
float3 pivotWS = mul(GetObjectToWorldMatrix(), float4(pivot, 1.0));

half3 normalWS = normalize(mul((float3x3)GetObjectToWorldMatrix(), IN.normal));

half3 viewDir = normalize(GetCameraPositionWS() - pivotWS);

float3 up = cross(normalWS, viewDir);
float3 right = cross(viewDir, up);


float3x3 rotMtx = float3x3(right, up, viewDir);

float3 transformedPosition = mul(rotMtx, IN.positionOS.xyz - pivot) + pivot;

o.positionCS = TransformObjectToHClip(float4(transformedPosition, 1.0));
                
                half radius = 1.0 * 0.5;
                //grid step in model
                float gridStep = 1 / _ParticlesPerAxis;
                //local center of model
                float3 gridCenterOS = float3(0.0, 0.0, 0.0);
                //center WS
                float3 gridCenterWS = mul(GetObjectToWorldMatrix(), float4(gridCenterOS, 1)).xyz;

                //quantized offset
                float3 gridOffset = round(gridCenterWS / gridStep) * gridStep - gridCenterWS;

                

                
                float l = 1 - saturate(length(gridCenterWS - pivotWS - gridOffset) / radius);
                                float3 positionWS = mul(GetObjectToWorldMatrix(), float4(IN.positionOS.xyz, 1.0)).xyz;


                float3 correctedPosition = positionWS + gridOffset;
                
                
               // o.positionCS = mul(GetWorldToHClipMatrix(), float4(correctedPosition, 1.0));

                o.color = l;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return half4(IN.color, 1);
            }
            ENDHLSL
        }
    }
}