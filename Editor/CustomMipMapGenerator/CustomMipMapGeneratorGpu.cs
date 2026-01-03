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
        GenerateInternal(sourceTexture, settings, shader, settings.compressionPc, ".standalone");
    }

    public static void Generate(Texture2D sourceTexture, CustomMipMapGeneratorSettings settings, ComputeShader shader,
        TextureFormat compressionOverride, string outputSuffix)
    {
        GenerateInternal(sourceTexture, settings, shader, compressionOverride, outputSuffix);
    }

    public static void GenerateCustomMipFile(Texture2D sourceTexture, CustomMipMapGeneratorSettings settings, ComputeShader shader)
    {
        GenerateInternal(sourceTexture, settings, shader, TextureFormat.RGBA32, null, true);
    }

    private static void GenerateInternal(Texture2D sourceTexture, CustomMipMapGeneratorSettings settings, ComputeShader shader,
        TextureFormat compressionOverride, string outputSuffix, bool writeCustomFile = false)
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

        var importerState = CaptureImporterState(importer);
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
        bool useAlphaPyramid = !toksvigActive && settings.alphaFilterMode == AlphaFilterMode.AlphaPyramid;
        bool useErrorDiffusion = !toksvigActive && settings.alphaFilterMode == AlphaFilterMode.ErrorDiffusion;
        int maxFullRes = Mathf.Min(6, Mathf.Max(0, mipCount - 1));
        var clampedFullResMipCount = Mathf.Clamp(settings.fullResMipCount, 0, maxFullRes);
        var shouldGammaCorrect = !isNormalMap && !isDataMap && PlayerSettings.colorSpace == ColorSpace.Gamma;
        var shaderAlphaMode = (toksvigActive || useAlphaPyramid || useErrorDiffusion)
            ? AlphaFilterMode.None
            : settings.alphaFilterMode;
        bool perChannelActive = settings.usePerChannelFilter && isDataMap;
        bool perChannelCoverage = perChannelActive && (settings.channelFilterR == ChannelFilter.PreserveCoverage
            || settings.channelFilterG == ChannelFilter.PreserveCoverage
            || settings.channelFilterB == ChannelFilter.PreserveCoverage
            || settings.channelFilterA == ChannelFilter.PreserveCoverage);
        bool doPreserveCoverage = shaderAlphaMode == AlphaFilterMode.PreserveCoverage || perChannelCoverage;
        int channelFilterRValue = perChannelActive ? ChannelFilterToShader(settings.channelFilterR) : 0;
        int channelFilterGValue = perChannelActive ? ChannelFilterToShader(settings.channelFilterG) : 0;
        int channelFilterBValue = perChannelActive ? ChannelFilterToShader(settings.channelFilterB) : 0;
        int channelFilterAValue = (perChannelActive && shaderAlphaMode == AlphaFilterMode.None)
            ? ChannelFilterToShader(settings.channelFilterA)
            : 0;

        RenderTexture rt = null;
        RenderTexture rtSharpen = null;
        RenderTexture rtPrev = null;
        RenderTexture rtScale = null;
        ComputeBuffer histBuffer = null;
        ComputeBuffer coverageBuffer = null;

        try
        {
            rt = CreateMipRenderTexture(width, height);
            if (doPreserveCoverage)
                rtScale = CreateMipRenderTexture(width, height);

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

            SetStaticShaderParams(shader, settings, isNormalMap, shouldGammaCorrect, shaderAlphaMode,
                channelFilterRValue, channelFilterGValue, channelFilterBValue, channelFilterAValue);

            bool warnedFullResRatio = false;
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
                if (!usePrev && settings.maxFullResRatio > 0)
                {
                    float maxRatio = Mathf.Max(wratio, hratio);
                    if (maxRatio > settings.maxFullResRatio)
                    {
                        usePrev = true;
                        srcWidth = prevWidth;
                        srcHeight = prevHeight;
                        wratio = (float)srcWidth / mipWidth;
                        hratio = (float)srcHeight / mipHeight;
                        if (!warnedFullResRatio)
                        {
                            Debug.LogWarning($"Full-res ratio {maxRatio:0.##} exceeds cap {settings.maxFullResRatio}. Switching to previous mip.");
                            warnedFullResRatio = true;
                        }
                    }
                }
                int maxFilterRadius = Mathf.Clamp(settings.maxFilterRadiusMin + mip / Mathf.Max(1, settings.maxFilterStepSize),
                    settings.maxFilterRadiusMin, settings.maxFilterRadiusMax);

                SetPerMipShaderParams(shader, mipWidth, mipHeight, srcWidth, srcHeight, prevWidth, prevHeight,
                    wratio, hratio, usePrev, mip, maxFilterRadius);

                bool needsPrev = usePrev || shaderAlphaMode != AlphaFilterMode.None || doPreserveCoverage;
                if (needsPrev)
                {
                    rtPrev = EnsurePrevTexture(rtPrev, prevWidth, prevHeight);
                    Graphics.CopyTexture(rt, 0, mip - 1, rtPrev, 0, 0);
                }
                shader.SetTexture(kernels.Generate, "_PrevTex", needsPrev ? rtPrev : rt);

                shader.SetTexture(kernels.Generate, "_SrcTex", sourceTexture);
                shader.SetTexture(kernels.Generate, "_MipTex", rt);
                shader.SetTexture(kernels.Generate, "_Result", rt, mip);

                int groupsX = (mipWidth + ThreadGroupSize - 1) / ThreadGroupSize;
                int groupsY = (mipHeight + ThreadGroupSize - 1) / ThreadGroupSize;
                shader.Dispatch(kernels.Generate, groupsX, groupsY, 1);

                if (doPreserveCoverage)
                    ApplyCoveragePreservation(shader, kernels, rt, rtPrev, rtScale, histBuffer, coverageBuffer, histData, coverageData,
                        settings, shaderAlphaMode, mip, mipWidth, mipHeight, prevWidth, prevHeight, coverageChannels, groupsX, groupsY);

                if (sharpenActive && mip <= maxSharpenMip)
                    ApplySharpen(shader, kernels, rt, rtSharpen, settings, mip, mipWidth, mipHeight, groupsX, groupsY);
            }

            var mipTexture = ReadbackTexture(rt, width, height, mipCount, isNormalMap || isDataMap);
            if (mipTexture == null)
                return;

            if (useAlphaPyramid)
                ApplyAlphaPyramid(mipTexture, mipCount, settings.alphaClip);
            if (useErrorDiffusion)
                ApplyAlphaErrorDiffusion(mipTexture, mipCount, settings.alphaClip, settings.alphaDitherNoise);

            mipTexture.Apply(false, false);

            if (writeCustomFile)
            {
                var outputPath = BuildCustomMipFilePath(path);
                if (!CustomMipMapGeneratorMipFile.TryWrite(outputPath, mipTexture, settings.textureKind, out var error))
                {
                    Debug.LogError($"Failed to write custom mip file: {error}");
                }
                else
                {
                    AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                    ConfigureCustomMipImporter(outputPath, settings);
                    Debug.Log("MipMaps generated: " + outputPath);
                }

                RestoreImporter(importer, importerState);
                Object.DestroyImmediate(mipTexture);
                return;
            }

            ApplySamplingSettings(mipTexture, settings);
            if (!SystemInfo.SupportsTextureFormat(compressionOverride))
            {
                Debug.LogWarning($"Compression format {compressionOverride} is not supported on this platform. Preview may be black.");
            }
            EditorUtility.CompressTexture(mipTexture, compressionOverride, TextureCompressionQuality.Best);

            var resolvedSuffix = string.IsNullOrWhiteSpace(outputSuffix) ? ".standalone" : outputSuffix;
            var newPath = BuildOutputPath(path, resolvedSuffix);
            SaveOrUpdateAsset(mipTexture, newPath);

            RestoreImporter(importer, importerState);

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
            if (rtScale != null)
                rtScale.Release();
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

    private struct ImporterState
    {
        public bool isReadable;
        public TextureImporterCompression compression;
        public bool mipmapEnabled;
        public bool sRGBTexture;
        public TextureImporterAlphaSource alphaSource;
        public bool alphaIsTransparency;
    }

    private static ImporterState CaptureImporterState(TextureImporter importer)
    {
        return new ImporterState
        {
            isReadable = importer.isReadable,
            compression = importer.textureCompression,
            mipmapEnabled = importer.mipmapEnabled,
            sRGBTexture = importer.sRGBTexture,
            alphaSource = importer.alphaSource,
            alphaIsTransparency = importer.alphaIsTransparency
        };
    }

    private static void RestoreImporter(TextureImporter importer, ImporterState state)
    {
        importer.isReadable = state.isReadable;
        importer.textureCompression = state.compression;
        importer.mipmapEnabled = state.mipmapEnabled;
        importer.sRGBTexture = state.sRGBTexture;
        importer.alphaSource = state.alphaSource;
        importer.alphaIsTransparency = state.alphaIsTransparency;
        importer.SaveAndReimport();
    }

    private static string BuildOutputPath(string sourcePath, string suffix)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var normalizedSuffix = string.IsNullOrEmpty(suffix) ? string.Empty : (suffix.StartsWith(".") ? suffix : "." + suffix);
        var safeDir = string.IsNullOrEmpty(dir) ? "Assets" : dir.Replace('\\', '/');
        return safeDir + "/" + baseName + normalizedSuffix + ".asset";
    }

    private static string BuildCustomMipFilePath(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var safeDir = string.IsNullOrEmpty(dir) ? "Assets" : dir.Replace('\\', '/');
        return safeDir + "/" + baseName + CustomMipMapGeneratorMipFile.Extension;
    }

    private static void ConfigureCustomMipImporter(string assetPath, CustomMipMapGeneratorSettings settings)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as CustomMipMapGeneratorImporter;
        if (importer == null)
        {
            Debug.LogWarning($"Custom mip importer not found for {assetPath}.");
            return;
        }

        importer.WrapModeU = settings.wrapModeU;
        importer.WrapModeV = settings.wrapModeV;
        importer.SamplerFilterMode = settings.samplerFilterMode;
        importer.AnisoLevel = settings.anisoLevel;
        importer.MipBias = settings.mipBias;
        importer.SaveAndReimport();
    }

    private static void ApplySamplingSettings(Texture2D texture, CustomMipMapGeneratorSettings settings)
    {
        if (texture == null || settings == null)
            return;

        texture.wrapModeU = settings.wrapModeU;
        texture.wrapModeV = settings.wrapModeV;
        texture.filterMode = settings.samplerFilterMode;
        texture.anisoLevel = Mathf.Clamp(settings.anisoLevel, 1, 16);
        texture.mipMapBias = settings.mipBias;
    }

    private static void SaveOrUpdateAsset(Texture2D mipTexture, string assetPath)
    {
        var assetName = Path.GetFileNameWithoutExtension(assetPath);
        if (!string.IsNullOrEmpty(assetName))
            mipTexture.name = assetName;

        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (existing == null)
        {
            var existingObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (existingObject != null)
            {
                Debug.LogError($"Cannot overwrite non-texture asset at {assetPath}.");
                Object.DestroyImmediate(mipTexture);
                return;
            }

            AssetDatabase.CreateAsset(mipTexture, assetPath);
            AssetDatabase.SaveAssets();
            return;
        }

        EditorUtility.CopySerialized(mipTexture, existing);
        if (!string.IsNullOrEmpty(assetName) && existing.name != assetName)
            existing.name = assetName;
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        Object.DestroyImmediate(mipTexture);
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

    private static void ApplyCoveragePreservation(ComputeShader shader, KernelIds kernels, RenderTexture rt, RenderTexture prevRt,
        RenderTexture scaleRt, ComputeBuffer histBuffer, ComputeBuffer coverageBuffer, uint[] histData, uint[] coverageData,
        CustomMipMapGeneratorSettings settings, AlphaFilterMode shaderAlphaMode, int mip, int mipWidth, int mipHeight,
        int prevWidth, int prevHeight, int[] coverageChannels, int groupsX, int groupsY)
    {
        if (scaleRt == null)
            scaleRt = rt;
        bool useScaleRt = scaleRt != rt;

        int coverageCount = 0;
        if (shaderAlphaMode == AlphaFilterMode.PreserveCoverage)
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

            shader.SetInt("_CoverageChannel", coverageChannel);
            shader.SetInt("_SubsampleN", SubsampleN);
            shader.SetBuffer(kernels.Stats, "_AlphaHist", histBuffer);
            shader.SetBuffer(kernels.Stats, "_AlphaCoverage", coverageBuffer);
            shader.SetInt("_AlphaStatsMode", 1);
            shader.SetTexture(kernels.Stats, "_MipTex", rt);
            shader.SetInt("_MipLevel", mip);
            shader.SetInt("_DstWidth", mipWidth);
            shader.SetInt("_DstHeight", mipHeight);
            shader.SetFloat("_AlphaClip", settings.alphaClip);

            shader.Dispatch(kernels.Stats, groupsX, groupsY, 1);

            coverageData[0] = 0;
            coverageBuffer.SetData(coverageData);
            shader.SetInt("_AlphaStatsMode", 2);
            shader.SetTexture(kernels.Stats, "_MipTex", prevRt);
            shader.SetInt("_MipLevel", 0);
            shader.SetInt("_DstWidth", prevWidth);
            shader.SetInt("_DstHeight", prevHeight);
            int coverageGroupsX = (prevWidth + ThreadGroupSize - 1) / ThreadGroupSize;
            int coverageGroupsY = (prevHeight + ThreadGroupSize - 1) / ThreadGroupSize;
            shader.Dispatch(kernels.Stats, coverageGroupsX, coverageGroupsY, 1);

            histBuffer.GetData(histData);
            coverageBuffer.GetData(coverageData);

            int coveragePixels = Mathf.Max(1, (prevWidth - 1) * (prevHeight - 1));
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
            shader.SetTexture(kernels.Scale, "_Result", scaleRt, mip);
            shader.SetInt("_MipLevel", mip);
            shader.SetInt("_DstWidth", mipWidth);
            shader.SetInt("_DstHeight", mipHeight);
            shader.Dispatch(kernels.Scale, groupsX, groupsY, 1);

            if (useScaleRt)
            {
                shader.SetTexture(kernels.Copy, "_MipTex", scaleRt);
                shader.SetTexture(kernels.Copy, "_Result", rt, mip);
                shader.SetInt("_MipLevel", mip);
                shader.SetInt("_DstWidth", mipWidth);
                shader.SetInt("_DstHeight", mipHeight);
                shader.Dispatch(kernels.Copy, groupsX, groupsY, 1);
            }
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


    private static Texture2D ReadbackTexture(RenderTexture rt, int width, int height, int mipCount, bool isLinear)
    {
        var mipTexture = new Texture2D(width, height, TextureFormat.RGBA32, mipCount, isLinear);
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

    private static void ApplyAlphaErrorDiffusion(Texture2D texture, int mipCount, float alphaClip, float ditherNoise)
    {
        if (texture == null)
            return;

        float clip = Mathf.Clamp01(alphaClip);
        float noise = Mathf.Max(0f, ditherNoise);
        for (int mip = 0; mip < mipCount; mip++)
        {
            int width = Mathf.Max(1, texture.width >> mip);
            int height = Mathf.Max(1, texture.height >> mip);
            var pixels = texture.GetPixels32(mip);
            if (pixels.Length == 0)
                continue;

            ApplyAlphaErrorDiffusionToPixels(pixels, width, height, clip, noise, mip);
            texture.SetPixels32(pixels, mip);
        }
    }

    private static void ApplyAlphaErrorDiffusionToPixels(Color32[] pixels, int width, int height, float alphaClip,
        float noise, int mip)
    {
        int total = width * height;
        if (total <= 0 || pixels.Length < total)
            return;

        var buffer = new float[total];
        for (int i = 0; i < total; i++)
            buffer[i] = pixels[i].a / 255f;

        if (noise > 0f)
        {
            uint seed = Hash(((uint)(mip + 1)) * 0x85ebca6bu);
            for (int i = 0; i < total; i++)
            {
                float n = (HashToUnitFloat(seed ^ (uint)i) - 0.5f) * noise;
                buffer[i] += n;
            }
        }

        const float w1 = 7f / 16f;
        const float w2 = 3f / 16f;
        const float w3 = 5f / 16f;
        const float w4 = 1f / 16f;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = row + x;
                float oldVal = buffer[idx];
                float newVal = oldVal >= alphaClip ? 1f : 0f;
                buffer[idx] = newVal;
                float error = oldVal - newVal;

                if (x + 1 < width)
                    buffer[idx + 1] += error * w1;

                if (y + 1 < height)
                {
                    int rowBelow = idx + width;
                    if (x > 0)
                        buffer[rowBelow - 1] += error * w2;
                    buffer[rowBelow] += error * w3;
                    if (x + 1 < width)
                        buffer[rowBelow + 1] += error * w4;
                }
            }
        }

        for (int i = 0; i < total; i++)
            pixels[i].a = buffer[i] >= 0.5f ? (byte)255 : (byte)0;
    }

    private sealed class AlphaPyramidLevel
    {
        public int Width;
        public int Height;
        public float[] Alpha;
        public int[] Capacity;
        public int[] Visibility;
    }

    private static void ApplyAlphaPyramid(Texture2D texture, int mipCount, float alphaClip)
    {
        if (texture == null)
            return;

        float clip = Mathf.Max(1e-4f, alphaClip);
        for (int mip = 0; mip < mipCount; mip++)
        {
            int width = Mathf.Max(1, texture.width >> mip);
            int height = Mathf.Max(1, texture.height >> mip);
            var pixels = texture.GetPixels32(mip);
            if (pixels.Length == 0)
                continue;

            ApplyAlphaPyramidToPixels(pixels, width, height, clip, mip);
            texture.SetPixels32(pixels, mip);
        }
    }

    private static void ApplyAlphaPyramidToPixels(Color32[] pixels, int width, int height, float alphaClip, int mip)
    {
        int total = width * height;
        if (total <= 0)
            return;

        var alpha = new float[total];
        var capacity = new int[total];
        float sumAlpha = 0f;
        for (int i = 0; i < total; i++)
        {
            float a = pixels[i].a / 255f;
            alpha[i] = a;
            capacity[i] = 1;
            sumAlpha += a;
        }

        int target = Mathf.Clamp(Mathf.CeilToInt(sumAlpha / (2f * alphaClip)), 0, total);

        var levels = new System.Collections.Generic.List<AlphaPyramidLevel>(16)
        {
            new AlphaPyramidLevel
            {
                Width = width,
                Height = height,
                Alpha = alpha,
                Capacity = capacity
            }
        };

        int currentWidth = width;
        int currentHeight = height;
        while (currentWidth > 1 || currentHeight > 1)
        {
            int nextWidth = Mathf.Max(1, currentWidth / 2);
            int nextHeight = Mathf.Max(1, currentHeight / 2);
            var nextAlpha = new float[nextWidth * nextHeight];
            var nextCapacity = new int[nextWidth * nextHeight];
            var prevLevel = levels[levels.Count - 1];

            for (int y = 0; y < nextHeight; y++)
            {
                int childStartY = y * 2;
                int childCountY = (y == nextHeight - 1) ? (currentHeight - childStartY) : 2;
                for (int x = 0; x < nextWidth; x++)
                {
                    int childStartX = x * 2;
                    int childCountX = (x == nextWidth - 1) ? (currentWidth - childStartX) : 2;
                    float sum = 0f;
                    int capSum = 0;
                    for (int cy = 0; cy < childCountY; cy++)
                    {
                        int row = (childStartY + cy) * currentWidth;
                        for (int cx = 0; cx < childCountX; cx++)
                        {
                            int childIndex = row + childStartX + cx;
                            sum += prevLevel.Alpha[childIndex];
                            capSum += prevLevel.Capacity[childIndex];
                        }
                    }
                    int parentIndex = y * nextWidth + x;
                    nextAlpha[parentIndex] = sum;
                    nextCapacity[parentIndex] = capSum;
                }
            }

            levels.Add(new AlphaPyramidLevel
            {
                Width = nextWidth,
                Height = nextHeight,
                Alpha = nextAlpha,
                Capacity = nextCapacity
            });

            currentWidth = nextWidth;
            currentHeight = nextHeight;
        }

        foreach (var level in levels)
            level.Visibility = new int[level.Alpha.Length];

        var topLevel = levels[levels.Count - 1];
        if (topLevel.Visibility.Length > 0)
            topLevel.Visibility[0] = target;

        float invClip = 1f / (2f * alphaClip);
        float[] remainderBuffer = new float[9];
        int[] indexBuffer = new int[9];
        uint baseSeed = Hash(((uint)(mip + 1)) * 0x9E3779B9u);

        for (int levelIndex = levels.Count - 1; levelIndex > 0; levelIndex--)
        {
            var parent = levels[levelIndex];
            var child = levels[levelIndex - 1];
            for (int py = 0; py < parent.Height; py++)
            {
                int childStartY = py * 2;
                int childCountY = (py == parent.Height - 1) ? (child.Height - childStartY) : 2;
                for (int px = 0; px < parent.Width; px++)
                {
                    int parentIndex = py * parent.Width + px;
                    int desired = parent.Visibility[parentIndex];
                    if (desired <= 0)
                        continue;

                    int childStartX = px * 2;
                    int childCountX = (px == parent.Width - 1) ? (child.Width - childStartX) : 2;
                    uint groupSeed = Hash(baseSeed ^ (uint)parentIndex);
                    DistributeVisibility(child, childStartX, childStartY, childCountX, childCountY,
                        desired, invClip, groupSeed, remainderBuffer, indexBuffer);
                }
            }
        }

        var vis0 = levels[0].Visibility;
        for (int i = 0; i < total; i++)
            pixels[i].a = (byte)(vis0[i] > 0 ? 255 : 0);
    }

    private static void DistributeVisibility(AlphaPyramidLevel child, int childStartX, int childStartY,
        int childCountX, int childCountY, int desired, float invClip, uint seed,
        float[] remainderBuffer, int[] indexBuffer)
    {
        int childWidth = child.Width;
        var childAlpha = child.Alpha;
        var childCapacity = child.Capacity;
        var childVisibility = child.Visibility;

        int count = 0;
        int baseSum = 0;
        for (int y = 0; y < childCountY; y++)
        {
            int row = (childStartY + y) * childWidth;
            for (int x = 0; x < childCountX; x++)
            {
                int idx = row + childStartX + x;
                int capacity = Mathf.Max(1, childCapacity[idx]);
                float expected = childAlpha[idx] * invClip;
                if (expected > capacity)
                    expected = capacity;
                int baseCount = Mathf.FloorToInt(expected);
                if (baseCount > capacity)
                    baseCount = capacity;
                childVisibility[idx] = baseCount;
                baseSum += baseCount;
                remainderBuffer[count] = expected - baseCount;
                indexBuffer[count] = idx;
                count++;
            }
        }

        int leftover = desired - baseSum;
        if (leftover > 0)
        {
            for (int i = 0; i < leftover; i++)
            {
                int best = -1;
                float bestRem = -1f;
                uint bestHash = 0;
                for (int c = 0; c < count; c++)
                {
                    int idx = indexBuffer[c];
                    if (childVisibility[idx] >= childCapacity[idx])
                        continue;
                    float rem = remainderBuffer[c];
                    if (rem < 0f)
                        continue;
                    uint h = Hash(seed ^ (uint)idx);
                    if (rem > bestRem || (Mathf.Abs(rem - bestRem) < 1e-6f && h > bestHash))
                    {
                        bestRem = rem;
                        bestHash = h;
                        best = c;
                    }
                }
                if (best < 0)
                    break;
                childVisibility[indexBuffer[best]] += 1;
                remainderBuffer[best] = -1f;
            }
        }
        else if (leftover < 0)
        {
            int remove = -leftover;
            for (int i = 0; i < remove; i++)
            {
                int worst = -1;
                float worstRem = float.MaxValue;
                uint worstHash = 0;
                for (int c = 0; c < count; c++)
                {
                    int idx = indexBuffer[c];
                    if (childVisibility[idx] <= 0)
                        continue;
                    float rem = remainderBuffer[c];
                    uint h = Hash(seed ^ (uint)idx);
                    if (rem < worstRem || (Mathf.Abs(rem - worstRem) < 1e-6f && h > worstHash))
                    {
                        worstRem = rem;
                        worstHash = h;
                        worst = c;
                    }
                }
                if (worst < 0)
                    break;
                childVisibility[indexBuffer[worst]] -= 1;
            }
        }
    }

    private static uint Hash(uint x)
    {
        unchecked
        {
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }

    private static float HashToUnitFloat(uint x)
    {
        return (Hash(x) & 0x00FFFFFF) / 16777216f;
    }

    private static int ChannelFilterToShader(ChannelFilter filter)
    {
        return filter == ChannelFilter.PreserveCoverage ? (int)ChannelFilter.WeightedAverage : (int)filter;
    }
}
}
