using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed partial class GTAORenderFeature : ScriptableRendererFeature
{
    const string GTAOBentNormalKeyword = "_GTAO_BENT_NORMALS";
    const string GTAOOcclusionKeyword = "_GTAO_OCCLUSION";

    public enum Quality
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public enum DenoiseLevel
    {
        Disabled = 0,
        Sharp = 1,
        Medium = 2,
        Soft = 3
    }

    public enum Resolution
    {
        Full = 1,
        Half = 2
    }

    [Serializable]
    public sealed class GTAOSettings
    {
        [Tooltip("Master switch for GTAO feature.")]
        public bool enabled = true;
        [Tooltip("Compute shader asset (XeGTAO.compute).")]
        public ComputeShader computeShader;
        [Tooltip("When the pass runs in the URP frame.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        [Tooltip("Sample count preset (Low/Medium/High/Ultra).")]
        public Quality quality = Quality.High;
        [Tooltip("Number/strength of denoise passes.")]
        public DenoiseLevel denoise = DenoiseLevel.Sharp;
        [Tooltip("AO buffer resolution (Full/Half).")]
        public Resolution resolution = Resolution.Half;
        [Tooltip("Output bent normals for IBL (extra cost).")]
        public bool bentNormals = false;

        [Tooltip("World-space AO radius.")]
        [Min(0.001f)] public float radius = 0.5f;
        [Tooltip("Final AO strength multiplier.")]
        [Min(0f)] public float intensity = 1.0f;

        [Tooltip("Use temporal accumulation (requires motion vectors).")]
        public bool temporal = true;
        [Tooltip("History weight. Higher = smoother, more ghosting.")]
        [Range(0f, 1f)] public float temporalBlend = 0.9f;
        [Tooltip("History clamp range scale. Lower = more stable, higher = more detail.")]
        [Range(0f, 1f)] public float temporalClamp = 0.1f;
        [Tooltip("Scales motion vectors for reprojection.")]
        [Min(0f)] public float motionVectorScale = 1.0f;

        [Tooltip("Depth-aware upsample to preserve edges.")]
        public bool depthAwareUpsample = true;
        [Tooltip("Depth threshold for upsample. Higher = softer edges.")]
        [Min(0f)] public float depthThresholdScale = 1.5f;

        [Tooltip("Radius multiplier (advanced tuning).")]
        public float radiusMultiplier = 1.457f;
        [Tooltip("Distance falloff range for occlusion.")]
        public float falloffRange = 0.615f;
        [Tooltip("Samples distribution power. Higher = more focus near center.")]
        public float sampleDistributionPower = 2.0f;
        [Tooltip("Boost for thin occluders (reduces light leaking).")]
        public float thinOccluderCompensation = 0.0f;
        [Tooltip("Final AO curve power. Higher = darker.")]
        public float finalValuePower = 2.2f;
        [Tooltip("Depth mip sampling bias (quality vs stability).")]
        public float depthMipSamplingOffset = 3.3f;
    }

    [SerializeField] private GTAOSettings settings = new GTAOSettings();
    GTAORenderPass pass;

    public override void Create()
    {
        pass = new GTAORenderPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null)
        {
            pass = new GTAORenderPass(settings);
        }

        pass.UpdateSettings(settings);

        if (!settings.enabled || settings.computeShader == null)
        {
            Shader.DisableKeyword(GTAOBentNormalKeyword);
            Shader.DisableKeyword(GTAOOcclusionKeyword);
            return;
        }

        if (renderingData.cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        if (renderingData.cameraData.isSceneViewCamera)
        {
            Shader.DisableKeyword(GTAOBentNormalKeyword);
            Shader.DisableKeyword(GTAOOcclusionKeyword);
            return;
        }

        pass.renderPassEvent = settings.renderPassEvent;
        renderer.EnqueuePass(pass);
        Shader.EnableKeyword(GTAOOcclusionKeyword);
        if (settings.bentNormals)
        {
            Shader.EnableKeyword(GTAOBentNormalKeyword);
        }
        else
        {
            Shader.DisableKeyword(GTAOBentNormalKeyword);
        }
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}
