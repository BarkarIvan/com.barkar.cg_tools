#ifndef ARMLIT_DEPTH_NORMALS_PASS_INCLUDED
#define ARMLIT_DEPTH_NORMALS_PASS_INCLUDED

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    #if defined(_NORMALMAP)
    float3 tangentWS : TEXCOORD2;
    float3 bitangentWS : TEXCOORD3;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float2 ARMLitEncodeNormalOct(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    float2 enc = (n.z >= 0.0) ? n.xy : (1.0 - abs(n.yx)) * (n.xy >= 0.0 ? 1.0 : -1.0);
    return enc * 0.5 + 0.5;
}

Varyings ARMLitDepthNormalsVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positionInputs.positionCS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

    float3 normalOS;
    float4 tangentOS;
    ARMLit_GetNormalTangentOS(input.normalOS, input.tangentOS, input.color, normalOS, tangentOS);
    float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
    output.normalWS = normalWS;

    #if defined(_NORMALMAP)
    float3 tangentWS = normalize(TransformObjectToWorldDir(tangentOS.xyz));
    float tangentSign = tangentOS.w * GetOddNegativeScale();
    float3 bitangentWS = tangentSign * cross(normalWS, tangentWS);
    output.tangentWS = tangentWS;
    output.bitangentWS = bitangentWS;
    #endif

    return output;
}

half4 ARMLitDepthNormalsFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    ARMLit_AlphaClip(input.uv);

    float3 normalWS = SafeNormalize(input.normalWS);
    #if defined(_NORMALMAP)
    half4 n = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
    half3 normalTS = UnpackNormalScale(n, _NormalMapScale);
    half3x3 tangentToWorld = half3x3(input.tangentWS, input.bitangentWS, normalWS);
    normalWS = SafeNormalize(mul(normalTS, tangentToWorld));
    #endif

    float2 enc = ARMLitEncodeNormalOct(normalWS);
    return half4(enc, 0, 0);
}

#endif
