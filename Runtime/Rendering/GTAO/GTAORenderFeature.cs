
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class GTAORenderFeature : ScriptableRendererFeature
{
    const string GTAOBentNormalKeyword = "_GTAO_BENT_NORMALS";

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
            Shader.DisableKeyword(ShaderKeywordStrings.ScreenSpaceOcclusion);
            Shader.DisableKeyword(GTAOBentNormalKeyword);
            return;
        }

        if (renderingData.cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        if (renderingData.cameraData.isSceneViewCamera)
        {
            Shader.DisableKeyword(ShaderKeywordStrings.ScreenSpaceOcclusion);
            Shader.DisableKeyword(GTAOBentNormalKeyword);
            return;
        }

        pass.renderPassEvent = settings.renderPassEvent;
        renderer.EnqueuePass(pass);
        Shader.EnableKeyword(ShaderKeywordStrings.ScreenSpaceOcclusion);
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

    sealed class GTAORenderPass : ScriptableRenderPass
    {
        static readonly int GTAOParams0Id = Shader.PropertyToID("_GTAOParams0");
        static readonly int GTAOParams1Id = Shader.PropertyToID("_GTAOParams1");
        static readonly int GTAOParams2Id = Shader.PropertyToID("_GTAOParams2");
        static readonly int GTAOParams3Id = Shader.PropertyToID("_GTAOParams3");
        static readonly int GTAOParams4Id = Shader.PropertyToID("_GTAOParams4");
        static readonly int GTAOParams5Id = Shader.PropertyToID("_GTAOParams5");
        static readonly int GTAOResolutionParamsId = Shader.PropertyToID("_GTAOResolutionParams");
        static readonly int GTAOTemporalParamsId = Shader.PropertyToID("_GTAOTemporalParams");
        static readonly int GTAOUpsampleParamsId = Shader.PropertyToID("_GTAOUpsampleParams");

        static readonly int GTAORawDepthId = Shader.PropertyToID("_GTAORawDepth");
        static readonly int GTAORawDepthHalfId = Shader.PropertyToID("_GTAORawDepthHalf");
        static readonly int GTAOWorkingDepthId = Shader.PropertyToID("_GTAOWorkingDepth");
        static readonly int GTAODepthMip0Id = Shader.PropertyToID("_GTAODepthMip0");
        static readonly int GTAODepthMip1Id = Shader.PropertyToID("_GTAODepthMip1");
        static readonly int GTAODepthMip2Id = Shader.PropertyToID("_GTAODepthMip2");
        static readonly int GTAODepthMip3Id = Shader.PropertyToID("_GTAODepthMip3");
        static readonly int GTAODepthMip4Id = Shader.PropertyToID("_GTAODepthMip4");
        static readonly int GTAONormalMapId = Shader.PropertyToID("_GTAONormalmap");
        static readonly int GTAONormalOutId = Shader.PropertyToID("_GTAONormalOut");
        static readonly int GTAOEdgesId = Shader.PropertyToID("_GTAOEdges");
        static readonly int GTAOEdgesRWId = Shader.PropertyToID("_GTAOEdgesRW");
        static readonly int GTAOAOTermId = Shader.PropertyToID("_GTAOAOTerm");
        static readonly int GTAOOutputAId = Shader.PropertyToID("_GTAOOutputA");
        static readonly int GTAOOutputBId = Shader.PropertyToID("_GTAOOutputB");
        static readonly int GTAOHistoryId = Shader.PropertyToID("_GTAOHistory");
        static readonly int GTAOHistoryOutId = Shader.PropertyToID("_GTAOHistoryOut");
        static readonly int GTAOInputFloatId = Shader.PropertyToID("_GTAOInputFloat");
        static readonly int GTAOFloatOutId = Shader.PropertyToID("_GTAOFloatOut");
        static readonly int GTAOOutputId = Shader.PropertyToID("_GTAOOutput");
        static readonly int GTAOMotionVectorsId = Shader.PropertyToID("_GTAOMotionVectors");
        static readonly int GTAOBentNormalOutId = Shader.PropertyToID("_GTAOBentNormalOut");
        static readonly int GTAOBentNormalId = Shader.PropertyToID("_GTAOBentNormal");
        static readonly int GTAOBentNormalFullOutId = Shader.PropertyToID("_GTAOBentNormalFullOut");
        static readonly int GTAOBentNormalTextureId = Shader.PropertyToID("_GTAOBentNormalTexture");

        static readonly int ScreenSpaceOcclusionId = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");

        static readonly ProfilingSampler DownsampleSampler = new ProfilingSampler("GTAO Downsample Depth");
        static readonly ProfilingSampler PrefilterSampler = new ProfilingSampler("GTAO Prefilter Depth");
        static readonly ProfilingSampler NormalsSampler = new ProfilingSampler("GTAO Normals");
        static readonly ProfilingSampler MainSampler = new ProfilingSampler("GTAO Main");
        static readonly ProfilingSampler DenoiseSampler = new ProfilingSampler("GTAO Denoise");
        static readonly ProfilingSampler TemporalSampler = new ProfilingSampler("GTAO Temporal");
        static readonly ProfilingSampler DecodeSampler = new ProfilingSampler("GTAO Decode");
        static readonly ProfilingSampler UpsampleSampler = new ProfilingSampler("GTAO Upsample");
        static readonly ProfilingSampler SetGlobalSampler = new ProfilingSampler("GTAO Set Global");

        GTAOSettings settings;
        ComputeShader compute;

        int kernelPrefilter;
        int kernelNormals;
        int kernelDownsample;
        int kernelGtaoLow;
        int kernelGtaoMedium;
        int kernelGtaoHigh;
        int kernelGtaoUltra;
        int kernelDenoise;
        int kernelDenoiseLast;
        int kernelTemporal;
        int kernelDecode;
        int kernelDecodeBentNormal;
        int kernelUpsample;
        int kernelUpsampleBentNormal;

        readonly Dictionary<int, HistoryBuffers> histories = new Dictionary<int, HistoryBuffers>();

        public GTAORenderPass(GTAOSettings settings)
        {
            UpdateSettings(settings);
        }

        public void UpdateSettings(GTAOSettings settings)
        {
            this.settings = settings;
            CacheShader(settings != null ? settings.computeShader : null);
            UpdateShaderKeywords();

            var inputs = ScriptableRenderPassInput.Depth;
            if (settings != null && settings.temporal)
            {
                inputs |= ScriptableRenderPassInput.Motion;
            }
            ConfigureInput(inputs);
        }

        public void Dispose()
        {
            foreach (var history in histories.Values)
            {
                history.Release();
            }
            histories.Clear();
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || compute == null || !settings.enabled)
            {
                return;
            }

            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.renderType != CameraRenderType.Base)
            {
                return;
            }

            if (cameraData.isSceneViewCamera)
            {
                return;
            }

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraDepth = resourceData.cameraDepthTexture;
            if (!cameraDepth.IsValid())
            {
                return;
            }

            int fullWidth = cameraData.cameraTargetDescriptor.width;
            int fullHeight = cameraData.cameraTargetDescriptor.height;
            if (fullWidth <= 0 || fullHeight <= 0)
            {
                return;
            }

            int downsample = (int)settings.resolution;
            int aoWidth = Mathf.Max(1, (fullWidth + downsample - 1) / downsample);
            int aoHeight = Mathf.Max(1, (fullHeight + downsample - 1) / downsample);
            bool useDownsample = downsample > 1;

            bool motionValid = resourceData.motionVectorColor.IsValid();
            bool useTemporal = settings.temporal && motionValid;
            bool useBentNormals = settings.bentNormals;
            int cameraId = cameraData.camera.GetInstanceID();

            HistoryBuffers history = null;
            TextureHandle historyRead = default;
            TextureHandle historyWrite = default;
            bool historyReset = true;
            if (useTemporal)
            {
                history = GetHistoryBuffers(cameraId, aoWidth, aoHeight);
                historyReset = !history.valid;
                historyRead = renderGraph.ImportTexture(history.read);
                historyWrite = renderGraph.ImportTexture(history.write);
            }
            else
            {
                if (histories.TryGetValue(cameraId, out var existingHistory))
                {
                    existingHistory.valid = false;
                }
            }

            bool reversedZ = SystemInfo.usesReversedZBuffer;
            var shaderParams = BuildShaderParams(cameraData, aoWidth, aoHeight, fullWidth, fullHeight, useTemporal, historyReset, reversedZ);

            var cameraDesc = cameraData.cameraTargetDescriptor;
            TextureHandle rawDepth = cameraDepth;
            if (useDownsample)
            {
                var rawDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R16_SFloat, "GTAO Raw Depth Half");
                rawDepth = renderGraph.CreateTexture(rawDesc);
                AddDownsamplePass(renderGraph, shaderParams, cameraDepth, rawDepth, aoWidth, aoHeight);
            }

            var depthDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R16_SFloat, "GTAO Depth Pyramid");
            depthDesc.useMipMap = true;
            depthDesc.autoGenerateMips = false;
            var depthPyramid = renderGraph.CreateTexture(depthDesc);

            var normalDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R32_UInt, "GTAO Normals");
            var normals = renderGraph.CreateTexture(normalDesc);

            var edgesDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R8_UNorm, "GTAO Edges");
            var edges = renderGraph.CreateTexture(edgesDesc);

            var packedDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R32_UInt, "GTAO Packed");
            var aoPackedA = renderGraph.CreateTexture(packedDesc);
            TextureHandle aoPackedB = default;
            int denoisePasses = (int)settings.denoise;
            if (denoisePasses > 0)
            {
                aoPackedB = renderGraph.CreateTexture(packedDesc);
            }

            AddPrefilterPass(renderGraph, shaderParams, rawDepth, depthPyramid, aoWidth, aoHeight);
            AddNormalsPass(renderGraph, shaderParams, rawDepth, normals, aoWidth, aoHeight);
            AddMainPass(renderGraph, shaderParams, depthPyramid, normals, edges, aoPackedA, aoWidth, aoHeight);

            TextureHandle aoPacked = aoPackedA;
            if (denoisePasses > 0)
            {
                TextureHandle aoPing = aoPackedB;
                for (int i = 0; i < denoisePasses; ++i)
                {
                    bool last = i == denoisePasses - 1;
                    AddDenoisePass(renderGraph, shaderParams, aoPacked, edges, aoPing, aoWidth, aoHeight, last);
                    Swap(ref aoPacked, ref aoPing);
                }
            }

            TextureHandle bentNormalFull = default;
            if (useBentNormals)
            {
                var bentDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R8G8B8A8_UNorm, "GTAO Bent Normal");
                var bentNormalLow = renderGraph.CreateTexture(bentDesc);
                AddBentNormalDecodePass(renderGraph, shaderParams, aoPacked, bentNormalLow, aoWidth, aoHeight);

                if (aoWidth != fullWidth || aoHeight != fullHeight)
                {
                    var bentFullDesc = CreateTextureDesc(cameraDesc, fullWidth, fullHeight, GraphicsFormat.R8G8B8A8_UNorm, "GTAO Bent Normal Full");
                    bentNormalFull = renderGraph.CreateTexture(bentFullDesc);
                    AddBentNormalUpsamplePass(renderGraph, shaderParams, cameraDepth, depthPyramid, bentNormalLow, bentNormalFull, fullWidth, fullHeight);
                }
                else
                {
                    bentNormalFull = bentNormalLow;
                }
            }

            TextureHandle aoFloat;
            if (useTemporal)
            {
                AddTemporalPass(renderGraph, shaderParams, aoPacked, resourceData.motionVectorColor, historyRead, historyWrite, aoWidth, aoHeight);
                aoFloat = historyWrite;
                history.valid = true;
                Swap(ref history.read, ref history.write);
            }
            else
            {
                var floatDesc = CreateTextureDesc(cameraDesc, aoWidth, aoHeight, GraphicsFormat.R16_SFloat, "GTAO Float");
                aoFloat = renderGraph.CreateTexture(floatDesc);
                AddDecodePass(renderGraph, shaderParams, aoPacked, aoFloat, aoWidth, aoHeight);
            }

            var outputDesc = CreateTextureDesc(cameraDesc, fullWidth, fullHeight, GraphicsFormat.R8_UNorm, "GTAO Output");
            var output = renderGraph.CreateTexture(outputDesc);
            AddUpsamplePass(renderGraph, shaderParams, cameraDepth, depthPyramid, aoFloat, output, fullWidth, fullHeight);

            AddSetGlobalPass(renderGraph, output, bentNormalFull, useBentNormals);
        }
        void CacheShader(ComputeShader shader)
        {
            if (shader == null)
            {
                compute = null;
                return;
            }

            if (shader == compute)
            {
                return;
            }

            compute = shader;
            kernelPrefilter = compute.FindKernel("CSPrefilterDepths16x16");
            kernelNormals = compute.FindKernel("CSGenerateNormals");
            kernelDownsample = compute.FindKernel("CSDownsampleDepth");
            kernelGtaoLow = compute.FindKernel("CSGTAOLow");
            kernelGtaoMedium = compute.FindKernel("CSGTAOMedium");
            kernelGtaoHigh = compute.FindKernel("CSGTAOHigh");
            kernelGtaoUltra = compute.FindKernel("CSGTAOUltra");
            kernelDenoise = compute.FindKernel("CSDenoisePass");
            kernelDenoiseLast = compute.FindKernel("CSDenoiseLastPass");
            kernelTemporal = compute.FindKernel("CSTemporal");
            kernelDecode = compute.FindKernel("CSDecode");
            kernelDecodeBentNormal = compute.FindKernel("CSDecodeBentNormal");
            kernelUpsample = compute.FindKernel("CSUpsample");
            kernelUpsampleBentNormal = compute.FindKernel("CSUpsampleBentNormal");
        }

        void UpdateShaderKeywords()
        {
            bool enableBentNormals = settings != null && settings.enabled && settings.bentNormals && compute != null;
            if (compute != null)
            {
                if (enableBentNormals)
                {
                    compute.EnableKeyword(GTAOBentNormalKeyword);
                }
                else
                {
                    compute.DisableKeyword(GTAOBentNormalKeyword);
                }
            }

        }

        HistoryBuffers GetHistoryBuffers(int cameraId, int width, int height)
        {
            if (!histories.TryGetValue(cameraId, out var history))
            {
                history = new HistoryBuffers();
                histories[cameraId] = history;
            }

            if (history.read == null || history.width != width || history.height != height)
            {
                var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R16_SFloat, 0)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    sRGB = false
                };

                RenderingUtils.ReAllocateHandleIfNeeded(ref history.read, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "GTAO History A");
                RenderingUtils.ReAllocateHandleIfNeeded(ref history.write, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "GTAO History B");
                history.width = width;
                history.height = height;
                history.valid = false;
            }

            return history;
        }

        static TextureDesc CreateTextureDesc(RenderTextureDescriptor baseDesc, int width, int height, GraphicsFormat format, string name)
        {
            var desc = new TextureDesc(width, height, baseDesc.useDynamicScale)
            {
                colorFormat = format,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
                clearBuffer = false,
                autoGenerateMips = false,
                useDynamicScaleExplicit = baseDesc.useDynamicScaleExplicit,
                dimension = baseDesc.dimension,
                slices = baseDesc.volumeDepth,
                vrUsage = baseDesc.vrUsage,
                msaaSamples = MSAASamples.None,
                bindTextureMS = false
            };
            return desc;
        }

        GTAOShaderParams BuildShaderParams(UniversalCameraData cameraData, int aoWidth, int aoHeight, int fullWidth, int fullHeight, bool useTemporal, bool historyReset, bool reversedZ)
        {
            Matrix4x4 proj = GetGpuProjectionMatrix(cameraData);
            float depthLinearizeMul = proj[3, 2];
            float depthLinearizeAdd = proj[2, 2];
            if (!reversedZ)
            {
                depthLinearizeMul = -depthLinearizeMul;
            }

            float tanHalfFovY = 1f / proj[1, 1];
            float tanHalfFovX = 1f / proj[0, 0];
            Vector2 cameraTanHalfFov = new Vector2(tanHalfFovX, tanHalfFovY);

            Vector2 ndcToViewMul = new Vector2(cameraTanHalfFov.x * 2f, cameraTanHalfFov.y * -2f);
            Vector2 ndcToViewAdd = new Vector2(cameraTanHalfFov.x * -1f, cameraTanHalfFov.y * 1f);
            Vector2 viewportPixelSize = new Vector2(1f / aoWidth, 1f / aoHeight);
            Vector2 ndcToViewMulPixelSize = new Vector2(ndcToViewMul.x * viewportPixelSize.x, ndcToViewMul.y * viewportPixelSize.y);

            float denoiseBlurBeta = settings.denoise == DenoiseLevel.Disabled ? 1e4f : 1.2f;
            int noiseIndex = settings.denoise == DenoiseLevel.Disabled ? 0 : (Time.frameCount % 64);
            float outputScale = settings.denoise == DenoiseLevel.Disabled ? 1.5f : 1.0f;
            bool depthAware = settings.depthAwareUpsample && (aoWidth != fullWidth || aoHeight != fullHeight);

            return new GTAOShaderParams
            {
                Params0 = new Vector4(aoWidth, aoHeight, viewportPixelSize.x, viewportPixelSize.y),
                Params1 = new Vector4(depthLinearizeMul, depthLinearizeAdd, cameraTanHalfFov.x, cameraTanHalfFov.y),
                Params2 = new Vector4(ndcToViewMul.x, ndcToViewMul.y, ndcToViewAdd.x, ndcToViewAdd.y),
                Params3 = new Vector4(ndcToViewMulPixelSize.x, ndcToViewMulPixelSize.y, Mathf.Max(0.001f, settings.radius), settings.falloffRange),
                Params4 = new Vector4(settings.radiusMultiplier, settings.finalValuePower, denoiseBlurBeta, settings.sampleDistributionPower),
                Params5 = new Vector4(settings.thinOccluderCompensation, settings.depthMipSamplingOffset, noiseIndex, reversedZ ? 1f : 0f),
                ResolutionParams = new Vector4(aoWidth, aoHeight, fullWidth, fullHeight),
                TemporalParams = new Vector4(useTemporal ? settings.temporalBlend : 0f, settings.temporalClamp, settings.motionVectorScale, historyReset ? 1f : 0f),
                UpsampleParams = new Vector4(settings.intensity, settings.depthThresholdScale, outputScale, depthAware ? 1f : 0f)
            };
        }

        static Matrix4x4 GetGpuProjectionMatrix(UniversalCameraData cameraData)
        {
            Camera camera = cameraData.camera;
            if (camera == null)
            {
                return Matrix4x4.identity;
            }

            bool renderIntoTexture = SystemInfo.graphicsUVStartsAtTop && camera.targetTexture != null;
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, renderIntoTexture);
            if (!IsProjectionValid(proj))
            {
                proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, renderIntoTexture);
            }

            return proj;
        }

        static bool IsProjectionValid(Matrix4x4 proj)
        {
            float m00 = proj.m00;
            float m11 = proj.m11;
            if (Mathf.Abs(m00) < 1e-6f || Mathf.Abs(m11) < 1e-6f)
            {
                return false;
            }

            return !(float.IsNaN(m00) || float.IsNaN(m11) || float.IsInfinity(m00) || float.IsInfinity(m11));
        }

        int GetQualityKernel()
        {
            switch (settings.quality)
            {
                case Quality.Low:
                    return kernelGtaoLow;
                case Quality.Medium:
                    return kernelGtaoMedium;
                case Quality.Ultra:
                    return kernelGtaoUltra;
                default:
                    return kernelGtaoHigh;
            }
        }

        static int DivRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

        static void Swap<T>(ref T a, ref T b)
        {
            T temp = a;
            a = b;
            b = temp;
        }

        static void SetCommonParams(ComputeCommandBuffer cmd, ComputeShader cs, ref GTAOShaderParams parameters)
        {
            cmd.SetComputeVectorParam(cs, GTAOParams0Id, parameters.Params0);
            cmd.SetComputeVectorParam(cs, GTAOParams1Id, parameters.Params1);
            cmd.SetComputeVectorParam(cs, GTAOParams2Id, parameters.Params2);
            cmd.SetComputeVectorParam(cs, GTAOParams3Id, parameters.Params3);
            cmd.SetComputeVectorParam(cs, GTAOParams4Id, parameters.Params4);
            cmd.SetComputeVectorParam(cs, GTAOParams5Id, parameters.Params5);
            cmd.SetComputeVectorParam(cs, GTAOResolutionParamsId, parameters.ResolutionParams);
            cmd.SetComputeVectorParam(cs, GTAOTemporalParamsId, parameters.TemporalParams);
            cmd.SetComputeVectorParam(cs, GTAOUpsampleParamsId, parameters.UpsampleParams);
        }
        void AddDownsamplePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle input, TextureHandle output, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<DownsamplePassData>("GTAO Downsample Depth", out var passData, DownsampleSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelDownsample;

                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((DownsamplePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAORawDepthId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAORawDepthHalfId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddPrefilterPass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle rawDepth, TextureHandle depthPyramid, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<PrefilterPassData>("GTAO Prefilter", out var passData, PrefilterSampler))
            {
                passData.parameters = parameters;
                passData.rawDepth = rawDepth;
                passData.depthPyramid = depthPyramid;
                passData.dispatchX = DivRoundUp(aoWidth, 16);
                passData.dispatchY = DivRoundUp(aoHeight, 16);
                passData.cs = compute;
                passData.kernel = kernelPrefilter;

                builder.UseTexture(rawDepth, AccessFlags.Read);
                builder.UseTexture(depthPyramid, AccessFlags.Write);

                builder.SetRenderFunc((PrefilterPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAORawDepthId, data.rawDepth);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAODepthMip0Id, data.depthPyramid, 0);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAODepthMip1Id, data.depthPyramid, 1);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAODepthMip2Id, data.depthPyramid, 2);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAODepthMip3Id, data.depthPyramid, 3);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAODepthMip4Id, data.depthPyramid, 4);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddNormalsPass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle rawDepth, TextureHandle normals, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<NormalsPassData>("GTAO Normals", out var passData, NormalsSampler))
            {
                passData.parameters = parameters;
                passData.rawDepth = rawDepth;
                passData.normals = normals;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelNormals;

                builder.UseTexture(rawDepth, AccessFlags.Read);
                builder.UseTexture(normals, AccessFlags.Write);

                builder.SetRenderFunc((NormalsPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAORawDepthId, data.rawDepth);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAONormalOutId, data.normals);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddMainPass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle depthPyramid, TextureHandle normals, TextureHandle edges, TextureHandle output, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<MainPassData>("GTAO Main", out var passData, MainSampler))
            {
                passData.parameters = parameters;
                passData.depthPyramid = depthPyramid;
                passData.normals = normals;
                passData.edges = edges;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = GetQualityKernel();

                builder.UseTexture(depthPyramid, AccessFlags.Read);
                builder.UseTexture(normals, AccessFlags.Read);
                builder.UseTexture(edges, AccessFlags.Write);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((MainPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOWorkingDepthId, data.depthPyramid);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAONormalMapId, data.normals);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOEdgesRWId, data.edges);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOOutputAId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddDenoisePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle input, TextureHandle edges, TextureHandle output, int aoWidth, int aoHeight, bool last)
        {
            using (var builder = renderGraph.AddComputePass<DenoisePassData>("GTAO Denoise", out var passData, DenoiseSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.edges = edges;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 16);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = last ? kernelDenoiseLast : kernelDenoise;

                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(edges, AccessFlags.Read);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((DenoisePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOAOTermId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOEdgesId, data.edges);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOOutputBId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddTemporalPass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle input, TextureHandle motionVectors, TextureHandle historyRead, TextureHandle historyWrite, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<TemporalPassData>("GTAO Temporal", out var passData, TemporalSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.motionVectors = motionVectors;
                passData.historyRead = historyRead;
                passData.historyWrite = historyWrite;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelTemporal;

                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(motionVectors, AccessFlags.Read);
                builder.UseTexture(historyRead, AccessFlags.Read);
                builder.UseTexture(historyWrite, AccessFlags.Write);

                builder.SetRenderFunc((TemporalPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOAOTermId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOMotionVectorsId, data.motionVectors);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOHistoryId, data.historyRead);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOHistoryOutId, data.historyWrite);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddDecodePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle input, TextureHandle output, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<DecodePassData>("GTAO Decode", out var passData, DecodeSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelDecode;

                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((DecodePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOAOTermId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOFloatOutId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddBentNormalDecodePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle input, TextureHandle output, int aoWidth, int aoHeight)
        {
            using (var builder = renderGraph.AddComputePass<BentNormalDecodePassData>("GTAO Decode Bent Normal", out var passData, DecodeSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelDecodeBentNormal;

                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((BentNormalDecodePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOAOTermId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOBentNormalOutId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddUpsamplePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle fullDepth, TextureHandle workingDepth, TextureHandle input, TextureHandle output, int fullWidth, int fullHeight)
        {
            using (var builder = renderGraph.AddComputePass<UpsamplePassData>("GTAO Upsample", out var passData, UpsampleSampler))
            {
                passData.parameters = parameters;
                passData.fullDepth = fullDepth;
                passData.workingDepth = workingDepth;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(fullWidth, 8);
                passData.dispatchY = DivRoundUp(fullHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelUpsample;

                builder.UseTexture(fullDepth, AccessFlags.Read);
                builder.UseTexture(workingDepth, AccessFlags.Read);
                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((UpsamplePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAORawDepthId, data.fullDepth);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOWorkingDepthId, data.workingDepth);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOInputFloatId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOOutputId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddBentNormalUpsamplePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle fullDepth, TextureHandle workingDepth, TextureHandle input, TextureHandle output, int fullWidth, int fullHeight)
        {
            using (var builder = renderGraph.AddComputePass<BentNormalUpsamplePassData>("GTAO Upsample Bent Normal", out var passData, UpsampleSampler))
            {
                passData.parameters = parameters;
                passData.fullDepth = fullDepth;
                passData.workingDepth = workingDepth;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(fullWidth, 8);
                passData.dispatchY = DivRoundUp(fullHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelUpsampleBentNormal;

                builder.UseTexture(fullDepth, AccessFlags.Read);
                builder.UseTexture(workingDepth, AccessFlags.Read);
                builder.UseTexture(input, AccessFlags.Read);
                builder.UseTexture(output, AccessFlags.Write);

                builder.SetRenderFunc((BentNormalUpsamplePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    SetCommonParams(cmd, data.cs, ref data.parameters);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAORawDepthId, data.fullDepth);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOWorkingDepthId, data.workingDepth);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOBentNormalId, data.input);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, GTAOBentNormalFullOutId, data.output);
                    cmd.DispatchCompute(data.cs, data.kernel, data.dispatchX, data.dispatchY, 1);
                });
            }
        }

        void AddSetGlobalPass(RenderGraph renderGraph, TextureHandle output, TextureHandle bentNormal, bool useBentNormals)
        {
            using (var builder = renderGraph.AddComputePass<SetGlobalPassData>("GTAO Set Global", out var passData, SetGlobalSampler))
            {
                passData.output = output;
                passData.bentNormal = bentNormal;
                passData.useBentNormals = useBentNormals;
                builder.UseTexture(output, AccessFlags.Read);
                if (useBentNormals)
                {
                    builder.UseTexture(bentNormal, AccessFlags.Read);
                }
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((SetGlobalPassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(ScreenSpaceOcclusionId, data.output);
                    if (data.useBentNormals)
                    {
                        context.cmd.SetGlobalTexture(GTAOBentNormalTextureId, data.bentNormal);
                    }
                });
            }
        }
        sealed class HistoryBuffers
        {
            public RTHandle read;
            public RTHandle write;
            public int width;
            public int height;
            public bool valid;

            public void Release()
            {
                read?.Release();
                write?.Release();
                read = null;
                write = null;
                width = 0;
                height = 0;
                valid = false;
            }
        }

        struct GTAOShaderParams
        {
            public Vector4 Params0;
            public Vector4 Params1;
            public Vector4 Params2;
            public Vector4 Params3;
            public Vector4 Params4;
            public Vector4 Params5;
            public Vector4 ResolutionParams;
            public Vector4 TemporalParams;
            public Vector4 UpsampleParams;
        }

        sealed class DownsamplePassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle input;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class PrefilterPassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle rawDepth;
            public TextureHandle depthPyramid;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class NormalsPassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle rawDepth;
            public TextureHandle normals;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class MainPassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle depthPyramid;
            public TextureHandle normals;
            public TextureHandle edges;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class DenoisePassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle input;
            public TextureHandle edges;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class TemporalPassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle input;
            public TextureHandle motionVectors;
            public TextureHandle historyRead;
            public TextureHandle historyWrite;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class DecodePassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle input;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class BentNormalDecodePassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle input;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class UpsamplePassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle fullDepth;
            public TextureHandle workingDepth;
            public TextureHandle input;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class BentNormalUpsamplePassData
        {
            public ComputeShader cs;
            public int kernel;
            public GTAOShaderParams parameters;
            public TextureHandle fullDepth;
            public TextureHandle workingDepth;
            public TextureHandle input;
            public TextureHandle output;
            public int dispatchX;
            public int dispatchY;
        }

        sealed class SetGlobalPassData
        {
            public TextureHandle output;
            public TextureHandle bentNormal;
            public bool useBentNormals;
        }
    }
}
