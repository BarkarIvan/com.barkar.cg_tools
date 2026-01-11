using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed partial class GTAORenderFeature
{
    sealed partial class GTAORenderPass
    {
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

        bool UseAsyncCompute()
        {
            return settings != null && settings.asyncCompute && SystemInfo.supportsAsyncCompute;
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
    }
}
