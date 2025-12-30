using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;

public class ARMLitShaderEditor : BaseShaderGUI
{
    private const float SectionSpacing = 30f;

    private MaterialEditor materialEditorRef;
    private MaterialProperty[] materialProperties;
    private Props props;
    private BlendModes blendMode;
    private MaterialHeaderScopeList headerScopeList;

    private enum HeaderExpandable : uint
    {
        SurfaceOptions = 1 << 0,
        SurfaceInputs = 1 << 1,
        AdvancedOptions = 1 << 2
    }

    private static class HeaderStyles
    {
        public static readonly GUIContent SurfaceOptions = new GUIContent("Surface Options");
        public static readonly GUIContent SurfaceInputs = new GUIContent("Surface Inputs");
        public static readonly GUIContent AdvancedOptions = new GUIContent("Advanced Options");
    }

    private sealed class Props
    {
        public MaterialProperty BaseMap;
        public MaterialProperty BaseColor;
        public MaterialProperty AdditionalMap;
        public MaterialProperty OcclusionStrength;
        public MaterialProperty NormalMap;
        public MaterialProperty NormalMapScale;
        public MaterialProperty Metallic;
        public MaterialProperty Roughness;
        public MaterialProperty UseToksvig;
        public MaterialProperty ToksvigStrength;
        public MaterialProperty EmissionColor;
        public MaterialProperty EmissionMap;
        public MaterialProperty Brightness;
        public MaterialProperty UseAlphaClip;
        public MaterialProperty Cutoff;
        public MaterialProperty Cull;
        public MaterialProperty Blend1;
        public MaterialProperty Blend2;
        public MaterialProperty ZWrite;
        public MaterialProperty OffsetFactor;
        public MaterialProperty OffsetUnits;
    }

    public enum CullEnum
    {
        Off = 0,
        Front = 1,
        Back = 2
    }

    public enum BlendModes
    {
        Opaque = 0,
        Transparent = 1,
        Fade = 2,
    }

    public enum BlendModeEnum
    {
        Zero = 0,
        One = 1,
        DstColor = 2,
        SrcColor = 3,
        OneMinusDstColor = 4,
        SrcAlpha = 5,
        OneMinusSrcColor = 6,
        DstAlpha = 7,
        OneMinusDstAlpha = 8,
        SrcAlphaSaturate = 9,
        OneMinusSrcAlpha = 10
    }

    public enum ZWriteEnum
    {
        On = 1,
        Off = 0
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        materialEditorRef = materialEditor;
        materialProperties = properties;

        props = FindProperties();
        blendMode = GetBlendModeFromMaterialProperties(props);

        EnsureHeaderScopeList();

