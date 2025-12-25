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
        public TextureKind textureKind = TextureKind.Color;
        public FilterMode filterMode = FilterMode.Kaiser;
        public bool edgeAware;
        public float edgeSigma = 0.12f;
        public float ewaSigma = 1.0f;
        public bool sharpenEnabled = true;
        public float sharpenStrength = 0.2f;
        public float sharpenClamp = 0.05f;
        public int sharpenMipCount = 3;
        public bool sharpenNormals;
        public bool toksvigInAlpha;
        public TextureFormat compression = TextureFormat.ASTC_6x6;
        public float kaiserBeta = 6f;
        public float baseRadius = 3f;
        public bool usePerChannelFilter;
        public ChannelFilter channelFilterR = ChannelFilter.WeightedAverage;
        public ChannelFilter channelFilterG = ChannelFilter.WeightedAverage;
        public ChannelFilter channelFilterB = ChannelFilter.WeightedAverage;
        public ChannelFilter channelFilterA = ChannelFilter.WeightedAverage;
        public float channelPower = 2.0f;
        public AlphaFilterMode alphaFilterMode = AlphaFilterMode.None;
        public float alphaClip = 0.5f;
        public int maxFilterRadiusMin = 1;
        public int maxFilterRadiusMax = 1;
        public int maxFilterStepSize = 1;
        public int fullResMipCount = 2;
    }
}
