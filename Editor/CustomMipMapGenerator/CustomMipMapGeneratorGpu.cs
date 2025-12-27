using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomMipMapGenerator
{
public static class CustomMipMapGeneratorGpu
{
    private const int ThreadGroupSize = 8;
    private const int HistBins = 256;
    private const int SubsampleN = 4;

    private struct KernelIds
    {
        public int Generate;
        public int Stats;
        public int Scale;
        public int Sharpen;
        public int Copy;
        public int PrepareToksvigBase;
    }

    public static void Generate(Texture2D sourceTexture, CustomMipMapGeneratorSettings settings, ComputeShader shader)
    {
        GenerateInternal(sourceTexture, settings, shader, settings.compression, null);
    }

    public static void Generate(Texture2D sourceTexture, CustomMipMapGeneratorSettings settings, ComputeShader shader,
        TextureFormat compressionOverride, string outputSuffix)
    {
        GenerateInternal(sourceTexture, settings, shader, compressionOverride, outputSuffix);
    }

    private static void GenerateInternal(Texture2D sourceTexture, CustomMipMapGeneratorSettings settings, ComputeShader shader,
        TextureFormat compressionOverride, string outputSuffix)
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Compute shaders are not supported on this system.");
            return;
        }

        if (sourceTexture == null)
        {
            Debug.LogError("Source texture is missing.");
            return;
        }

        if (shader == null)
        {
            Debug.LogError("Compute shader is missing.");
            return;
        }

        var path = AssetDatabase.GetAssetPath(sourceTexture);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("Texture importer not found.");
            return;
        }

        ConfigureImporter(importer);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (sourceTexture == null)
        {
            Debug.LogError("Source texture failed to reload.");
            return;
        }

        var width = sourceTexture.width;
        var height = sourceTexture.height;
        bool isNormalMap = settings.textureKind == TextureKind.NormalMap;
        bool isDataMap = settings.textureKind == TextureKind.DataMap;
        var mipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2)) + 1;
        bool toksvigActive = isNormalMap && settings.toksvigInAlpha;
        int maxFullRes = Mathf.Min(6, Mathf.Max(0, mipCount - 1));
        var clampedFullResMipCount = Mathf.Clamp(settings.fullResMipCount, 0, maxFullRes);
        var shouldGammaCorrect = !isNormalMap && !isDataMap && PlayerSettings.colorSpace == ColorSpace.Gamma;
        var effectiveAlphaMode = toksvigActive ? AlphaFilterMode.None : settings.alphaFilterMode;
        bool perChannelActive = settings.usePerChannelFilter && isDataMap;
        bool perChannelCoverage = perChannelActive && (settings.channelFilterR == ChannelFilter.PreserveCoverage
            || settings.channelFilterG == ChannelFilter.PreserveCoverage
            || settings.channelFilterB == ChannelFilter.PreserveCoverage
            || settings.channelFilterA == ChannelFilter.PreserveCoverage);
        bool doPreserveCoverage = effectiveAlphaMode == AlphaFilterMode.PreserveCoverage || perChannelCoverage;
        int channelFilterRValue = perChannelActive ? ChannelFilterToShader(settings.channelFilterR) : 0;
        int channelFilterGValue = perChannelActive ? ChannelFilterToShader(settings.channelFilterG) : 0;
        int channelFilterBValue = perChannelActive ? ChannelFilterToShader(settings.channelFilterB) : 0;
        int channelFilterAValue = (perChannelActive && effectiveAlphaMode == AlphaFilterMode.None)
            ? ChannelFilterToShader(settings.channelFilterA)
            : 0;

        RenderTexture rt = null;
        RenderTexture rtSharpen = null;
        RenderTexture rtPrev = null;
        ComputeBuffer histBuffer = null;
        ComputeBuffer coverageBuffer = null;

        try
        {
            rt = CreateMipRenderTexture(width, height);

            bool sharpenActive = settings.sharpenEnabled && (settings.sharpenNormals || !isNormalMap);
            int maxSharpenMip = sharpenActive && mipCount > 1 ? Mathf.Clamp(settings.sharpenMipCount, 1, mipCount - 1) : 0;
            if (sharpenActive)
                rtSharpen = CreateMipRenderTexture(width, height);

            var prevActive = RenderTexture.active;
            Graphics.Blit(sourceTexture, rt);
            RenderTexture.active = prevActive;

            uint[] histData = null;
            uint[] coverageData = null;
            if (doPreserveCoverage)
            {
                histBuffer = new ComputeBuffer(HistBins, sizeof(uint));
                coverageBuffer = new ComputeBuffer(1, sizeof(uint));
                histData = new uint[HistBins];
                coverageData = new uint[1];
            }

            var kernels = FindKernels(shader);
            int[] coverageChannels = new int[4];

            if (toksvigActive)
                DispatchPrepareToksvigBase(shader, kernels.PrepareToksvigBase, sourceTexture, rt, width, height);

            SetStaticShaderParams(shader, settings, isNormalMap, shouldGammaCorrect, effectiveAlphaMode,
                channelFilterRValue, channelFilterGValue, channelFilterBValue, channelFilterAValue);

            for (int mip = 1; mip < mipCount; mip++)
            {
                EditorUtility.DisplayProgressBar("Generating MipMaps", $"Mip Level {mip}/{mipCount - 1}", (float)mip / (mipCount - 1));

                int mipWidth = Mathf.Max(1, width >> mip);
                int mipHeight = Mathf.Max(1, height >> mip);
                int prevWidth = Mathf.Max(1, width >> (mip - 1));
                int prevHeight = Mathf.Max(1, height >> (mip - 1));
                bool usePrev = mip > clampedFullResMipCount;
                int srcWidth = usePrev ? prevWidth : width;
                int srcHeight = usePrev ? prevHeight : height;
                float wratio = (float)srcWidth / mipWidth;
                float hratio = (float)srcHeight / mipHeight;
                int maxFilterRadius = Mathf.Clamp(settings.maxFilterRadiusMin + mip / Mathf.Max(1, settings.maxFilterStepSize),
                    settings.maxFilterRadiusMin, settings.maxFilterRadiusMax);

                SetPerMipShaderParams(shader, mipWidth, mipHeight, srcWidth, srcHeight, prevWidth, prevHeight,
                    wratio, hratio, usePrev, mip, maxFilterRadius);

                bool needsPrev = usePrev || effectiveAlphaMode != AlphaFilterMode.None;
                if (needsPrev)
                {
                    rtPrev = EnsurePrevTexture(rtPrev, prevWidth, prevHeight);
                    Graphics.CopyTexture(rt, 0, mip - 1, rtPrev, 0, 0);
                    shader.SetTexture(kernels.Generate, "_PrevTex", rtPrev);
                }

                shader.SetTexture(kernels.Generate, "_SrcTex", sourceTexture);
                shader.SetTexture(kernels.Generate, "_MipTex", rt);
                shader.SetTexture(kernels.Generate, "_Result", rt, mip);

                int groupsX = (mipWidth + ThreadGroupSize - 1) / ThreadGroupSize;
                int groupsY = (mipHeight + ThreadGroupSize - 1) / ThreadGroupSize;
                shader.Dispatch(kernels.Generate, groupsX, groupsY, 1);

                if (doPreserveCoverage)
                    ApplyCoveragePreservation(shader, kernels, rt, histBuffer, coverageBuffer, histData, coverageData,
                        settings, effectiveAlphaMode, mip, mipWidth, mipHeight, coverageChannels, groupsX, groupsY);

                if (sharpenActive && mip <= maxSharpenMip)
                    ApplySharpen(shader, kernels, rt, rtSharpen, settings, mip, mipWidth, mipHeight, groupsX, groupsY);
            }

            var mipTexture = ReadbackTexture(rt, width, height, mipCount, isNormalMap);
            if (mipTexture == null)
                return;

            mipTexture.Apply(false, false);
            EditorUtility.CompressTexture(mipTexture, compressionOverride, TextureCompressionQuality.Best);

            var newPath = BuildOutputPath(path, outputSuffix);
            AssetDatabase.CreateAsset(mipTexture, newPath);
            AssetDatabase.SaveAssets();

            RestoreImporter(importer, isNormalMap, isDataMap);

            Debug.Log("MipMaps generated: " + newPath);
        }
        finally
        {
            if (histBuffer != null)
                histBuffer.Release();
            if (coverageBuffer != null)
                coverageBuffer.Release();
            if (rt != null && RenderTexture.active == rt)
                RenderTexture.active = null;
            if (rtSharpen != null && RenderTexture.active == rtSharpen)
                RenderTexture.active = null;
            if (rt != null)
                rt.Release();
            if (rtSharpen != null)
                rtSharpen.Release();
            if (rtPrev != null)
                rtPrev.Release();
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ConfigureImporter(TextureImporter importer)
    {
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    private static void RestoreImporter(TextureImporter importer, bool isNormalMap, bool isDataMap)
    {
        importer.isReadable = false;
        importer.sRGBTexture = !isNormalMap && !isDataMap;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }

    private static string BuildOutputPath(string sourcePath, string suffix)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var normalizedSuffix = string.IsNullOrEmpty(suffix) ? string.Empty : (suffix.StartsWith(".") ? suffix : "." + suffix);
        var safeDir = string.IsNullOrEmpty(dir) ? "Assets" : dir.Replace('\\', '/');
        return safeDir + "/" + baseName + "_customMips" + normalizedSuffix + ".asset";
    }

    private static RenderTexture CreateMipRenderTexture(int width, int height)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            useMipMap = true,
            autoGenerateMips = false,
            enableRandomWrite = true,
            filterMode = UnityEngine.FilterMode.Point
        };
        rt.Create();
        return rt;
    }

    private static RenderTexture EnsurePrevTexture(RenderTexture existing, int width, int height)
    {
        if (existing != null && existing.width == width && existing.height == height)
            return existing;
        if (existing != null)
            existing.Release();
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = UnityEngine.FilterMode.Point
        };
        rt.Create();
        return rt;
    }

    private static KernelIds FindKernels(ComputeShader shader)
    {
        return new KernelIds
        {
            Generate = shader.FindKernel("GenerateMip"),
            Stats = shader.FindKernel("ComputeAlphaStats"),
            Scale = shader.FindKernel("ApplyAlphaScale"),
            Sharpen = shader.FindKernel("SharpenMip"),
            Copy = shader.FindKernel("CopyMip"),
            PrepareToksvigBase = shader.FindKernel("PrepareToksvigBase")
        };
    }

    private static void DispatchPrepareToksvigBase(ComputeShader shader, int kernel, Texture2D source, RenderTexture target,
        int width, int height)
    {
        shader.SetInt("_DstWidth", width);
        shader.SetInt("_DstHeight", height);
        shader.SetInt("_ToksvigInAlpha", 1);
        shader.SetTexture(kernel, "_SrcTex", source);
        shader.SetTexture(kernel, "_Result", target, 0);
        int groupsX = (width + ThreadGroupSize - 1) / ThreadGroupSize;
        int groupsY = (height + ThreadGroupSize - 1) / ThreadGroupSize;
        shader.Dispatch(kernel, groupsX, groupsY, 1);
    }

    private static void SetStaticShaderParams(ComputeShader shader, CustomMipMapGeneratorSettings settings, bool isNormalMap,
        bool shouldGammaCorrect, AlphaFilterMode effectiveAlphaMode, int channelFilterRValue, int channelFilterGValue,
        int channelFilterBValue, int channelFilterAValue)
    {
        shader.SetInt("_FilterMode", settings.filterMode == FilterMode.Kaiser ? 0 : 1);
        shader.SetInt("_AlphaMode", (int)effectiveAlphaMode);
        shader.SetInt("_ChannelFilterR", channelFilterRValue);
        shader.SetInt("_ChannelFilterG", channelFilterGValue);
        shader.SetInt("_ChannelFilterB", channelFilterBValue);
        shader.SetInt("_ChannelFilterA", channelFilterAValue);
        shader.SetFloat("_ChannelPower", settings.channelPower);
        shader.SetFloat("_KaiserBeta", settings.kaiserBeta);
        shader.SetFloat("_BaseRadius", settings.baseRadius);
        shader.SetFloat("_EwaSigma", settings.ewaSigma);
        shader.SetInt("_EdgeAware", (!isNormalMap && settings.edgeAware) ? 1 : 0);
        shader.SetFloat("_EdgeSigma", settings.edgeSigma);
        shader.SetFloat("_AlphaClip", settings.alphaClip);
        shader.SetInt("_NormalMap", isNormalMap ? 1 : 0);
        shader.SetInt("_GammaCorrect", shouldGammaCorrect ? 1 : 0);
        shader.SetInt("_ToksvigInAlpha", (isNormalMap && settings.toksvigInAlpha) ? 1 : 0);
        shader.SetInt("_MaxFilterRadiusMin", settings.maxFilterRadiusMin);
        shader.SetInt("_MaxFilterRadiusMax", settings.maxFilterRadiusMax);
        shader.SetInt("_MaxFilterStepSize", settings.maxFilterStepSize);
    }

    private static void SetPerMipShaderParams(ComputeShader shader, int mipWidth, int mipHeight, int srcWidth, int srcHeight,
        int prevWidth, int prevHeight, float wratio, float hratio, bool usePrev, int mip, int maxFilterRadius)
    {
        shader.SetInt("_DstWidth", mipWidth);
        shader.SetInt("_DstHeight", mipHeight);
        shader.SetInt("_SrcWidth", srcWidth);
        shader.SetInt("_SrcHeight", srcHeight);
        shader.SetInt("_PrevWidth", prevWidth);
        shader.SetInt("_PrevHeight", prevHeight);
        shader.SetFloat("_WRatio", wratio);
        shader.SetFloat("_HRatio", hratio);
        shader.SetInt("_UsePrev", usePrev ? 1 : 0);
        shader.SetInt("_MipLevel", mip);
        shader.SetInt("_MaxFilterRadius", maxFilterRadius);
    }

    private static void ApplyCoveragePreservation(ComputeShader shader, KernelIds kernels, RenderTexture rt, ComputeBuffer histBuffer,
        ComputeBuffer coverageBuffer, uint[] histData, uint[] coverageData, CustomMipMapGeneratorSettings settings,
        AlphaFilterMode effectiveAlphaMode, int mip, int mipWidth, int mipHeight, int[] coverageChannels, int groupsX, int groupsY)
    {
        int coverageCount = 0;
        if (effectiveAlphaMode == AlphaFilterMode.PreserveCoverage)
        {
            coverageChannels[coverageCount++] = 3;
        }
        else
        {
            if (settings.channelFilterR == ChannelFilter.PreserveCoverage)
                coverageChannels[coverageCount++] = 0;
            if (settings.channelFilterG == ChannelFilter.PreserveCoverage)
                coverageChannels[coverageCount++] = 1;
            if (settings.channelFilterB == ChannelFilter.PreserveCoverage)
                coverageChannels[coverageCount++] = 2;
            if (settings.channelFilterA == ChannelFilter.PreserveCoverage)
                coverageChannels[coverageCount++] = 3;
        }

        for (int i = 0; i < coverageCount; i++)
        {
            int coverageChannel = coverageChannels[i];
            System.Array.Clear(histData, 0, histData.Length);
            histBuffer.SetData(histData);
            coverageData[0] = 0;
            coverageBuffer.SetData(coverageData);

            shader.SetInt("_CoverageChannel", coverageChannel);
            shader.SetInt("_SubsampleN", SubsampleN);
            shader.SetBuffer(kernels.Stats, "_AlphaHist", histBuffer);
            shader.SetBuffer(kernels.Stats, "_AlphaCoverage", coverageBuffer);
            shader.SetTexture(kernels.Stats, "_MipTex", rt);
            shader.SetInt("_MipLevel", mip);
            shader.SetInt("_DstWidth", mipWidth);
            shader.SetInt("_DstHeight", mipHeight);
            shader.SetFloat("_AlphaClip", settings.alphaClip);

            shader.Dispatch(kernels.Stats, groupsX, groupsY, 1);

            histBuffer.GetData(histData);
            coverageBuffer.GetData(coverageData);

            int coveragePixels = Mathf.Max(1, (mipWidth - 1) * (mipHeight - 1));
            float targetCoverage = (float)coverageData[0] / (coveragePixels * SubsampleN * SubsampleN);
            int totalPixels = mipWidth * mipHeight;
            int targetCount = Mathf.Clamp(Mathf.RoundToInt(targetCoverage * totalPixels), 0, totalPixels);

            float scale;
            if (targetCount <= 0)
            {
                scale = 0f;
            }
            else if (targetCount >= totalPixels)
            {
                scale = 4f;
            }
            else
            {
                int cumulative = 0;
                int thresholdBin = 0;
                for (int bin = HistBins - 1; bin >= 0; bin--)
                {
                    cumulative += (int)histData[bin];
                    if (cumulative >= targetCount)
                    {
                        thresholdBin = bin;
                        break;
                    }
                }
                float threshold = thresholdBin / 255f;
                scale = settings.alphaClip / Mathf.Max(threshold, 1f / 255f);
                scale = Mathf.Clamp(scale, 0f, 4f);
            }

            shader.SetInt("_CoverageChannel", coverageChannel);
            shader.SetFloat("_AlphaScale", scale);
            shader.SetTexture(kernels.Scale, "_MipTex", rt);
            shader.SetTexture(kernels.Scale, "_Result", rt, mip);
            shader.SetInt("_MipLevel", mip);
            shader.SetInt("_DstWidth", mipWidth);
            shader.SetInt("_DstHeight", mipHeight);
            shader.Dispatch(kernels.Scale, groupsX, groupsY, 1);
        }
    }

    private static void ApplySharpen(ComputeShader shader, KernelIds kernels, RenderTexture rt, RenderTexture rtSharpen,
        CustomMipMapGeneratorSettings settings, int mip, int mipWidth, int mipHeight, int groupsX, int groupsY)
    {
        shader.SetFloat("_SharpenStrength", settings.sharpenStrength);
        shader.SetFloat("_SharpenClamp", settings.sharpenClamp);
        shader.SetTexture(kernels.Sharpen, "_MipTex", rt);
        shader.SetTexture(kernels.Sharpen, "_Result", rtSharpen, mip);
        shader.SetInt("_MipLevel", mip);
        shader.SetInt("_DstWidth", mipWidth);
        shader.SetInt("_DstHeight", mipHeight);
        shader.Dispatch(kernels.Sharpen, groupsX, groupsY, 1);
        shader.SetTexture(kernels.Copy, "_MipTex", rtSharpen);
        shader.SetTexture(kernels.Copy, "_Result", rt, mip);
        shader.SetInt("_MipLevel", mip);
        shader.SetInt("_DstWidth", mipWidth);
        shader.SetInt("_DstHeight", mipHeight);
        shader.Dispatch(kernels.Copy, groupsX, groupsY, 1);
    }

    private static Texture2D ReadbackTexture(RenderTexture rt, int width, int height, int mipCount, bool isNormalMap)
    {
        var mipTexture = new Texture2D(width, height, TextureFormat.RGBA32, mipCount, isNormalMap);
        for (int mip = 0; mip < mipCount; mip++)
        {
            var request = AsyncGPUReadback.Request(rt, mip, TextureFormat.RGBA32);
            request.WaitForCompletion();
            if (request.hasError)
            {
                Debug.LogError("GPU readback failed.");
                Object.DestroyImmediate(mipTexture);
                return null;
            }
            mipTexture.SetPixelData(request.GetData<Color32>(), mip);
        }
        return mipTexture;
    }

    private static int ChannelFilterToShader(ChannelFilter filter)
    {
        return filter == ChannelFilter.PreserveCoverage ? (int)ChannelFilter.WeightedAverage : (int)filter;
    }
}
}
