#ifndef ARMLIT_SPECULAR_AA_INCLUDED
#define ARMLIT_SPECULAR_AA_INCLUDED

// Specular AA via normal variance (Toksvig/Hill style).
// Assumes perceptual roughness r in [0..1], where alpha = r^2 for GGX.
// We approximate the normal distribution variance over the pixel using ddx/ddy
// of the shading normal (includes normal map if present). The variance is added
// in alpha space and converted back to perceptual roughness. strength scales the
// effect (0 = no change).
inline half ApplySpecularAA(half perceptualRoughness, float3 normalWS, half strength)
{
    normalWS = SafeNormalize(normalWS);

    float3 dndx = ddx(normalWS);
    float3 dndy = ddy(normalWS);
    float variance = max(0.0, 0.5 * (dot(dndx, dndx) + dot(dndy, dndy)));

    float r  = saturate(perceptualRoughness);
    float a  = r * r;       // alpha
    float a2 = a * a;       // alpha^2

    a2 = saturate(a2 + variance * strength);

    return pow(a2, 0.25);     
}

// Valve (Alex Vlachos, GDC 2015 slide 43): geometric roughness from derivatives
// of the interpolated geometric normal. This clamps roughness from below to
// reduce sparkling on dense meshes even without normal maps.
inline half ApplyGeometricRoughness(half perceptualRoughness, float3 geometricNormalWS, half strength)
{
    float3 dndx = ddx(geometricNormalWS);
    float3 dndy = ddy(geometricNormalWS);
    float maxLen2 = max(dot(dndx, dndx), dot(dndy, dndy));
    float geo = pow(saturate(maxLen2), 0.3333333);
    geo *=strength;
    return max(perceptualRoughness, (half)geo);
}
//WIP

// Valve (GDC 2015 slide 44): centroid interpolation for normals to reduce
// silhouette sparkling. If the regular interpolated normal length is > 1.01,
// prefer centroid normal. Call this before normalization.
inline float3 SelectCentroidNormal(float3 normalWS, float3 centroidNormalWS)
{
    if (dot(normalWS, normalWS) >= 1.01)
    {
        return centroidNormalWS;
    }
    return normalWS;
}

#endif
