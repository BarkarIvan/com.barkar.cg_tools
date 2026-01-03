#ifndef ARMLIT_DEPTH_ONLY_PASS_INCLUDED
#define ARMLIT_DEPTH_ONLY_PASS_INCLUDED

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float4 positionCS : SV_POSITION;
};

Varyings ARMLitDepthOnlyVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positionInputs.positionCS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    return output;
}

half4 ARMLitDepthOnlyFragment(Varyings input) : SV_TARGET
{
    ARMLit_AlphaClip(input.uv);
    return 0;
}

#endif
