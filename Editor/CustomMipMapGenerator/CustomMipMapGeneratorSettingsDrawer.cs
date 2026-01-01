using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomMipMapGenerator
{
    [CustomPropertyDrawer(typeof(CustomMipMapGeneratorSettings))]
    internal sealed class CustomMipMapGeneratorSettingsDrawer : PropertyDrawer
    {
        private const float SectionSpacing = 4f;
        private static readonly Dictionary<string, string> Tooltips = new Dictionary<string, string>
        {
            ["textureKind"] = "Color = sRGB color. Normal Map = normal renormalization + Toksvig. Packed/Data = linear masks/roughness/AO/height.",
            ["filterMode"] = "Kaiser = sharper, more detail, more ringing risk. EWA = smoother, less ringing and moire on diagonals/anisotropic patterns.",
            ["kaiserBeta"] = "Sharpness vs ringing. Higher = sharper but more ringing.",
            ["baseRadius"] = "Filter radius in texels. Higher = smoother but blurrier.",
            ["ewaSigma"] = "Elliptical Gaussian radius in texels. Higher = smoother.",
            ["edgeAware"] = "Reduce color bleeding across edges by lowering weights across luminance changes.",
            ["edgeSigma"] = "Edge sensitivity. Lower values preserve edges more aggressively.",
            ["fullResMipCount"] = "Number of mip levels generated from full-res source before switching to previous-mip source.",
            ["maxFullResRatio"] = "0 = no cap. Switches to previous mip if source/dest ratio exceeds this value.",
            ["sharpenEnabled"] = "Apply unsharp filter to the first N mips.",
            ["sharpenStrength"] = "Sharpen amount. Keep low to avoid halos.",
            ["sharpenClamp"] = "Clamp overshoot to limit ringing.",
            ["sharpenMipCount"] = "Number of mip levels to sharpen.",
            ["sharpenNormals"] = "Apply sharpening to normal maps.",
            ["toksvigInAlpha"] = "Store normal length in alpha for Toksvig roughness. Requires shader support.",
            ["alphaFilterMode"] = "None = filter alpha normally. PreserveCoverage = keep alpha-clip coverage. MaxFilter = dilate alpha.",
            ["alphaClip"] = "Alpha threshold used for coverage preservation.",
            ["maxFilterRadiusMin"] = "Minimum dilation radius for MaxFilter alpha.",
            ["maxFilterRadiusMax"] = "Maximum dilation radius for MaxFilter alpha.",
            ["maxFilterStepSize"] = "Increase dilation radius after every N mip levels.",
            ["usePerChannelFilter"] = "Override filter per channel (Average/Min/Max/LinearRoughness/LinearSmoothness/PowerMean/PreserveCoverage).",
            ["channelFilterR"] = "Filter for R channel.",
            ["channelFilterG"] = "Filter for G channel.",
            ["channelFilterB"] = "Filter for B channel.",
            ["channelFilterA"] = "Filter for A channel.",
            ["channelPower"] = "p < 1 biases darker (AO), p > 1 biases brighter. 1 = average.",
            ["wrapModeU"] = "Texture wrap mode for U axis.",
            ["wrapModeV"] = "Texture wrap mode for V axis.",
            ["samplerFilterMode"] = "Filtering mode for sampling the output texture.",
            ["anisoLevel"] = "Anisotropic filtering level.",
            ["mipBias"] = "Mip LOD bias.",
            ["compressionMobile"] = "Compression format for mobile targets (Android/iOS/tvOS).",
            ["compressionPc"] = "Compression format for standalone desktop targets."
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var line = new Rect(position.x, position.y, position.width, lineHeight);

            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            float y = line.yMax + spacing;

            var textureKindProp = property.FindPropertyRelative("textureKind");
            var filterModeProp = property.FindPropertyRelative("filterMode");
            var edgeAwareProp = property.FindPropertyRelative("edgeAware");
            var edgeSigmaProp = property.FindPropertyRelative("edgeSigma");
            var ewaSigmaProp = property.FindPropertyRelative("ewaSigma");
            var sharpenEnabledProp = property.FindPropertyRelative("sharpenEnabled");
            var sharpenStrengthProp = property.FindPropertyRelative("sharpenStrength");
            var sharpenClampProp = property.FindPropertyRelative("sharpenClamp");
            var sharpenMipCountProp = property.FindPropertyRelative("sharpenMipCount");
            var sharpenNormalsProp = property.FindPropertyRelative("sharpenNormals");
            var toksvigInAlphaProp = property.FindPropertyRelative("toksvigInAlpha");
            var maxFullResRatioProp = property.FindPropertyRelative("maxFullResRatio");
            var kaiserBetaProp = property.FindPropertyRelative("kaiserBeta");
            var baseRadiusProp = property.FindPropertyRelative("baseRadius");
            var usePerChannelFilterProp = property.FindPropertyRelative("usePerChannelFilter");
            var channelFilterRProp = property.FindPropertyRelative("channelFilterR");
            var channelFilterGProp = property.FindPropertyRelative("channelFilterG");
            var channelFilterBProp = property.FindPropertyRelative("channelFilterB");
            var channelFilterAProp = property.FindPropertyRelative("channelFilterA");
            var channelPowerProp = property.FindPropertyRelative("channelPower");
            var alphaFilterModeProp = property.FindPropertyRelative("alphaFilterMode");
            var alphaClipProp = property.FindPropertyRelative("alphaClip");
            var maxFilterRadiusMinProp = property.FindPropertyRelative("maxFilterRadiusMin");
            var maxFilterRadiusMaxProp = property.FindPropertyRelative("maxFilterRadiusMax");
            var maxFilterStepSizeProp = property.FindPropertyRelative("maxFilterStepSize");
            var fullResMipCountProp = property.FindPropertyRelative("fullResMipCount");
            var compressionMobileProp = property.FindPropertyRelative("compressionMobile");
            var compressionPcProp = property.FindPropertyRelative("compressionPc");
            var wrapModeUProp = property.FindPropertyRelative("wrapModeU");
            var wrapModeVProp = property.FindPropertyRelative("wrapModeV");
            var samplerFilterModeProp = property.FindPropertyRelative("samplerFilterMode");
            var anisoLevelProp = property.FindPropertyRelative("anisoLevel");
            var mipBiasProp = property.FindPropertyRelative("mipBias");

            var textureKind = (TextureKind)textureKindProp.enumValueIndex;
            bool isNormal = textureKind == TextureKind.NormalMap;
            bool isData = textureKind == TextureKind.DataMap;
            bool toksvigActive = isNormal && toksvigInAlphaProp.boolValue;

            y = DrawHeader(position, y, "Texture Type");
            DrawProperty(position, ref y, textureKindProp, spacing);

            y = DrawHeader(position, y, "Filter");
            DrawProperty(position, ref y, filterModeProp, spacing);
            var filterMode = (FilterMode)filterModeProp.enumValueIndex;
            if (filterMode == FilterMode.Kaiser)
            {
                DrawSlider(position, ref y, kaiserBetaProp, 1f, 20f);
                DrawSlider(position, ref y, baseRadiusProp, 1f, 6f);
            }
            else
            {
                DrawSlider(position, ref y, ewaSigmaProp, 0.5f, 3f);
            }

            if (!isNormal)
            {
                DrawProperty(position, ref y, edgeAwareProp, spacing);
                if (edgeAwareProp.boolValue)
                    DrawSlider(position, ref y, edgeSigmaProp, 0.01f, 0.5f);
            }

            y = DrawHeader(position, y, "Full-Res Mips");
            DrawIntSlider(position, ref y, fullResMipCountProp, 0, 6);
            DrawIntSlider(position, ref y, maxFullResRatioProp, 0, 64);

            y = DrawHeader(position, y, "Sharpen");
            DrawProperty(position, ref y, sharpenEnabledProp, spacing);
            if (sharpenEnabledProp.boolValue)
            {
                DrawSlider(position, ref y, sharpenStrengthProp, 0f, 1f);
                DrawSlider(position, ref y, sharpenClampProp, 0f, 0.2f);
                DrawIntSlider(position, ref y, sharpenMipCountProp, 1, 6);
                if (isNormal)
                    DrawProperty(position, ref y, sharpenNormalsProp, spacing);
            }

            if (isNormal)
            {
                y = DrawHeader(position, y, "Toksvig");
                DrawProperty(position, ref y, toksvigInAlphaProp, spacing);
            }

            if (!isData)
            {
                y = DrawHeader(position, y, "Alpha Filtering");
                using (new EditorGUI.DisabledScope(toksvigActive))
                {
                    DrawProperty(position, ref y, alphaFilterModeProp, spacing);
                }

                if (!toksvigActive)
                {
                    var alphaMode = (AlphaFilterMode)alphaFilterModeProp.enumValueIndex;
                    if (alphaMode == AlphaFilterMode.PreserveCoverage)
                        DrawSlider(position, ref y, alphaClipProp, 0f, 1f);
                    if (alphaMode == AlphaFilterMode.MaxFilter)
                    {
                        DrawIntSlider(position, ref y, maxFilterRadiusMinProp, 1, 4);
                        DrawIntSlider(position, ref y, maxFilterRadiusMaxProp, 1, 8);
                        DrawIntSlider(position, ref y, maxFilterStepSizeProp, 1, 4);
                    }
                }
            }

            if (isData)
            {
                y = DrawHeader(position, y, "Per-Channel Filters");
                DrawProperty(position, ref y, usePerChannelFilterProp, spacing);
                if (usePerChannelFilterProp.boolValue)
                {
                    DrawProperty(position, ref y, channelFilterRProp, spacing);
                    DrawProperty(position, ref y, channelFilterGProp, spacing);
                    DrawProperty(position, ref y, channelFilterBProp, spacing);
                    DrawProperty(position, ref y, channelFilterAProp, spacing);

                    bool usesPowerMean = UsesChannelFilter(channelFilterRProp, ChannelFilter.PowerMean)
                        || UsesChannelFilter(channelFilterGProp, ChannelFilter.PowerMean)
                        || UsesChannelFilter(channelFilterBProp, ChannelFilter.PowerMean)
                        || UsesChannelFilter(channelFilterAProp, ChannelFilter.PowerMean);
                    if (usesPowerMean)
                        DrawSlider(position, ref y, channelPowerProp, 0.25f, 8f);

                    bool usesCoverage = UsesChannelFilter(channelFilterRProp, ChannelFilter.PreserveCoverage)
                        || UsesChannelFilter(channelFilterGProp, ChannelFilter.PreserveCoverage)
                        || UsesChannelFilter(channelFilterBProp, ChannelFilter.PreserveCoverage)
                        || UsesChannelFilter(channelFilterAProp, ChannelFilter.PreserveCoverage);
                    if (usesCoverage)
                        DrawSlider(position, ref y, alphaClipProp, 0f, 1f);
                }
            }

            y = DrawHeader(position, y, "Sampling");
            DrawProperty(position, ref y, wrapModeUProp, spacing);
            DrawProperty(position, ref y, wrapModeVProp, spacing);
            DrawProperty(position, ref y, samplerFilterModeProp, spacing);
            DrawIntSlider(position, ref y, anisoLevelProp, 1, 16);
            DrawSlider(position, ref y, mipBiasProp, -2f, 2f);

            if (!ShouldHideCompression(property))
            {
                y = DrawHeader(position, y, "Compression");
                DrawProperty(position, ref y, compressionMobileProp, spacing);
                DrawProperty(position, ref y, compressionPcProp, spacing);
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            if (!property.isExpanded)
                return lineHeight;

            float y = 0f;
            y += lineHeight + spacing;
            y = MeasureSettings(property, y, lineHeight, spacing);
            return y;
        }

        private static bool UsesChannelFilter(SerializedProperty property, ChannelFilter filter)
        {
            if (property == null)
                return false;
            return property.enumValueIndex == (int)filter;
        }

        private static float MeasureSettings(SerializedProperty property, float y, float lineHeight, float spacing)
        {
            var textureKindProp = property.FindPropertyRelative("textureKind");
            var filterModeProp = property.FindPropertyRelative("filterMode");
            var edgeAwareProp = property.FindPropertyRelative("edgeAware");
            var sharpenEnabledProp = property.FindPropertyRelative("sharpenEnabled");
            var toksvigInAlphaProp = property.FindPropertyRelative("toksvigInAlpha");
            var usePerChannelFilterProp = property.FindPropertyRelative("usePerChannelFilter");
            var alphaFilterModeProp = property.FindPropertyRelative("alphaFilterMode");

            var textureKind = (TextureKind)textureKindProp.enumValueIndex;
            bool isNormal = textureKind == TextureKind.NormalMap;
            bool isData = textureKind == TextureKind.DataMap;
            bool toksvigActive = isNormal && toksvigInAlphaProp.boolValue;
            var filterMode = (FilterMode)filterModeProp.enumValueIndex;

            y = MeasureHeader(y, lineHeight, spacing);
            y = MeasureProperty(textureKindProp, y, spacing);

            y = MeasureHeader(y, lineHeight, spacing);
            y = MeasureProperty(filterModeProp, y, spacing);
            y = MeasureLine(y, lineHeight, spacing);
            if (filterMode == FilterMode.Kaiser)
                y = MeasureLine(y, lineHeight, spacing);
            if (!isNormal)
            {
                y = MeasureProperty(edgeAwareProp, y, spacing);
                if (edgeAwareProp.boolValue)
                    y = MeasureLine(y, lineHeight, spacing);
            }
            y = MeasureHeader(y, lineHeight, spacing);
            y = MeasureLine(y, lineHeight, spacing);
            y = MeasureLine(y, lineHeight, spacing);

            y = MeasureHeader(y, lineHeight, spacing);
            y = MeasureProperty(sharpenEnabledProp, y, spacing);
            if (sharpenEnabledProp.boolValue)
            {
                y = MeasureLine(y, lineHeight, spacing);
                y = MeasureLine(y, lineHeight, spacing);
                y = MeasureLine(y, lineHeight, spacing);
                if (isNormal)
                    y = MeasureProperty(property.FindPropertyRelative("sharpenNormals"), y, spacing);
            }

            if (isNormal)
            {
                y = MeasureHeader(y, lineHeight, spacing);
                y = MeasureProperty(toksvigInAlphaProp, y, spacing);
            }

            if (!isData)
            {
                y = MeasureHeader(y, lineHeight, spacing);
                y = MeasureProperty(alphaFilterModeProp, y, spacing);
                if (!toksvigActive)
                {
                    var alphaMode = (AlphaFilterMode)alphaFilterModeProp.enumValueIndex;
                    if (alphaMode == AlphaFilterMode.PreserveCoverage)
                        y = MeasureLine(y, lineHeight, spacing);
                    if (alphaMode == AlphaFilterMode.MaxFilter)
                    {
                        y = MeasureLine(y, lineHeight, spacing);
                        y = MeasureLine(y, lineHeight, spacing);
                        y = MeasureLine(y, lineHeight, spacing);
                    }
                }
            }

            if (isData)
            {
                y = MeasureHeader(y, lineHeight, spacing);
                y = MeasureProperty(usePerChannelFilterProp, y, spacing);
                if (usePerChannelFilterProp.boolValue)
                {
                    y = MeasureProperty(property.FindPropertyRelative("channelFilterR"), y, spacing);
                    y = MeasureProperty(property.FindPropertyRelative("channelFilterG"), y, spacing);
                    y = MeasureProperty(property.FindPropertyRelative("channelFilterB"), y, spacing);
                    y = MeasureProperty(property.FindPropertyRelative("channelFilterA"), y, spacing);

                    if (UsesChannelFilter(property.FindPropertyRelative("channelFilterR"), ChannelFilter.PowerMean)
                        || UsesChannelFilter(property.FindPropertyRelative("channelFilterG"), ChannelFilter.PowerMean)
                        || UsesChannelFilter(property.FindPropertyRelative("channelFilterB"), ChannelFilter.PowerMean)
                        || UsesChannelFilter(property.FindPropertyRelative("channelFilterA"), ChannelFilter.PowerMean))
                        y = MeasureLine(y, lineHeight, spacing);

                    if (UsesChannelFilter(property.FindPropertyRelative("channelFilterR"), ChannelFilter.PreserveCoverage)
                        || UsesChannelFilter(property.FindPropertyRelative("channelFilterG"), ChannelFilter.PreserveCoverage)
                        || UsesChannelFilter(property.FindPropertyRelative("channelFilterB"), ChannelFilter.PreserveCoverage)
                        || UsesChannelFilter(property.FindPropertyRelative("channelFilterA"), ChannelFilter.PreserveCoverage))
                        y = MeasureLine(y, lineHeight, spacing);
                }
            }

            y = MeasureHeader(y, lineHeight, spacing);
            y = MeasureProperty(property.FindPropertyRelative("wrapModeU"), y, spacing);
            y = MeasureProperty(property.FindPropertyRelative("wrapModeV"), y, spacing);
            y = MeasureProperty(property.FindPropertyRelative("samplerFilterMode"), y, spacing);
            y = MeasureLine(y, lineHeight, spacing);
            y = MeasureLine(y, lineHeight, spacing);

            if (!ShouldHideCompression(property))
            {
                y = MeasureHeader(y, lineHeight, spacing);
                y = MeasureProperty(property.FindPropertyRelative("compressionMobile"), y, spacing);
                y = MeasureProperty(property.FindPropertyRelative("compressionPc"), y, spacing);
            }

            return y;
        }

        private static float MeasureHeader(float y, float lineHeight, float spacing)
        {
            return y + lineHeight + spacing + SectionSpacing;
        }

        private static float MeasureProperty(SerializedProperty property, float y, float spacing)
        {
            if (property == null)
                return y;
            return y + EditorGUI.GetPropertyHeight(property) + spacing;
        }

        private static float MeasureLine(float y, float lineHeight, float spacing)
        {
            return y + lineHeight + spacing;
        }

        private static float DrawHeader(Rect position, float y, string title)
        {
            var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(line, title, EditorStyles.boldLabel);
            return line.yMax + EditorGUIUtility.standardVerticalSpacing + SectionSpacing;
        }

        private static void DrawProperty(Rect position, ref float y, SerializedProperty property, float spacing)
        {
            if (property == null)
                return;

            float height = EditorGUI.GetPropertyHeight(property);
            var line = new Rect(position.x, y, position.width, height);
            EditorGUI.PropertyField(line, property, GetLabel(property));
            y = line.yMax + spacing;
        }

        private static void DrawSlider(Rect position, ref float y, SerializedProperty property, float min, float max)
        {
            var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            property.floatValue = EditorGUI.Slider(line, GetLabel(property), property.floatValue, min, max);
            y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static void DrawIntSlider(Rect position, ref float y, SerializedProperty property, int min, int max)
        {
            var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            property.intValue = EditorGUI.IntSlider(line, GetLabel(property), property.intValue, min, max);
            y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static bool ShouldHideCompression(SerializedProperty property)
        {
            var target = property.serializedObject?.targetObject;
            return target is CustomMipMapGeneratorProfileSet;
        }

        private static GUIContent GetLabel(SerializedProperty property)
        {
            if (property == null)
                return GUIContent.none;

            if (Tooltips.TryGetValue(property.name, out var tooltip) && !string.IsNullOrEmpty(tooltip))
                return new GUIContent(property.displayName, tooltip);

            return new GUIContent(property.displayName);
        }
    }
}
