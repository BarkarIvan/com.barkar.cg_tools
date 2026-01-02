#ifndef GLTF_IRIDESCENCE_INCLUDED
#define GLTF_IRIDESCENCE_INCLUDED

#include "Packages/com.barkar.cg_tools/ShaderLibrary/GltfExtensions/GltfCore.hlsl"

static const float3x3 GLTF_XYZ_TO_REC709 = float3x3(
     3.2404542, -0.9692660,  0.0556434,
    -1.5371385,  1.8760108, -0.2040259,
    -0.4985314,  0.0415560,  1.0572252
);

float Gltf_Sq(float v)
{
    return v * v;
}

float2 Gltf_Sq(float2 v)
{
    return v * v;
}

float3 Gltf_Sq(float3 v)
{
    return v * v;
}

float Gltf_Smoothstep(float edge0, float edge1, float x)
{
    float t = saturate((x - edge0) / (edge1 - edge0));
    return t * t * (3.0 - 2.0 * t);
}

float3 Gltf_Fresnel0ToIor(float3 fresnel0)
{
    float3 sqrtF0 = sqrt(fresnel0);
    return (float3(1.0, 1.0, 1.0) + sqrtF0) / (float3(1.0, 1.0, 1.0) - sqrtF0);
}

float3 Gltf_IorToFresnel0(float3 transmittedIor, float incidentIor)
{
    return Gltf_Sq((transmittedIor - incidentIor) / (transmittedIor + incidentIor));
}

float Gltf_IorToFresnel0(float transmittedIor, float incidentIor)
{
    return Gltf_Sq((transmittedIor - incidentIor) / (transmittedIor + incidentIor));
}

float3 Gltf_EvalSensitivity(float OPD, float3 shift)
{
    float phase = 2.0 * PI * OPD * 1.0e-9;
    float3 val = float3(5.4856e-13, 4.4201e-13, 5.2481e-13);
    float3 pos = float3(1.6810e+06, 1.7953e+06, 2.2084e+06);
    float3 var = float3(4.3278e+09, 9.3046e+09, 6.6121e+09);

    float3 xyz = val * sqrt(2.0 * PI * var) * cos(pos * phase + shift) * exp(-Gltf_Sq(phase) * var);
    xyz.x += 9.7470e-14 * sqrt(2.0 * PI * 4.5282e+09) * cos(2.2399e+06 * phase + shift.x) *
        exp(-4.5282e+09 * Gltf_Sq(phase));
    xyz /= 1.0685e-7;

    return mul(GLTF_XYZ_TO_REC709, xyz);
}

float3 Gltf_EvalIridescence(float outsideIOR, float eta2, float cosTheta1, float thinFilmThickness, float3 baseF0)
{
    float iridescenceIor = lerp(outsideIOR, eta2, Gltf_Smoothstep(0.0, 0.03, thinFilmThickness));
    float sinTheta2Sq = Gltf_Sq(outsideIOR / iridescenceIor) * (1.0 - Gltf_Sq(cosTheta1));

    float cosTheta2Sq = 1.0 - sinTheta2Sq;
    if (cosTheta2Sq < 0.0)
    {
        return float3(1.0, 1.0, 1.0);
    }

    float cosTheta2 = sqrt(cosTheta2Sq);

    float R0 = Gltf_IorToFresnel0(iridescenceIor, outsideIOR);
    float R12 = Gltf_F_Schlick(R0, cosTheta1);
    float T121 = 1.0 - R12;
    float phi12 = 0.0;
    if (iridescenceIor < outsideIOR)
    {
        phi12 = PI;
    }
    float phi21 = PI - phi12;

    float3 baseIOR = Gltf_Fresnel0ToIor(clamp(baseF0, 0.0, 0.9999));
    float3 R1 = Gltf_IorToFresnel0(baseIOR, iridescenceIor);
    float3 R23 = Gltf_F_Schlick(R1, float3(1.0, 1.0, 1.0), cosTheta2);
    float3 phi23 = float3(0.0, 0.0, 0.0);
    if (baseIOR.x < iridescenceIor)
    {
        phi23.x = PI;
    }
    if (baseIOR.y < iridescenceIor)
    {
        phi23.y = PI;
    }
    if (baseIOR.z < iridescenceIor)
    {
        phi23.z = PI;
    }

    float OPD = 2.0 * iridescenceIor * thinFilmThickness * cosTheta2;
    float3 phi = float3(phi21, phi21, phi21) + phi23;

    float3 R123 = clamp(R12 * R23, 1e-5, 0.9999);
    float3 r123 = sqrt(R123);
    float3 Rs = Gltf_Sq(T121) * R23 / (float3(1.0, 1.0, 1.0) - R123);

    float3 C0 = float3(R12, R12, R12) + Rs;
    float3 I = C0;

    float3 Cm = Rs - float3(T121, T121, T121);
    [unroll]
    for (int m = 1; m <= 2; ++m)
    {
        Cm *= r123;
        float3 Sm = 2.0 * Gltf_EvalSensitivity((float)m * OPD, (float)m * phi);
        I += Cm * Sm;
    }

    return max(I, float3(0.0, 0.0, 0.0));
}

float3 Gltf_RgbMix(float3 baseColor, float3 layerColor, float3 rgbAlpha)
{
    float rgbAlphaMax = max(max(rgbAlpha.r, rgbAlpha.g), rgbAlpha.b);
    return (1.0 - rgbAlphaMax) * baseColor + rgbAlpha * layerColor;
}

#endif
