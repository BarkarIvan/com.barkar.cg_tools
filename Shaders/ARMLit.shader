Shader "CGTools/ARMLit"
{
    Properties
    {

        [MainTexture] _BaseMap ("Albedo (RGB) Alpha (A)", 2D) = "white"{}
        _BaseColor ("Color", Color) = (1,1,1,1)

        _AdditionalMap ("ARM Map (R=AO G=Roughness B=Metallic)", 2D) = "white"{}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0

        _NormalMap ("Normal Map", 2D) = "gray"{}
        [Toggle(_MQ_QUANTIZED)] _MQQuantized ("Use Quantized Normals", Float) = 0
        [Toggle(_USETOGSVIK)] _UseToksvig ("Use Toksvig", Float) = 0
        _ToksvigStrength ("ToksvigStrength", Range(0,1)) = 0.5
        _NormalMapScale("Normal Map Scale", Range(0,3)) = 1

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Roughness( "Roughness", Range(0,1)) = 0.0
        _SpecularAAStrength("Specular AA Strength", Range(0,1)) = 0.0

        [Toggle(_MATERIAL_SPECULAR)] _UseSpecular ("Use Specular (glTF)", Float) = 0
        _SpecularFactor ("Specular Factor", Range(0,1)) = 1.0
        _SpecularColor ("Specular Color", Color) = (1,1,1,1)
        _SpecularMap ("Specular Map (RGBA: RGB=Color A=Factor)", 2D) = "white"{}

        [Toggle(_MATERIAL_CLEARCOAT)] _UseClearcoat ("Use Clearcoat (glTF)", Float) = 0
        _ClearcoatFactor ("Clearcoat Factor", Range(0,1)) = 0.0
        _ClearcoatRoughness ("Clearcoat Roughness", Range(0,1)) = 0.0
        _ClearcoatMap ("Clearcoat Map (R=Factor G=Roughness)", 2D) = "white"{}
        _ClearcoatNormalMap ("Clearcoat Normal Map", 2D) = "bump"{}
        _ClearcoatNormalScale ("Clearcoat Normal Scale", Range(0,3)) = 1.0

        [Toggle(_MATERIAL_SHEEN)] _UseSheen ("Use Sheen (glTF)", Float) = 0
        _SheenColor ("Sheen Color", Color) = (0,0,0,1)
        _SheenRoughness ("Sheen Roughness", Range(0,1)) = 0.0
        _SheenColorMap ("Sheen Map (RGBA: RGB=Color A=Roughness)", 2D) = "white"{}

        [HDR] _EmissionColor ("Emission", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map (RGB)", 2D) = "black"{}

        _Brightness("Brightness", Range(0,2)) = 1

        [Toggle(_USEALPHACLIP)] _UseAlphaClip ("Use Alpha Clip", Float) = 0
        _Cutoff ("ClipAlha", Range(0,1)) = 0
        _OffsetFactor ("Offset Factor", Range(-1,1)) = 0
        _OffsetUnits ("Offset Units", Range(-1,1)) = 0


        [Space(40)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Int) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _Blend1 ("Blend mode", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _Blend2 ("Blend mode", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }


        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull [_Cull]
            Blend [_Blend1] [_Blend2]
            ZWrite [_ZWrite]
            Offset [_OffsetFactor], [_OffsetUnits]


            HLSLPROGRAM
            #pragma vertex ARMLitVertex
            #pragma fragment ARMLitFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _MQ_QUANTIZED
            #pragma shader_feature_local _USETOKSVIG
            #pragma shader_feature_local _ADDITIONALMAP
            #pragma shader_feature_local _MATERIAL_SPECULAR
            #pragma shader_feature_local _SPECULAR_MAP
            #pragma shader_feature_local _MATERIAL_CLEARCOAT
            #pragma shader_feature_local _CLEARCOAT_MAP
            #pragma shader_feature_local _CLEARCOAT_NORMALMAP
            #pragma shader_feature_local _MATERIAL_SHEEN
            #pragma shader_feature_local _SHEEN_COLOR_MAP
            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_fragment _USEALPHACLIP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ _GTAO_OCCLUSION
            #pragma multi_compile_fragment _ _GTAO_BENT_NORMALS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Surface.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/CustomBRDF.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode"="ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_fragment _USEALPHACLIP
            #pragma shader_feature_local _MQ_QUANTIZED

            #pragma vertex ARMLitShadowCasterVertex
            #pragma fragment ARMLitShadowCasterFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode"="DepthOnly"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_local _USEALPHACLIP

            #pragma vertex ARMLitDepthOnlyVertex
            #pragma fragment ARMLitDepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitDepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode"="DepthNormals"
            }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _MQ_QUANTIZED
            #pragma shader_feature_local _USEALPHACLIP

            #pragma vertex ARMLitDepthNormalsVertex
            #pragma fragment ARMLitDepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags
            {
                "LightMode"="MotionVectors"
            }

            ColorMask RG

            HLSLPROGRAM
            #pragma shader_feature_local _USEALPHACLIP
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitMotionVectorsPass.hlsl"
            ENDHLSL
        }


        Pass
        {
            Name "Meta"
            Tags
            {
                "LightMode"="Meta"
            }

            // ZWrite On
            //ZTest LEqual
            Cull Off


            HLSLPROGRAM
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ADDITIONALMAP
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local _USEALPHACLIP
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma vertex MetaPassVertex
            #pragma fragment MetaPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/Surface.hlsl"


            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitInput.hlsl"
            #include "Packages/com.barkar.cg_tools/ShaderLibrary/ARMLit/ARMLitMetaPass.hlsl"
            ENDHLSL
        }
    }



    CustomEditor "ARMLitShaderEditor"
}
