using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomMipMapGenerator
{
public class CustomMipMapGeneratorWindow : EditorWindow
{
    private const string ComputeShaderFileName = "CustomMipMapGenerator.compute";
    private Texture2D sourceTexture;
    private CustomMipMapGeneratorSettings settings = new CustomMipMapGeneratorSettings();
    private Vector2 scrollPosition;
    private AlphaFilterMode savedAlphaFilterMode = AlphaFilterMode.None;
    private bool hasSavedAlphaFilterMode;
    private AlphaFilterMode savedAlphaFilterModeForData = AlphaFilterMode.None;
    private bool hasSavedAlphaFilterModeForData;
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
    private static readonly GUIContent MaxFullResRatioLabel = new GUIContent("Max Full-Res Ratio", "0 = no cap. Switches to previous mip if source/dest ratio exceeds this value.");
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
    private static readonly GUIContent VariantsLabel = new GUIContent("Platform Variants", "Generate platform-specific variant assets.");
    private static readonly GUIContent MobileCompressionLabel = new GUIContent("Mobile (.mobile)", "Compression format for mobile variant asset.");
    private static readonly GUIContent PcCompressionLabel = new GUIContent("PC (.pc)", "Compression format for PC variant asset.");
    private static readonly GUIContent LinuxCompressionLabel = new GUIContent("Linux (.linux)", "Compression format for Linux variant asset.");
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
    private const string VariantsHelpText =
        "Creates *_customMips.mobile.asset, *_customMips.pc.asset, and *_customMips.linux.asset for build-time swapping. Use *_customMips.pc.asset on materials.";

    [MenuItem("Tools/Custom MipMap Generator/Open Window")]
    public static void ShowWindow()
    {
        GetWindow<CustomMipMapGeneratorWindow>("Custom MipMap Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Custom MipMap Generator", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField(TextureLabel, sourceTexture, typeof(Texture2D), false);
        bool wasNormalMap = settings.textureKind == TextureKind.NormalMap;
        bool wasDataMap = settings.textureKind == TextureKind.DataMap;
        settings.textureKind = (TextureKind)EditorGUILayout.EnumPopup(TextureTypeLabel, settings.textureKind);
        bool isNormalMap = settings.textureKind == TextureKind.NormalMap;
        bool isDataMap = settings.textureKind == TextureKind.DataMap;
        bool showToksvigHelp = false;
        GUILayout.Label($"sRGB Texture: {!isNormalMap && !isDataMap}");
        settings.filterMode = (FilterMode)EditorGUILayout.EnumPopup(FilterModeLabel, settings.filterMode);
        if (settings.filterMode == FilterMode.Kaiser)
        {
            settings.kaiserBeta = EditorGUILayout.Slider(KaiserBetaLabel, settings.kaiserBeta, 1f, 20f);
            settings.baseRadius = EditorGUILayout.Slider(KaiserRadiusLabel, settings.baseRadius, 1f, 6f);
        }
        else
        {
            settings.ewaSigma = EditorGUILayout.Slider(EwaSigmaLabel, settings.ewaSigma, 0.5f, 3f);
        }
        settings.edgeAware = EditorGUILayout.Toggle(EdgeAwareLabel, settings.edgeAware);
        if (settings.edgeAware)
            settings.edgeSigma = EditorGUILayout.Slider(EdgeSigmaLabel, settings.edgeSigma, 0.01f, 0.5f);
        if (isDataMap)
            EditorGUILayout.HelpBox(DataMapHelpText, MessageType.Info);
        if (!isDataMap && wasDataMap && hasSavedAlphaFilterModeForData)
        {
            settings.alphaFilterMode = savedAlphaFilterModeForData;
            hasSavedAlphaFilterModeForData = false;
        }
        if (isDataMap && !wasDataMap)
        {
            savedAlphaFilterModeForData = settings.alphaFilterMode;
            hasSavedAlphaFilterModeForData = true;
            settings.alphaFilterMode = AlphaFilterMode.None;
        }
        if (isDataMap)
            settings.alphaFilterMode = AlphaFilterMode.None;
        if (!isNormalMap && wasNormalMap && settings.toksvigInAlpha)
        {
            settings.toksvigInAlpha = false;
            if (hasSavedAlphaFilterMode)
            {
                settings.alphaFilterMode = savedAlphaFilterMode;
                hasSavedAlphaFilterMode = false;
            }
        }
        if (isNormalMap)
        {
            EditorGUILayout.HelpBox(NormalPackingWarning, MessageType.Warning);
            bool wasToksvig = settings.toksvigInAlpha;
            settings.toksvigInAlpha = EditorGUILayout.Toggle(ToksvigLabel, settings.toksvigInAlpha);
            if (settings.toksvigInAlpha && !wasToksvig)
            {
                savedAlphaFilterMode = settings.alphaFilterMode;
                hasSavedAlphaFilterMode = true;
                settings.alphaFilterMode = AlphaFilterMode.None;
            }
            else if (!settings.toksvigInAlpha && wasToksvig && hasSavedAlphaFilterMode)
            {
                settings.alphaFilterMode = savedAlphaFilterMode;
                hasSavedAlphaFilterMode = false;
            }
            if (settings.toksvigInAlpha)
            {
                var warning = GetToksvigCompressionWarning();
                if (!string.IsNullOrEmpty(warning))
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                showToksvigHelp = true;
            }
        }
        bool toksvigActive = isNormalMap && settings.toksvigInAlpha;
        int maxFullResMipCount = GetMaxFullResMipCount();
        settings.fullResMipCount = Mathf.Clamp(settings.fullResMipCount, 0, maxFullResMipCount);
        settings.fullResMipCount = EditorGUILayout.IntSlider(FullResMipsLabel, settings.fullResMipCount, 0, maxFullResMipCount);
        settings.maxFullResRatio = EditorGUILayout.IntSlider(MaxFullResRatioLabel, settings.maxFullResRatio, 0, 64);
        settings.sharpenEnabled = EditorGUILayout.Toggle(SharpenLabel, settings.sharpenEnabled);
        if (settings.sharpenEnabled)
        {
            settings.sharpenStrength = EditorGUILayout.Slider(SharpenStrengthLabel, settings.sharpenStrength, 0f, 1f);
            settings.sharpenClamp = EditorGUILayout.Slider(SharpenClampLabel, settings.sharpenClamp, 0f, 0.2f);
            settings.sharpenMipCount = EditorGUILayout.IntSlider(SharpenMipsLabel, settings.sharpenMipCount, 1, 6);
            EditorGUI.BeginDisabledGroup(!isNormalMap);
            settings.sharpenNormals = EditorGUILayout.Toggle(SharpenNormalsLabel, settings.sharpenNormals);
            EditorGUI.EndDisabledGroup();
        }
        if (!isDataMap)
        {
            EditorGUI.BeginDisabledGroup(toksvigActive);
            settings.alphaFilterMode = (AlphaFilterMode)EditorGUILayout.EnumPopup(AlphaFilterModeLabel, settings.alphaFilterMode);
            EditorGUI.EndDisabledGroup();
            if (toksvigActive)
                EditorGUILayout.HelpBox(ToksvigAlphaFilterWarning, MessageType.Info);
            if (settings.alphaFilterMode == AlphaFilterMode.PreserveCoverage)
                settings.alphaClip = EditorGUILayout.Slider(AlphaClipLabel, settings.alphaClip, 0f, 1f);
      
            if (settings.alphaFilterMode == AlphaFilterMode.MaxFilter)
            {
                GUILayout.Label("Auto Max Filter Radius", EditorStyles.boldLabel);
                settings.maxFilterRadiusMin = EditorGUILayout.IntSlider(MaxFilterMinLabel, settings.maxFilterRadiusMin, 1, 4);
                settings.maxFilterRadiusMax = EditorGUILayout.IntSlider(MaxFilterMaxLabel, settings.maxFilterRadiusMax, settings.maxFilterRadiusMin, 8);
                settings.maxFilterStepSize = EditorGUILayout.IntSlider(MaxFilterStepLabel, settings.maxFilterStepSize, 1, 4);
            }
        }
        EditorGUI.BeginDisabledGroup(!isDataMap);
        settings.usePerChannelFilter = EditorGUILayout.Toggle(PerChannelFilterLabel, settings.usePerChannelFilter);
        EditorGUI.EndDisabledGroup();
        if (settings.usePerChannelFilter && isDataMap)
        {
            EditorGUILayout.HelpBox(PerChannelHelpText, MessageType.Info);
            EditorGUI.indentLevel++;
            settings.channelFilterR = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterRLabel, settings.channelFilterR);
            settings.channelFilterG = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterGLabel, settings.channelFilterG);
            settings.channelFilterB = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterBLabel, settings.channelFilterB);
            settings.channelFilterA = (ChannelFilter)EditorGUILayout.EnumPopup(ChannelFilterALabel, settings.channelFilterA);
            bool usesPowerMean = settings.channelFilterR == ChannelFilter.PowerMean
                || settings.channelFilterG == ChannelFilter.PowerMean
                || settings.channelFilterB == ChannelFilter.PowerMean
                || settings.channelFilterA == ChannelFilter.PowerMean;
            if (usesPowerMean)
                settings.channelPower = EditorGUILayout.Slider(ChannelPowerLabel, settings.channelPower, 0.25f, 8f);
            bool usesPreserveCoverage = settings.channelFilterR == ChannelFilter.PreserveCoverage
                || settings.channelFilterG == ChannelFilter.PreserveCoverage
                || settings.channelFilterB == ChannelFilter.PreserveCoverage
                || settings.channelFilterA == ChannelFilter.PreserveCoverage;
            if (usesPreserveCoverage)
                settings.alphaClip = EditorGUILayout.Slider(AlphaClipLabel, settings.alphaClip, 0f, 1f);
            EditorGUI.indentLevel--;
            if (settings.alphaFilterMode != AlphaFilterMode.None)
                EditorGUILayout.HelpBox("Alpha filter mode overrides the A channel filter.", MessageType.Info);
        }
        GUILayout.Space(6);
        GUILayout.Label(VariantsLabel, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(VariantsHelpText, MessageType.Info);
        settings.compressionMobile = (TextureFormat)EditorGUILayout.EnumPopup(MobileCompressionLabel, settings.compressionMobile);
        settings.compressionPc = (TextureFormat)EditorGUILayout.EnumPopup(PcCompressionLabel, settings.compressionPc);
        settings.compressionLinux = (TextureFormat)EditorGUILayout.EnumPopup(LinuxCompressionLabel, settings.compressionLinux);
        if (showToksvigHelp)
            EditorGUILayout.HelpBox(ToksvigHelpText, MessageType.Info);
        GUILayout.Space(20);
        if (sourceTexture != null)
        {
            if (GUILayout.Button("Generate Mobile Variant (.mobile)"))
                GenerateVariantMipMaps(settings.compressionMobile, ".mobile");
            if (GUILayout.Button("Generate PC Variant (.pc)"))
                GeneratePcVariant();
            if (GUILayout.Button("Generate Linux Variant (.linux)"))
                GenerateVariantMipMaps(settings.compressionLinux, ".linux");
            if (GUILayout.Button("Generate All Variants"))
                GenerateAllVariants();
        }
        EditorGUILayout.EndScrollView();
    }

    private void GenerateVariantMipMaps(TextureFormat compression, string suffix)
    {
        if (!TryGetShader(out var shader))
            return;
        GenerateVariantMipMaps(shader, compression, suffix);
    }

    private void GenerateVariantMipMaps(ComputeShader shader, TextureFormat compression, string suffix)
    {
        CustomMipMapGeneratorGpu.Generate(sourceTexture, settings, shader, compression, suffix);
    }

    private void GeneratePcVariant()
    {
        if (!TryGetShader(out var shader))
            return;
        GenerateVariantMipMaps(shader, settings.compressionPc, ".pc");
    }

    private void GenerateAllVariants()
    {
        if (!TryGetShader(out var shader))
            return;
        GenerateVariantMipMaps(shader, settings.compressionPc, ".pc");
        GenerateVariantMipMaps(shader, settings.compressionLinux, ".linux");
        GenerateVariantMipMaps(shader, settings.compressionMobile, ".mobile");
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

    private string GetComputeShaderPath()
    {
        var script = MonoScript.FromScriptableObject(this);
        var scriptPath = AssetDatabase.GetAssetPath(script);
        var dir = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrEmpty(dir))
            return ComputeShaderFileName;
        return Path.Combine(dir, ComputeShaderFileName).Replace('\\', '/');
    }

    private string GetToksvigCompressionWarning()
    {
        var missing = new List<string>(3);
        if (!CompressionHasAlpha(settings.compressionMobile))
            missing.Add("Mobile");
        if (!CompressionHasAlpha(settings.compressionPc))
            missing.Add("PC");
        if (!CompressionHasAlpha(settings.compressionLinux))
            missing.Add("Linux");
        if (missing.Count == 0)
            return null;
        return "Toksvig uses alpha. These variants drop alpha: " + string.Join(", ", missing) + ".";
    }

    private bool TryGetShader(out ComputeShader shader)
    {
        var shaderPath = GetComputeShaderPath();
        shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
        if (shader == null)
        {
            Debug.LogError($"Compute shader not found at {shaderPath}.");
            return false;
        }

        return true;
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
}