        EditorGUI.BeginChangeCheck();
        headerScopeList.DrawHeaders(materialEditorRef, materialEditorRef.target as Material);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var obj in materialEditorRef.targets)
            {
                if (obj is Material material)
                {
                    ApplyKeywords(material);
                }
            }
        }
    }

    private void DrawBaseSection()
    {
        EditorGUILayout.HelpBox("Base Map", MessageType.None);
        materialEditorRef.TextureProperty(props.BaseMap, "Base Color");
        EditorGUILayout.Space(SectionSpacing);
        materialEditorRef.ShaderProperty(props.BaseColor, "Color");
    }

    private void DrawArmSection()
    {
        EditorGUILayout.HelpBox("ARM (AO, Roughness, Metallic)", MessageType.None);
        materialEditorRef.TextureProperty(props.AdditionalMap, "ARM Map");
        materialEditorRef.ShaderProperty(props.OcclusionStrength, "Occlusion Strength");
        EditorGUILayout.Space(SectionSpacing);
    }

    private void DrawNormalSection()
    {
        materialEditorRef.TextureProperty(props.NormalMap, "Normal Map");

        if (props.NormalMap.textureValue != null)
        {
            materialEditorRef.ShaderProperty(props.UseToksvig, "Use Toksvig");
            materialEditorRef.ShaderProperty(props.NormalMapScale, "Normal Map Scale");
        }

        if (props.UseToksvig.floatValue > 0)
        {
            materialEditorRef.ShaderProperty(props.ToksvigStrength, "Strength");
        }

        EditorGUILayout.Space(SectionSpacing);
    }

    private void DrawBrdfSection()
    {
        EditorGUILayout.HelpBox("BRDF", MessageType.None);
        materialEditorRef.ShaderProperty(props.Metallic, "Metallic");
        materialEditorRef.ShaderProperty(props.Roughness, "Roughness");

        EditorGUILayout.Separator();
        EditorGUILayout.Space(SectionSpacing);
    }

    private void DrawEmissionSection()
    {
        EditorGUILayout.HelpBox("EMISSION", MessageType.None);
        materialEditorRef.TextureProperty(props.EmissionMap, "EmissionMap");
        materialEditorRef.ShaderProperty(props.EmissionColor, "EmissionColor");
        EditorGUILayout.Space(SectionSpacing);
    }

    private void DrawSurfaceOptionsSection()
    {
        materialEditorRef.ShaderProperty(props.Brightness, "Brightness");
        EditorGUILayout.Space(SectionSpacing);
        materialEditorRef.ShaderProperty(props.UseAlphaClip, "Use Alpha Clip");
        if (props.UseAlphaClip.floatValue == 1)
        {
            materialEditorRef.ShaderProperty(props.Cutoff, "Alpha Clip Threshold");
        }
    }

    private void DrawRenderStateSection()
    {
        props.Cull.floatValue = (float)(CullEnum)EditorGUILayout.EnumPopup("Cull", (CullEnum)props.Cull.floatValue);
        materialEditorRef.ShaderProperty(props.OffsetFactor, "Offset Factor");
        materialEditorRef.ShaderProperty(props.OffsetUnits, "Offset Units");
        EditorGUILayout.Space();

        blendMode = (BlendModes)EditorGUILayout.EnumPopup("Blend Mode", blendMode);
        switch (blendMode)
        {
            case BlendModes.Opaque:
                props.Blend1.floatValue = (int)BlendModeEnum.One;
                props.Blend2.floatValue = (int)BlendModeEnum.Zero;
                props.ZWrite.floatValue = (int)ZWriteEnum.On;

                break;
            case BlendModes.Transparent:
                props.Blend1.floatValue = (int)BlendModeEnum.SrcAlpha;
                props.Blend2.floatValue = (int)BlendModeEnum.OneMinusSrcAlpha;
                props.ZWrite.floatValue = (int)ZWriteEnum.Off;

                break;
            case BlendModes.Fade:
                props.Blend1.floatValue = (int)BlendModeEnum.SrcAlpha;
                props.Blend2.floatValue = (int)BlendModeEnum.OneMinusSrcAlpha;
                props.ZWrite.floatValue = (int)ZWriteEnum.On;

                break;
        }
        materialEditorRef.RenderQueueField();
    }

    private void DrawSurfaceOptionsHeader(Material material)
    {
        DrawSurfaceOptionsSection();
    }

    private void DrawSurfaceInputsHeader(Material material)
    {
        DrawBaseSection();
        DrawArmSection();
        DrawNormalSection();
        DrawBrdfSection();
        DrawEmissionSection();
    }

    private void DrawAdvancedOptionsHeader(Material material)
    {
        DrawRenderStateSection();
    }

    private void ApplyKeywords(Material material)
    {
        SetKeyword(material, "_USEALPHACLIP", props.UseAlphaClip.floatValue == 1);
        SetKeyword(material, "_NORMALMAP", props.NormalMap.textureValue != null);
        SetKeyword(material, "_USETOKSVIG", props.UseToksvig.floatValue == 1);
        SetKeyword(material, "_ADDITIONALMAP", props.AdditionalMap.textureValue != null);
        SetKeyword(material, "_EMISSION", props.EmissionMap.textureValue != null);
    }

    private BlendModes GetBlendModeFromMaterialProperties(Props props)
    {
        if (props.Blend1.floatValue == (int)BlendModeEnum.SrcAlpha && props.Blend2.floatValue == (int)BlendModeEnum.OneMinusSrcAlpha)
        {
            if (props.ZWrite.floatValue == (int)ZWriteEnum.On) return BlendModes.Fade;

            return BlendModes.Transparent;
        }
        return BlendModes.Opaque;
    }

    private void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
        {
            material.EnableKeyword(keyword);
        }
        else
        {
            material.DisableKeyword(keyword);
        }
    }

    private Props FindProperties()
    {
        return new Props
        {
            BaseMap = FindProperty("_BaseMap"),
            BaseColor = FindProperty("_BaseColor"),
            AdditionalMap = FindProperty("_AdditionalMap"),
            OcclusionStrength = FindProperty("_OcclusionStrength"),
            UseToksvig = FindProperty("_UseToksvig"),
            ToksvigStrength = FindProperty("_ToksvigStrength"),
            NormalMap = FindProperty("_NormalMap"),
            NormalMapScale = FindProperty("_NormalMapScale"),
            UseAlphaClip = FindProperty("_UseAlphaClip"),
            Cutoff = FindProperty("_Cutoff"),
            Metallic = FindProperty("_Metallic"),
            Roughness = FindProperty("_Roughness"),
            Brightness = FindProperty("_Brightness"),
            EmissionMap = FindProperty("_EmissionMap"),
            EmissionColor = FindProperty("_EmissionColor"),
            Cull = FindProperty("_Cull"),
            Blend1 = FindProperty("_Blend1"),
            Blend2 = FindProperty("_Blend2"),
            ZWrite = FindProperty("_ZWrite"),
            OffsetFactor = FindProperty("_OffsetFactor"),
            OffsetUnits = FindProperty("_OffsetUnits")
        };
    }

    private MaterialProperty FindProperty(string propertyName)
    {
        MaterialProperty prop = FindProperty(propertyName, materialProperties);

        if (prop == null)
        {
            Debug.LogError("Property " + propertyName + " not found");
            return null;
        }

        return prop;
    }

    private void EnsureHeaderScopeList()
    {
        if (headerScopeList != null)
        {
            return;
        }

        headerScopeList = new MaterialHeaderScopeList();
        headerScopeList.RegisterHeaderScope(HeaderStyles.SurfaceOptions, (uint)HeaderExpandable.SurfaceOptions, DrawSurfaceOptionsHeader);
        headerScopeList.RegisterHeaderScope(HeaderStyles.SurfaceInputs, (uint)HeaderExpandable.SurfaceInputs, DrawSurfaceInputsHeader);
        headerScopeList.RegisterHeaderScope(HeaderStyles.AdvancedOptions, (uint)HeaderExpandable.AdvancedOptions, DrawAdvancedOptionsHeader);
    }
}
