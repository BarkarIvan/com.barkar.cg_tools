using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public sealed partial class GTAORenderFeature
{
    sealed partial class GTAORenderPass
    {
        void AddDownsamplePass(RenderGraph renderGraph, GTAOShaderParams parameters, TextureHandle input, TextureHandle output, int aoWidth, int aoHeight)
        {
            bool useAsync = UseAsyncCompute();
            using (var builder = renderGraph.AddComputePass<DownsamplePassData>("GTAO Downsample Depth", out var passData, DownsampleSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelDownsample;

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
            using (var builder = renderGraph.AddComputePass<PrefilterPassData>("GTAO Prefilter", out var passData, PrefilterSampler))
            {
                passData.parameters = parameters;
                passData.rawDepth = rawDepth;
                passData.depthPyramid = depthPyramid;
                passData.dispatchX = DivRoundUp(aoWidth, 16);
                passData.dispatchY = DivRoundUp(aoHeight, 16);
                passData.cs = compute;
                passData.kernel = kernelPrefilter;

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
            using (var builder = renderGraph.AddComputePass<NormalsPassData>("GTAO Normals", out var passData, NormalsSampler))
            {
                passData.parameters = parameters;
                passData.rawDepth = rawDepth;
                passData.normals = normals;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelNormals;

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
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

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
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

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
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

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
            using (var builder = renderGraph.AddComputePass<DecodePassData>("GTAO Decode", out var passData, DecodeSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelDecode;

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
            using (var builder = renderGraph.AddComputePass<BentNormalDecodePassData>("GTAO Decode Bent Normal", out var passData, DecodeSampler))
            {
                passData.parameters = parameters;
                passData.input = input;
                passData.output = output;
                passData.dispatchX = DivRoundUp(aoWidth, 8);
                passData.dispatchY = DivRoundUp(aoHeight, 8);
                passData.cs = compute;
                passData.kernel = kernelDecodeBentNormal;

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
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

                builder.EnableAsyncCompute(useAsync);
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
            bool useAsync = UseAsyncCompute();
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

                builder.EnableAsyncCompute(useAsync);
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
                    context.cmd.SetGlobalTexture(GTAOOcclusionTextureId, data.output);
                    if (data.useBentNormals)
                    {
                        context.cmd.SetGlobalTexture(GTAOBentNormalTextureId, data.bentNormal);
                    }
                });
            }
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
