using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomMipMapGeneratorWindow : EditorWindow
{
    private enum AlphaFilterMode { None, PreserveCoverage, MaxFilter }
    private enum FilterMode { Kaiser, Ewa }
    private enum TextureKind { Color, NormalMap, DataMap }
    private enum ChannelFilter
    {
        WeightedAverage = 0,
        Min = 1,
        Max = 2,
        LinearRoughness = 3,
        LinearSmoothness = 4,
        PowerMean = 5,
        PreserveCoverage = 6
    }
    private const string ComputeShaderFileName = "CustomMipMapGenerator.compute";
    private int maxFilterRadiusMin = 1;
    private int maxFilterRadiusMax = 1;
    private int maxFilterStepSize = 1;
    private int fullResMipCount = 2;
    
    private Texture2D sourceTexture;
    private TextureKind textureKind = TextureKind.Color;
    private FilterMode filterMode = FilterMode.Kaiser;
    private bool edgeAware;
    private float edgeSigma = 0.12f;
    private float ewaSigma = 1.0f;
    private bool sharpenEnabled = true;
    private float sharpenStrength = 0.2f;
    private float sharpenClamp = 0.05f;
    private int sharpenMipCount = 3;
    private bool sharpenNormals;
    private bool toksvigInAlpha;
    private TextureFormat compression = TextureFormat.ASTC_6x6;
    private float kaiserBeta = 6f;
    private float baseRadius = 3f;
    private bool usePerChannelFilter;
    private ChannelFilter channelFilterR = ChannelFilter.WeightedAverage;
    private ChannelFilter channelFilterG = ChannelFilter.WeightedAverage;
    private ChannelFilter channelFilterB = ChannelFilter.WeightedAverage;
    private ChannelFilter channelFilterA = ChannelFilter.WeightedAverage;
    private float channelPower = 2.0f;
    private AlphaFilterMode alphaFilterMode = AlphaFilterMode.None;
    private AlphaFilterMode savedAlphaFilterMode = AlphaFilterMode.None;
    private bool hasSavedAlphaFilterMode;
    private AlphaFilterMode savedAlphaFilterModeForData = AlphaFilterMode.None;
    private bool hasSavedAlphaFilterModeForData;
    private float alphaClip = 0.5f;
    private static readonly GUIContent TextureLabel = new GUIContent("Texture", "Source texture to generate custom mip maps for.");
    private static readonly GUIContent FilterModeLabel = new GUIContent(
        "Filter Mode",
        "Kaiser = sharper, more detail, more ringing risk. EWA = smoother, less ringing and moire on diagonals/anisotropic patterns.");
    private static readonly GUIContent KaiserBetaLabel = new GUIContent("Kaiser Beta", "Sharpness vs ringing. Higher = sharper but more ringing.");
    private static readonly GUIContent KaiserRadiusLabel = new GUIContent("Kaiser Radius", "Filter radius in texels. Higher = smoother but blurrier.");
    private static readonly GUIContent EdgeAwareLabel = new GUIContent("Edge-Aware", "Reduce color bleeding across edges by lowering weights across luminance changes.");
    private static readonly GUIContent EdgeSigmaLabel = new GUIContent("Edge Sigma", "Edge sensitivity. Lower values preserve edges more aggressively.");
    private static readonly GUIContent EwaSigmaLabel = new GUIContent("EWA Sigma", "Elliptical Gaussian radius in texels. Higher = smoother.");
    private static readonly GUIContent FullResMipsLabel = new GUIContent("Full-Res Mips", "Number of mip levels generated from full-res source before switching to previous-mip source.");
    private static readonly GUIContent TextureTypeLabel = new GUIContent("Texture Type", "Color = sRGB color. Normal Map = normal renormalization + Toksvig. Packed/Data = linear masks/roughness/AO/height.");
    private static readonly GUIContent ToksvigLabel = new GUIContent("Toksvig In Alpha", "Store normal length in alpha for Toksvig roughness. Requires shader support.");
    private static readonly GUIContent SharpenLabel = new GUIContent("Sharpen", "Apply unsharp filter to the first N mips.");
    private static readonly GUIContent SharpenStrengthLabel = new GUIContent("Sharpen Strength", "Sharpen amount. Keep low to avoid halos.");
    private static readonly GUIContent SharpenClampLabel = new GUIContent("Sharpen Clamp", "Clamp overshoot to limit ringing.");
    private static readonly GUIContent SharpenMipsLabel = new GUIContent("Sharpen Mips", "Number of mip levels to sharpen.");
    private static readonly GUIContent SharpenNormalsLabel = new GUIContent("Sharpen Normals", "Apply sharpening to normal maps.");
    private static readonly GUIContent AlphaFilterModeLabel = new GUIContent("Alpha Filter Mode", "None = filter alpha normally. PreserveCoverage = keep alpha-clip coverage. MaxFilter = dilate alpha.");
    private static readonly GUIContent AlphaClipLabel = new GUIContent("Alpha Clip", "Alpha threshold used for coverage preservation.");
    private static readonly GUIContent MaxFilterMinLabel = new GUIContent("Min Radius", "Minimum dilation radius for MaxFilter alpha.");
    private static readonly GUIContent MaxFilterMaxLabel = new GUIContent("Max Radius", "Maximum dilation radius for MaxFilter alpha.");
    private static readonly GUIContent MaxFilterStepLabel = new GUIContent("Increase Every N Mip Levels", "Increase dilation radius after every N mip levels.");
    private static readonly GUIContent CompressionLabel = new GUIContent("Compression", "Final texture compression format.");
    private static readonly GUIContent PerChannelFilterLabel = new GUIContent("Per-Channel Filters", "Override filter per channel (Average/Min/Max/LinearRoughness/LinearSmoothness/PowerMean/PreserveCoverage).");
    private static readonly GUIContent ChannelFilterRLabel = new GUIContent("R");
    private static readonly GUIContent ChannelFilterGLabel = new GUIContent("G");
    private static readonly GUIContent ChannelFilterBLabel = new GUIContent("B");
    private static readonly GUIContent ChannelFilterALabel = new GUIContent("A");
    private static readonly GUIContent ChannelPowerLabel = new GUIContent("Power Mean Exponent", "p < 1 biases darker (AO), p > 1 biases brighter. 1 = average.");
    private const string ToksvigHelpText =
        "MY CUSTOM SHADER APROACH\n" +
        "// 1) Семплим нормаль (альфа = |Na| из твоих кастомных мипов)\n" +
        "float4 nTex = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv);\n" +
        "float3 N_ts = UnpackNormal(nTex);\n" +
        "float  tok  = saturate(nTex.a);            // это |Na|, НЕ Toksvig factor\n" +
        "\n" +
        "// 2) Семплим roughness как обычно\n" +
        "float rough = CustomSurfaceData.roughness; // 0..1 perceptual roughness\n" +
        "\n" +
        "// 3) Toksvig/variance -> boost roughness (в домене a2=r^4, потому что твой BRDF так работает)\n" +
        "float m = max(tok, 1e-3);\n" +
        "float sigma2 = (1.0 - m) / m;\n" +
        "\n" +
        "// твой GGX использует a2 = r^4\n" +
        "float a2 = rough * rough; a2 *= a2;        // r^4\n" +
        "a2 = saturate(a2 + _ToksvigStrength * sigma2);\n" +
        "\n" +
        "rough = pow(a2, 0.25);                     // вернуть r так, чтобы Pow4(r)==a2\n" +
        "\n" +
        "// дальше используй `rough` как обычно в GGX";
    private const string ToksvigAlphaWarning =
        "Toksvig uses alpha. Selected compression may drop alpha (use ASTC/BC7/RGBA).";
    private const string ToksvigAlphaFilterWarning =
        "Toksvig uses alpha. Alpha filter is forced to None while enabled.";
    private const string DataMapHelpText =
        "Packed/Data Map is treated as linear data (no sRGB/gamma correction). Use per-channel Preserve Coverage for cutout masks.";
    private const string PerChannelHelpText =
        "Average = weighted filter.\n" +
        "Min/Max = choose extreme in kernel.\n" +
        "Linear Roughness = sqrt(mean(r^2)).\n" +
        "Linear Smoothness = 1 - sqrt(mean((1-s)^2)).\n" +
        "Power Mean = (mean(x^p))^(1/p), p < 1 biases dark, p > 1 biases bright.\n" +
        "Preserve Coverage = scale channel to keep coverage at Alpha Clip.\n" +
        "Metallic: Average for blends, Power Mean (p>1) or Max to keep metal specks.";
    private const string NormalPackingWarning =
        "This generator outputs raw RGB normals (xyz in RGB). Use tex.rgb*2-1 in shader; do not use Unity normal decoding.";

    [MenuItem("Tools/Custom MipMap Generator")]
    public static void ShowWindow()
    {
        GetWindow<CustomMipMapGeneratorWindow>("Custom MipMap Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Custom MipMap Generator", EditorStyles.boldLabel);
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField(TextureLabel, sourceTexture, typeof(Texture2D), false);
        bool wasNormalMap = textureKind == TextureKind.NormalMap;
        bool wasDataMap = textureKind == TextureKind.DataMap;
        textureKind = (TextureKind)EditorGUILayout.EnumPopup(TextureTypeLabel, textureKind);
        bool isNormalMap = textureKind == TextureKind.NormalMap;
        bool isDataMap = textureKind == TextureKind.DataMap;
        bool showToksvigHelp = false;
        GUILayout.Label($"sRGB Texture: {!isNormalMap && !isDataMap}");
        filterMode = (FilterMode)EditorGUILayout.EnumPopup(FilterModeLabel, filterMode);
        if (filterMode == FilterMode.Kaiser)
        {
            kaiserBeta = EditorGUILayout.Slider(KaiserBetaLabel, kaiserBeta, 1f, 20f);
            baseRadius = EditorGUILayout.Slider(KaiserRadiusLabel, baseRadius, 1f, 6f);
        }
        else
        {
            ewaSigma = EditorGUILayout.Slider(EwaSigmaLabel, ewaSigma, 0.5f, 3f);
        }
        edgeAware = EditorGUILayout.Toggle(EdgeAwareLabel, edgeAware);
        if (edgeAware)
            edgeSigma = EditorGUILayout.Slider(EdgeSigmaLabel, edgeSigma, 0.01f, 0.5f);
        if (isDataMap)
            EditorGUILayout.HelpBox(DataMapHelpText, MessageType.Info);
        if (!isDataMap && wasDataMap && hasSavedAlphaFilterModeForData)
        {
            alphaFilterMode = savedAlphaFilterModeForData;
            hasSavedAlphaFilterModeForData = false;
        }
        if (isDataMap && !wasDataMap)
        {
            savedAlphaFilterModeForData = alphaFilterMode;
            hasSavedAlphaFilterModeForData = true;
            alphaFilterMode = AlphaFilterMode.None;
        }
        if (isDataMap)
            alphaFilterMode = AlphaFilterMode.None;
        if (!isNormalMap && wasNormalMap && toksvigInAlpha)
        {
            toksvigInAlpha = false;
            if (hasSavedAlphaFilterMode)
            {
                alphaFilterMode = savedAlphaFilterMode;
                hasSavedAlphaFilterMode = false;
            }
        }
        if (isNormalMap)
        {
            EditorGUILayout.HelpBox(NormalPackingWarning, MessageType.Warning);
            bool wasToksvig = toksvigInAlpha;
            toksvigInAlpha = EditorGUILayout.Toggle(ToksvigLabel, toksvigInAlpha);
            if (toksvigInAlpha && !wasToksvig)
            {
                savedAlphaFilterMode = alphaFilterMode;
                hasSavedAlphaFilterMode = true;
                alphaFilterMode = AlphaFilterMode.None;
            }
            else if (!toksvigInAlpha && wasToksvig && hasSavedAlphaFilterMode)
            {
                alphaFilterMode = savedAlphaFilterMode;
                hasSavedAlphaFilterMode = false;
            }
            if (toksvigInAlpha)
            {
                if (!CompressionHasAlpha(compression))
                    EditorGUILayout.HelpBox(ToksvigAlphaWarning, MessageType.Warning);
                showToksvigHelp = true;
            }
        }
        bool toksvigActive = isNormalMap && toksvigInAlpha;
        int maxFullResMipCount = GetMaxFullResMipCount();
        fullResMipCount = Mathf.Clamp(fullResMipCount, 0, maxFullResMipCount);
        fullResMipCount = EditorGUILayout.IntSlider(FullResMipsLabel, fullResMipCount, 0, maxFullResMipCount);
        sharpenEnabled = EditorGUILayout.Toggle(SharpenLabel, sharpenEnabled);
        if (sharpenEnabled)
        {
            sharpenStrength = EditorGUILayout.Slider(SharpenStrengthLabel, sharpenStrength, 0f, 1f);
            sharpenClamp = EditorGUILayout.Slider(SharpenClampLabel, sharpenClamp, 0f, 0.2f);
            sharpenMipCount = EditorGUILayout.IntSlider(SharpenMipsLabel, sharpenMipCount, 1, 6);
            EditorGUI.BeginDisabledGroup(!isNormalMap);
            sharpenNormals = EditorGUILayout.Toggle(SharpenNormalsLabel, sharpenNormals);
            EditorGUI.EndDisabledGroup();
        }
        if (!isDataMap)
        {
            EditorGUI.BeginDisabledGroup(toksvigActive);
            alphaFilterMode = (AlphaFilterMode)EditorGUILayout.EnumPopup(AlphaFilterModeLabel, alphaFilterMode);
            EditorGUI.EndDisabledGroup();
            if (toksvigActive)
                EditorGUILayout.HelpBox(ToksvigAlphaFilterWarning, MessageType.Info);
            if (alphaFilterMode == AlphaFilterMode.PreserveCoverage)
                alphaClip = EditorGUILayout.Slider(AlphaClipLabel, alphaClip, 0f, 1f);
      
            if (alphaFilterMode == AlphaFilterMode.MaxFilter)
            {
                GUILayout.Label("Auto Max Filter Radius", EditorStyles.boldLabel);
                maxFilterRadiusMin = EditorGUILayout.IntSlider(MaxFilterMinLabel, maxFilterRadiusMin, 1, 4);
                maxFilterRadiusMax = EditorGUILayout.IntSlider(MaxFilterMaxLabel, maxFilterRadiusMax, maxFilterRadiusMin, 8);
                maxFilterStepSize = EditorGUILayout.IntSlider(MaxFilterStepLabel, maxFilterStepSize, 1, 4);
            }
        }
        if (!isDataMap)
            EditorGUILayout.HelpBox("Per-channel filters are available for Packed/Data maps.", MessageType.Info);
        EditorGUI.BeginDisabledGroup(!isDataMap);
        usePerChannelFilter = EditorGUILayout.Toggle(PerChannelFilterLabel, usePerChannelFilter);
        EditorGUI.EndDisabledGroup();
        if (usePerChannelFilter && isDataMap)
        {
            EditorGUILayout.HelpBox(PerChannelHelpText, MessageType.Info);
            EditorGUI.indentLevel++;
            channelFilterR = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterRLabel, channelFilterR);
            channelFilterG = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterGLabel, channelFilterG);
            channelFilterB = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterBLabel, channelFilterB);
            channelFilterA = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterALabel, channelFilterA);
            bool usesPowerMean = channelFilterR == ChannelFilter.PowerMean
                || channelFilterG == ChannelFilter.PowerMean
                || channelFilterB == ChannelFilter.PowerMean
                || channelFilterA == ChannelFilter.PowerMean;
            if (usesPowerMean)
                channelPower = EditorGUILayout.Slider(ChannelPowerLabel, channelPower, 0.25f, 8f);
            bool usesPreserveCoverage = channelFilterR == ChannelFilter.PreserveCoverage
                || channelFilterG == ChannelFilter.PreserveCoverage
                || channelFilterB == ChannelFilter.PreserveCoverage
                || channelFilterA == ChannelFilter.PreserveCoverage;
            if (usesPreserveCoverage)
                alphaClip = EditorGUILayout.Slider(AlphaClipLabel, alphaClip, 0f, 1f);
            EditorGUI.indentLevel--;
            if (alphaFilterMode != AlphaFilterMode.None)
                EditorGUILayout.HelpBox("Alpha filter mode overrides the A channel filter.", MessageType.Info);
        }
        compression = (TextureFormat)EditorGUILayout.EnumPopup(CompressionLabel, compression);
        if (showToksvigHelp)
            EditorGUILayout.HelpBox(ToksvigHelpText, MessageType.Info);
        GUILayout.Space(20);
        if (sourceTexture != null && GUILayout.Button("Generate"))
            GenerateCustomMipMaps();
    }

    private void GenerateCustomMipMaps()
    {
        GenerateCustomMipMapsGpu();
    }

    private void GenerateCustomMipMapsGpu()
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Compute shaders are not supported on this system.");
            return;
        }

        var shaderPath = GetComputeShaderPath();
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
        if (shader == null)
        {
            Debug.LogError($"Compute shader not found at {shaderPath}.");
            return;
        }

        var path = AssetDatabase.GetAssetPath(sourceTexture);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        var width = sourceTexture.width;
        var height = sourceTexture.height;
        bool isNormalMap = textureKind == TextureKind.NormalMap;
        bool isDataMap = textureKind == TextureKind.DataMap;
        var mipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2)) + 1;
        bool toksvigActive = isNormalMap && toksvigInAlpha;
        int maxFullRes = Mathf.Min(6, Mathf.Max(0, mipCount - 1));
        var clampedFullResMipCount = Mathf.Clamp(fullResMipCount, 0, maxFullRes);
        var shouldGammaCorrect = !isNormalMap && !isDataMap && PlayerSettings.colorSpace == ColorSpace.Gamma;
        var effectiveAlphaMode = toksvigActive ? AlphaFilterMode.None : alphaFilterMode;
        bool perChannelActive = usePerChannelFilter && isDataMap;
        bool perChannelCoverage = perChannelActive && (channelFilterR == ChannelFilter.PreserveCoverage
            || channelFilterG == ChannelFilter.PreserveCoverage
            || channelFilterB == ChannelFilter.PreserveCoverage
            || channelFilterA == ChannelFilter.PreserveCoverage);
        bool doPreserveCoverage = effectiveAlphaMode == AlphaFilterMode.PreserveCoverage || perChannelCoverage;
        int channelFilterRValue = perChannelActive ? ChannelFilterToShader(channelFilterR) : 0;
        int channelFilterGValue = perChannelActive ? ChannelFilterToShader(channelFilterG) : 0;
        int channelFilterBValue = perChannelActive ? ChannelFilterToShader(channelFilterB) : 0;
        int channelFilterAValue = (perChannelActive && effectiveAlphaMode == AlphaFilterMode.None)
            ? ChannelFilterToShader(channelFilterA)
            : 0;

        RenderTexture rt = null;
        RenderTexture rtSharpen = null;
        ComputeBuffer histBuffer = null;
        ComputeBuffer coverageBuffer = null;

        try
        {
            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                useMipMap = true,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = UnityEngine.FilterMode.Point
            };
            rt.Create();

            bool sharpenActive = sharpenEnabled && (sharpenNormals || !isNormalMap);
            int maxSharpenMip = sharpenActive && mipCount > 1 ? Mathf.Clamp(sharpenMipCount, 1, mipCount - 1) : 0;
            if (sharpenActive)
            {
                rtSharpen = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    useMipMap = true,
                    autoGenerateMips = false,
                    enableRandomWrite = true,
                    filterMode = UnityEngine.FilterMode.Point
                };
                rtSharpen.Create();
            }
            var prevActive = RenderTexture.active;
            Graphics.Blit(sourceTexture, rt);
            RenderTexture.active = prevActive;

            const int threadGroupSize = 8;
            const int histBins = 256;
            const int subsampleN = 4;

            uint[] histData = null;
            uint[] coverageData = null;
            if (doPreserveCoverage)
            {
                histBuffer = new ComputeBuffer(histBins, sizeof(uint));
                coverageBuffer = new ComputeBuffer(1, sizeof(uint));
                histData = new uint[histBins];
                coverageData = new uint[1];
            }

            int generateKernel = shader.FindKernel("GenerateMip");
            int statsKernel = shader.FindKernel("ComputeAlphaStats");
            int scaleKernel = shader.FindKernel("ApplyAlphaScale");
            int sharpenKernel = shader.FindKernel("SharpenMip");
            int copyKernel = shader.FindKernel("CopyMip");
            int prepareKernel = shader.FindKernel("PrepareToksvigBase");
            int[] coverageChannels = new int[4];

            if (toksvigActive)
            {
                shader.SetInt("_DstWidth", width);
                shader.SetInt("_DstHeight", height);
                shader.SetInt("_ToksvigInAlpha", 1);
                shader.SetTexture(prepareKernel, "_SrcTex", sourceTexture);
                shader.SetTexture(prepareKernel, "_Result", rt, 0);
                int baseGroupsX = (width + threadGroupSize - 1) / threadGroupSize;
                int baseGroupsY = (height + threadGroupSize - 1) / threadGroupSize;
                shader.Dispatch(prepareKernel, baseGroupsX, baseGroupsY, 1);
            }

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

                shader.SetInt("_DstWidth", mipWidth);
                shader.SetInt("_DstHeight", mipHeight);
                shader.SetInt("_SrcWidth", srcWidth);
                shader.SetInt("_SrcHeight", srcHeight);
                shader.SetInt("_PrevWidth", prevWidth);
                shader.SetInt("_PrevHeight", prevHeight);
                shader.SetFloat("_WRatio", wratio);
                shader.SetFloat("_HRatio", hratio);
                shader.SetInt("_UsePrev", usePrev ? 1 : 0);
                shader.SetInt("_FilterMode", filterMode == FilterMode.Kaiser ? 0 : 1);
                shader.SetInt("_AlphaMode", (int)effectiveAlphaMode);
                shader.SetInt("_ChannelFilterR", channelFilterRValue);
                shader.SetInt("_ChannelFilterG", channelFilterGValue);
                shader.SetInt("_ChannelFilterB", channelFilterBValue);
                shader.SetInt("_ChannelFilterA", channelFilterAValue);
                shader.SetFloat("_ChannelPower", channelPower);
                shader.SetFloat("_KaiserBeta", kaiserBeta);
                shader.SetFloat("_BaseRadius", baseRadius);
                shader.SetFloat("_EwaSigma", ewaSigma);
                shader.SetInt("_EdgeAware", (!isNormalMap && edgeAware) ? 1 : 0);
                shader.SetFloat("_EdgeSigma", edgeSigma);
                shader.SetFloat("_AlphaClip", alphaClip);
                shader.SetInt("_NormalMap", isNormalMap ? 1 : 0);
                shader.SetInt("_GammaCorrect", shouldGammaCorrect ? 1 : 0);
                shader.SetInt("_ToksvigInAlpha", (isNormalMap && toksvigInAlpha) ? 1 : 0);
                shader.SetInt("_MipLevel", mip);
                shader.SetInt("_PrevMipLevel", mip - 1);
                shader.SetInt("_MaxFilterRadiusMin", maxFilterRadiusMin);
                shader.SetInt("_MaxFilterRadiusMax", maxFilterRadiusMax);
                shader.SetInt("_MaxFilterStepSize", maxFilterStepSize);
                int maxFilterRadius = Mathf.Clamp(maxFilterRadiusMin + mip / Mathf.Max(1, maxFilterStepSize), maxFilterRadiusMin, maxFilterRadiusMax);
                shader.SetInt("_MaxFilterRadius", maxFilterRadius);

                shader.SetTexture(generateKernel, "_SrcTex", sourceTexture);
                shader.SetTexture(generateKernel, "_MipTex", rt);
                shader.SetTexture(generateKernel, "_Result", rt, mip);

                int groupsX = (mipWidth + threadGroupSize - 1) / threadGroupSize;
                int groupsY = (mipHeight + threadGroupSize - 1) / threadGroupSize;
                shader.Dispatch(generateKernel, groupsX, groupsY, 1);

                if (doPreserveCoverage)
                {
                    int coverageCount = 0;
                    if (effectiveAlphaMode == AlphaFilterMode.PreserveCoverage)
                    {
                        coverageChannels[coverageCount++] = 3;
                    }
                    else
                    {
                        if (channelFilterR == ChannelFilter.PreserveCoverage)
                            coverageChannels[coverageCount++] = 0;
                        if (channelFilterG == ChannelFilter.PreserveCoverage)
                            coverageChannels[coverageCount++] = 1;
                        if (channelFilterB == ChannelFilter.PreserveCoverage)
                            coverageChannels[coverageCount++] = 2;
                        if (channelFilterA == ChannelFilter.PreserveCoverage)
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
                        shader.SetInt("_SubsampleN", subsampleN);
                        shader.SetBuffer(statsKernel, "_AlphaHist", histBuffer);
                        shader.SetBuffer(statsKernel, "_AlphaCoverage", coverageBuffer);
                        shader.SetTexture(statsKernel, "_MipTex", rt);
                        shader.SetInt("_MipLevel", mip);
                        shader.SetInt("_DstWidth", mipWidth);
                        shader.SetInt("_DstHeight", mipHeight);
                        shader.SetFloat("_AlphaClip", alphaClip);

                        shader.Dispatch(statsKernel, groupsX, groupsY, 1);

                        histBuffer.GetData(histData);
                        coverageBuffer.GetData(coverageData);

                        int coveragePixels = Mathf.Max(1, (mipWidth - 1) * (mipHeight - 1));
                        float targetCoverage = (float)coverageData[0] / (coveragePixels * subsampleN * subsampleN);
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
                            for (int bin = histBins - 1; bin >= 0; bin--)
                            {
                                cumulative += (int)histData[bin];
                                if (cumulative >= targetCount)
                                {
                                    thresholdBin = bin;
                                    break;
                                }
                            }
                            float threshold = thresholdBin / 255f;
                            scale = alphaClip / Mathf.Max(threshold, 1f / 255f);
                            scale = Mathf.Clamp(scale, 0f, 4f);
                        }

                        shader.SetInt("_CoverageChannel", coverageChannel);
                        shader.SetFloat("_AlphaScale", scale);
                        shader.SetTexture(scaleKernel, "_MipTex", rt);
                        shader.SetTexture(scaleKernel, "_Result", rt, mip);
                        shader.SetInt("_MipLevel", mip);
                        shader.SetInt("_DstWidth", mipWidth);
                        shader.SetInt("_DstHeight", mipHeight);
                        shader.Dispatch(scaleKernel, groupsX, groupsY, 1);
                    }
                }

                if (sharpenActive && mip <= maxSharpenMip)
                {
                    shader.SetFloat("_SharpenStrength", sharpenStrength);
                    shader.SetFloat("_SharpenClamp", sharpenClamp);
                    shader.SetTexture(sharpenKernel, "_MipTex", rt);
                    shader.SetTexture(sharpenKernel, "_Result", rtSharpen, mip);
                    shader.SetInt("_MipLevel", mip);
                    shader.SetInt("_DstWidth", mipWidth);
                    shader.SetInt("_DstHeight", mipHeight);
                    shader.Dispatch(sharpenKernel, groupsX, groupsY, 1);
                    shader.SetTexture(copyKernel, "_MipTex", rtSharpen);
                    shader.SetTexture(copyKernel, "_Result", rt, mip);
                    shader.SetInt("_MipLevel", mip);
                    shader.SetInt("_DstWidth", mipWidth);
                    shader.SetInt("_DstHeight", mipHeight);
                    shader.Dispatch(copyKernel, groupsX, groupsY, 1);
                }
            }

            var mipTexture = new Texture2D(width, height, TextureFormat.RGBA32, mipCount, isNormalMap);
            for (int mip = 0; mip < mipCount; mip++)
            {
                var request = AsyncGPUReadback.Request(rt, mip, TextureFormat.RGBA32);
                request.WaitForCompletion();
                if (request.hasError)
                {
                    Debug.LogError("GPU readback failed.");
                    Object.DestroyImmediate(mipTexture);
                    return;
                }
                mipTexture.SetPixelData(request.GetData<Color32>(), mip);
            }

            mipTexture.Apply(false, false);
            EditorUtility.CompressTexture(mipTexture, compression, TextureCompressionQuality.Best);

            var newPath = Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(path) + "_customMips.asset";
            AssetDatabase.CreateAsset(mipTexture, newPath);
            AssetDatabase.SaveAssets();

            importer.isReadable = false;
            importer.sRGBTexture = !isNormalMap && !isDataMap;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

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
            EditorUtility.ClearProgressBar();
        }
    }

    private int GetMaxFullResMipCount()
    {
        if (sourceTexture == null)
            return 6;

        int maxDim = Mathf.Max(sourceTexture.width, sourceTexture.height);
        if (maxDim <= 0)
            return 0;

        int mipCount = Mathf.FloorToInt(Mathf.Log(maxDim, 2)) + 1;
        return Mathf.Min(6, Mathf.Max(0, mipCount - 1));
    }

    private static int ChannelFilterToShader(ChannelFilter filter)
    {
        return filter == ChannelFilter.PreserveCoverage ? (int)ChannelFilter.WeightedAverage : (int)filter;
    }

    private string GetComputeShaderPath()
    {
        var script = MonoScript.FromScriptableObject(this);
        var scriptPath = AssetDatabase.GetAssetPath(script);
        var dir = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrEmpty(dir))
            return ComputeShaderFileName;
        return Path.Combine(dir, ComputeShaderFileName).Replace('\\', '/');
    }

    private static bool CompressionHasAlpha(TextureFormat format)
    {
#if UNITY_2020_1_OR_NEWER
        var gfxFormat = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, false);
        if (gfxFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.None)
            return true;
        return UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(gfxFormat);
#else
        return true;
#endif
    }
    
}


