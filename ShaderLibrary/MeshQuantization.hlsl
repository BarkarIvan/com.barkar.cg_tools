#ifndef MQ_MESH_QUANTIZATION_INCLUDED
#define MQ_MESH_QUANTIZATION_INCLUDED

#define MQ_PI 3.14159265358979323846

float3 MQ_DecodeOct(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = max(-n.z, 0.0);
    n.x += n.x >= 0.0 ? -t : t;
    n.y += n.y >= 0.0 ? -t : t;
    return normalize(n);
}

float3 MQ_DecodeNormalFromColor(float4 packed)
{
    return MQ_DecodeOct(packed.xy);
}

float3 MQ_CalculateTangentBase(float3 normalWS)
{
    return abs(normalWS.x) > abs(normalWS.z)
        ? normalize(float3(-normalWS.y, normalWS.x, 0.0))
        : normalize(float3(0.0, -normalWS.z, normalWS.y));
}

float4 MQ_DecodeTangentFromColor(float4 packed)
{
    float3 n = MQ_DecodeNormalFromColor(packed);
    float3 tb = MQ_CalculateTangentBase(n);
    float angle = packed.z * (2.0 * MQ_PI);
    float sign = packed.w > 0.5 ? 1.0 : -1.0;
    float3 t = normalize(tb * cos(angle) + cross(n, tb) * sin(angle));
    return float4(t, sign);
}

float3 MQ_DecodeBitangent(float3 n, float3 t, float sign)
{
    return sign * cross(n, t);
}

#endif
