#ifndef BRDF_FACTORIZATION_INCLUDED
#define BRDF_FACTORIZATION_INCLUDED

float2 BrdfFactorization_ParabolicUV(float3 dir)
{
    dir = normalize(dir);
    float2 uv = dir.xy / (1.0 + dir.z);
    return uv * 0.5 + 0.5;
}

float3 BrdfFactorization_ToLocal(float3 dirWS, float3 T, float3 B, float3 N)
{
    return normalize(float3(dot(dirWS, T), dot(dirWS, B), dot(dirWS, N)));
}

float3 BrdfFactorization_Sample(Texture2D tex, float3 dir)
{
    float2 uv = BrdfFactorization_ParabolicUV(dir);
    return SAMPLE_TEXTURE2D(tex, sampler_LinearClamp, uv).rgb;
}

float3 BrdfFactorization_Evaluate(Texture2D pTex, Texture2D qTex, float3 V, float3 L, float3 T, float3 B, float3 N, float3 scale)
{
    float3 Vt = BrdfFactorization_ToLocal(V, T, B, N);
    float3 Lt = BrdfFactorization_ToLocal(L, T, B, N);
    float3 Ht = normalize(Vt + Lt);
    float3 pV = BrdfFactorization_Sample(pTex, Vt);
    float3 pL = BrdfFactorization_Sample(pTex, Lt);
    float3 qH = BrdfFactorization_Sample(qTex, Ht);
    return scale * (pV * qH * pL);
}

#endif
