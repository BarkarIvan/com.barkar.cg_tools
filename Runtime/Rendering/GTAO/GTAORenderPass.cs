using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed partial class GTAORenderFeature
{
    sealed partial class GTAORenderPass : ScriptableRenderPass
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
    }
}
