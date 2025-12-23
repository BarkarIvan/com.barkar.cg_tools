#ifndef CUSTOM_FLUFFY_TREE
#define CUSTOM_FLUFFY_TREE


half remap(half value, half inputMin, half inputMax, half outputMin, half outputMax)
{
    return outputMin + (value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin);
}

half2 remap(half2 value, half2 inputMin, half2 inputMax, half2 outputMin, half2 outputMax)
{
    return outputMin + (value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin);
}

float3 FluffyDisplace(float2 uv)
{
    float2 remapuv = remap(uv, half2(0,0), half2(1,1), half2(-1,-1), half2(1,1));
    float4 toView = mul(half4(remapuv,0,1),  UNITY_MATRIX_V);
    float3 toWorld = mul(unity_ObjectToWorld, toView).xyz;
    return toWorld = normalize(toWorld); //скейл
    

}
#endif