using System;
using UnityEngine;

namespace CustomMipMapGenerator
{
    public enum AlphaFilterMode { None, PreserveCoverage, MaxFilter }
    public enum FilterMode { Kaiser, Ewa }
    public enum TextureKind { Color, NormalMap, DataMap }
    public enum ChannelFilter
    {
        WeightedAverage = 0,
        Min = 1,
        Max = 2,
        LinearRoughness = 3,
        LinearSmoothness = 4,
        PowerMean = 5,
        PreserveCoverage = 6
    }

    [Serializable]
    public class CustomMipMapGeneratorSettings
    {
        [Header("Texture Type")]
        public TextureKind textureKind = TextureKind.Color;
        [Header("Filter")]
        public FilterMode filterMode = FilterMode.Kaiser;
        public bool edgeAware;
        [Range(0.01f, 0.5f)]
        public float edgeSigma = 0.12f;
        [Range(0.5f, 3f)]
        public float ewaSigma = 1.0f;
        [Header("Sharpen")]
        public bool sharpenEnabled = true;
        [Range(0f, 1f)]
        public float sharpenStrength = 0.2f;
        [Range(0f, 0.2f)]
        public float sharpenClamp = 0.05f;
        [Range(1, 6)]
        public int sharpenMipCount = 3;
        public bool sharpenNormals;
        [Header("Toksvig")]
        public bool toksvigInAlpha;
        [Header("Compression"), HideInInspector]
        public TextureFormat compressionMobile = TextureFormat.ASTC_6x6;
        [HideInInspector]
        public TextureFormat compressionPc = TextureFormat.BC7;
        [Header("Sampling")]
        public TextureWrapMode wrapModeU = TextureWrapMode.Repeat;
        public TextureWrapMode wrapModeV = TextureWrapMode.Repeat;
        public UnityEngine.FilterMode samplerFilterMode = UnityEngine.FilterMode.Bilinear;
        [Range(1, 16)]
        public int anisoLevel = 1;
        [Range(-2f, 2f)]
        public float mipBias;
        [Header("Full-Res Mips")]
        [Range(0, 64)]
        public int maxFullResRatio = 16;
        [Header("Kaiser")]
        [Range(1f, 20f)]
        public float kaiserBeta = 6f;
        [Range(1f, 6f)]
        public float baseRadius = 3f;
        [Header("Per-Channel Filters")]
        public bool usePerChannelFilter;
        public ChannelFilter channelFilterR = ChannelFilter.WeightedAverage;
        public ChannelFilter channelFilterG = ChannelFilter.WeightedAverage;
        public ChannelFilter channelFilterB = ChannelFilter.WeightedAverage;
        public ChannelFilter channelFilterA = ChannelFilter.WeightedAverage;
        [Range(0.25f, 8f)]
        public float channelPower = 2.0f;
        [Header("Alpha Filtering")]
        public AlphaFilterMode alphaFilterMode = AlphaFilterMode.None;
        [Range(0f, 1f)]
        public float alphaClip = 0.5f;
        [Range(1, 4)]
        public int maxFilterRadiusMin = 1;
        [Range(1, 8)]
        public int maxFilterRadiusMax = 1;
        [Range(1, 4)]
        public int maxFilterStepSize = 1;
        [Min(0)]
        public int fullResMipCount = 2;

        public CustomMipMapGeneratorSettings Clone()
        {
            return new CustomMipMapGeneratorSettings
            {
                textureKind = textureKind,
                filterMode = filterMode,
                edgeAware = edgeAware,
                edgeSigma = edgeSigma,
                ewaSigma = ewaSigma,
                sharpenEnabled = sharpenEnabled,
                sharpenStrength = sharpenStrength,
                sharpenClamp = sharpenClamp,
                sharpenMipCount = sharpenMipCount,
                sharpenNormals = sharpenNormals,
                toksvigInAlpha = toksvigInAlpha,
                compressionMobile = compressionMobile,
                compressionPc = compressionPc,
                wrapModeU = wrapModeU,
                wrapModeV = wrapModeV,
                samplerFilterMode = samplerFilterMode,
                anisoLevel = anisoLevel,
                mipBias = mipBias,
                maxFullResRatio = maxFullResRatio,
                kaiserBeta = kaiserBeta,
                baseRadius = baseRadius,
                usePerChannelFilter = usePerChannelFilter,
                channelFilterR = channelFilterR,
                channelFilterG = channelFilterG,
                channelFilterB = channelFilterB,
                channelFilterA = channelFilterA,
                channelPower = channelPower,
                alphaFilterMode = alphaFilterMode,
                alphaClip = alphaClip,
                maxFilterRadiusMin = maxFilterRadiusMin,
                maxFilterRadiusMax = maxFilterRadiusMax,
                maxFilterStepSize = maxFilterStepSize,
                fullResMipCount = fullResMipCount
            };
        }
    }
}
