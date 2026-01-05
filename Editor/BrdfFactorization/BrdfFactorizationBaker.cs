using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BrdfFactorization
{
    internal static class BrdfFactorizationBaker
    {
        private const float Epsilon = 1e-5f;
        private const float InvPi = 0.31830988618f;

        private struct BilinearSample
        {
            public int i0, i1, i2, i3;
            public float w0, w1, w2, w3;

            public float Evaluate(float[] data)
            {
                return data[i0] * w0 + data[i1] * w1 + data[i2] * w2 + data[i3] * w3;
            }

            public void Accumulate(float[] data, float value)
            {
                data[i0] += w0 * value;
                data[i1] += w1 * value;
                data[i2] += w2 * value;
                data[i3] += w3 * value;
            }
        }

        private struct SampleConstraint
        {
            public BilinearSample pWo;
            public BilinearSample pWi;
            public BilinearSample qH;
        }

        private struct SampleData
        {
            public Vector3 wo;
            public Vector3 wi;
            public Vector3 h;
        }

        public static BrdfFactorizationAsset BakeAndSave(BrdfFactorizationSettings settings, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("BRDF factorization: output asset path is empty.");
                return null;
            }

            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("BRDF factorization: output asset must be a .asset path.");
                return null;
            }

            var result = Bake(settings);
            if (result == null)
                return null;

            var dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            string pPath = Path.Combine(dir ?? string.Empty, $"{baseName}_p.exr").Replace('\\', '/');
            string qPath = Path.Combine(dir ?? string.Empty, $"{baseName}_q.exr").Replace('\\', '/');

            var pTexture = WriteExrTexture(pPath, result.size, result.pPixels);
            var qTexture = WriteExrTexture(qPath, result.size, result.qPixels);

            var asset = AssetDatabase.LoadAssetAtPath<BrdfFactorizationAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<BrdfFactorizationAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.pTexture = pTexture;
            asset.qTexture = qTexture;
            asset.scale = result.scale;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private sealed class BakeResult
        {
            public int size;
            public Color[] pPixels;
            public Color[] qPixels;
            public Vector3 scale;
        }

        private static BakeResult Bake(BrdfFactorizationSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("BRDF factorization: settings are null.");
                return null;
            }

            int size = Mathf.Max(4, settings.textureSize);
            int texelCount = size * size;
            int sampleCount = Mathf.Max(256, settings.sampleCount);

            var sampleData = GenerateSampleDirections(sampleCount);
            var fSamples = new Vector3[sampleCount];

            try
            {
                EditorUtility.DisplayProgressBar("BRDF Factorization", "Sampling BRDF...", 0.0f);
                EvaluateBrdfSamples(settings, sampleData, fSamples);

                var avg = Average(fSamples);
                if (avg.x <= 0f) avg.x = 1f;
                if (avg.y <= 0f) avg.y = 1f;
                if (avg.z <= 0f) avg.z = 1f;

                var logR = new float[sampleCount];
                var logG = new float[sampleCount];
                var logB = new float[sampleCount];
                var fR = new float[sampleCount];
                var fG = new float[sampleCount];
                var fB = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    Vector3 f = fSamples[i];
                    fR[i] = f.x;
                    fG[i] = f.y;
                    fB[i] = f.z;
                    logR[i] = Mathf.Log((f.x + Epsilon * avg.x) / avg.x);
                    logG[i] = Mathf.Log((f.y + Epsilon * avg.y) / avg.y);
                    logB[i] = Mathf.Log((f.z + Epsilon * avg.z) / avg.z);
                }

                EditorUtility.DisplayProgressBar("BRDF Factorization", "Solving R channel...", 0.2f);
                var pR = SolveChannelMultiScale(sampleData, logR, fR, size, settings);
                EditorUtility.DisplayProgressBar("BRDF Factorization", "Solving G channel...", 0.5f);
                var pG = SolveChannelMultiScale(sampleData, logG, fG, size, settings);
                EditorUtility.DisplayProgressBar("BRDF Factorization", "Solving B channel...", 0.8f);
                var pB = SolveChannelMultiScale(sampleData, logB, fB, size, settings);

                var pPixels = new Color[texelCount];
                var qPixels = new Color[texelCount];
                for (int i = 0; i < texelCount; i++)
                {
                    pPixels[i] = new Color(pR.p[i], pG.p[i], pB.p[i], 1f);
                    qPixels[i] = new Color(pR.q[i], pG.q[i], pB.q[i], 1f);
                }

                Vector3 scale = new Vector3(pR.scale, pG.scale, pB.scale);
                Debug.Log($"BRDF factorization: scale = {scale}, relRMS = ({pR.relRms:F4}, {pG.relRms:F4}, {pB.relRms:F4}).");

                return new BakeResult
                {
                    size = size,
                    pPixels = pPixels,
                    qPixels = qPixels,
                    scale = scale
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private sealed class ChannelResult
        {
            public float[] p;
            public float[] q;
            public float scale;
            public float relRms;
        }

        private static ChannelResult SolveChannelMultiScale(SampleData[] sampleData, float[] logF, float[] f, int finalSize, BrdfFactorizationSettings settings)
        {
            int minSize = Mathf.Min(8, finalSize);
            if (minSize < 4)
                minSize = finalSize;

            float[] prevLogP = null;
            float[] prevLogQ = null;
            int prevSize = 0;
            ChannelResult lastResult = null;

            for (int size = minSize; size < finalSize; size *= 2)
            {
                var samples = BuildConstraints(sampleData, size);
                float[] initialX = null;
                if (prevLogP != null && prevLogQ != null)
                {
                    var upP = UpsampleLog(prevLogP, prevSize, size);
                    var upQ = UpsampleLog(prevLogQ, prevSize, size);
                    NormalizeLogs(upP);
                    NormalizeLogs(upQ);
                    int texelCount = size * size;
                    initialX = new float[texelCount * 2];
                    Array.Copy(upP, 0, initialX, 0, texelCount);
                    Array.Copy(upQ, 0, initialX, texelCount, texelCount);
                }

                lastResult = SolveChannel(samples, logF, f, size, settings, initialX);
                prevLogP = ComputeLogArray(lastResult.p);
                prevLogQ = ComputeLogArray(lastResult.q);
                prevSize = size;
            }

            var finalSamples = BuildConstraints(sampleData, finalSize);
            float[] finalInitial = null;
            if (prevLogP != null && prevLogQ != null && prevSize != finalSize)
            {
                var upP = UpsampleLog(prevLogP, prevSize, finalSize);
                var upQ = UpsampleLog(prevLogQ, prevSize, finalSize);
                NormalizeLogs(upP);
                NormalizeLogs(upQ);
                int texelCount = finalSize * finalSize;
                finalInitial = new float[texelCount * 2];
                Array.Copy(upP, 0, finalInitial, 0, texelCount);
                Array.Copy(upQ, 0, finalInitial, texelCount, texelCount);
            }

            return SolveChannel(finalSamples, logF, f, finalSize, settings, finalInitial);
        }

        private static ChannelResult SolveChannel(SampleConstraint[] samples, float[] logF, float[] f, int size, BrdfFactorizationSettings settings, float[] initialX)
        {
            int texelCount = size * size;
            int totalCount = texelCount * 2;

            var solver = new LinearSolver(samples, size, settings.smoothness);
            var rhs = new float[totalCount];
            solver.ApplyAT(logF, rhs);

            var x = new float[totalCount];
            var r = new float[totalCount];
            var p = new float[totalCount];
            var ap = new float[totalCount];

            if (initialX != null && initialX.Length == totalCount)
            {
                Array.Copy(initialX, x, totalCount);
                solver.ApplyM(x, ap);
                for (int i = 0; i < totalCount; i++)
                    r[i] = rhs[i] - ap[i];
            }
            else
            {
                Array.Copy(rhs, r, totalCount);
            }
            Array.Copy(r, p, totalCount);

            double rsOld = Dot(r, r);
            double rhsNorm = Math.Sqrt(Dot(rhs, rhs));
            if (rhsNorm < 1e-8)
            {
                return new ChannelResult
                {
                    p = new float[texelCount],
                    q = new float[texelCount],
                    scale = 1f,
                    relRms = 0f
                };
            }

            for (int iter = 0; iter < settings.maxIterations; iter++)
            {
                solver.ApplyM(p, ap);
                double denom = Dot(p, ap);
                if (Math.Abs(denom) < 1e-10)
                    break;

                double alpha = rsOld / denom;
                AddScaled(x, p, alpha);
                AddScaled(r, ap, -alpha);

                double rsNew = Dot(r, r);
                double rel = Math.Sqrt(rsNew) / rhsNorm;
                if (rel < settings.tolerance)
                    break;

                double beta = rsNew / rsOld;
                ScaleAndAdd(p, r, beta);
                rsOld = rsNew;
            }

            var logP = new float[texelCount];
            var logQ = new float[texelCount];
            Array.Copy(x, 0, logP, 0, texelCount);
            Array.Copy(x, texelCount, logQ, 0, texelCount);

            NormalizeLogs(logP);
            NormalizeLogs(logQ);

            var pLin = new float[texelCount];
            var qLin = new float[texelCount];
            for (int i = 0; i < texelCount; i++)
            {
                pLin[i] = Mathf.Exp(logP[i]);
                qLin[i] = Mathf.Exp(logQ[i]);
            }

            var full = new float[totalCount];
            Array.Copy(pLin, 0, full, 0, texelCount);
            Array.Copy(qLin, 0, full, texelCount, texelCount);

            float scale = ComputeScale(samples, f, full);
            float relRms = ComputeRelRms(samples, f, full, scale);

            return new ChannelResult
            {
                p = pLin,
                q = qLin,
                scale = scale,
                relRms = relRms
            };
        }

        private static void NormalizeLogs(float[] logValues)
        {
            double sum = 0.0;
            for (int i = 0; i < logValues.Length; i++)
                sum += logValues[i];
            float mean = (float)(sum / logValues.Length);
            for (int i = 0; i < logValues.Length; i++)
                logValues[i] -= mean;
        }

        private static float ComputeScale(SampleConstraint[] samples, float[] f, float[] full)
        {
            double num = 0.0;
            double den = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                float approx = s.pWo.Evaluate(full) * s.qH.Evaluate(full) * s.pWi.Evaluate(full);
                float value = f[i];
                num += value * approx;
                den += approx * approx;
            }

            if (den <= 0.0)
                return 1f;
            return (float)(num / den);
        }

        private static float ComputeRelRms(SampleConstraint[] samples, float[] f, float[] full, float scale)
        {
            double num = 0.0;
            double den = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                float approx = scale * s.pWo.Evaluate(full) * s.qH.Evaluate(full) * s.pWi.Evaluate(full);
                float value = f[i];
                float diff = value - approx;
                num += diff * diff;
                den += value * value;
            }

            if (den <= 0.0)
                return 0f;
            return (float)Math.Sqrt(num / den);
        }

        private static SampleData[] GenerateSampleDirections(int sampleCount)
        {
            var data = new SampleData[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                Vector2 woXi = Hammersley((uint)i, (uint)sampleCount);
                Vector2 wiXi = Hammersley((uint)((i * 13) % sampleCount), (uint)sampleCount);
                Vector3 wo = SampleHemisphere(woXi);
                Vector3 wi = SampleHemisphere(wiXi);
                Vector3 h = wo + wi;
                if (h.sqrMagnitude < 1e-10f)
                    h = Vector3.forward;
                h.Normalize();

                data[i] = new SampleData
                {
                    wo = wo,
                    wi = wi,
                    h = h
                };
            }

            return data;
        }

        private static void EvaluateBrdfSamples(BrdfFactorizationSettings settings, SampleData[] data, Vector3[] fSamples)
        {
            for (int i = 0; i < data.Length; i++)
                fSamples[i] = EvaluateBrdf(settings, data[i].wo, data[i].wi);
        }

        private static SampleConstraint[] BuildConstraints(SampleData[] data, int size)
        {
            int texelCount = size * size;
            int pOffset = 0;
            int qOffset = texelCount;
            var samples = new SampleConstraint[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                samples[i] = new SampleConstraint
                {
                    pWo = BuildBilinear(ParabolicUV(data[i].wo), size, pOffset),
                    pWi = BuildBilinear(ParabolicUV(data[i].wi), size, pOffset),
                    qH = BuildBilinear(ParabolicUV(data[i].h), size, qOffset)
                };
            }
            return samples;
        }

        private static float[] UpsampleLog(float[] src, int srcSize, int dstSize)
        {
            var dst = new float[dstSize * dstSize];
            if (srcSize == dstSize)
            {
                Array.Copy(src, dst, src.Length);
                return dst;
            }

            for (int y = 0; y < dstSize; y++)
            {
                float v = (y + 0.5f) / dstSize;
                float sy = v * (srcSize - 1);
                int y0 = Mathf.FloorToInt(sy);
                int y1 = Mathf.Min(y0 + 1, srcSize - 1);
                float ay = sy - y0;
                float by = 1.0f - ay;
                for (int x = 0; x < dstSize; x++)
                {
                    float u = (x + 0.5f) / dstSize;
                    float sx = u * (srcSize - 1);
                    int x0 = Mathf.FloorToInt(sx);
                    int x1 = Mathf.Min(x0 + 1, srcSize - 1);
                    float ax = sx - x0;
                    float bx = 1.0f - ax;

                    float s00 = src[y0 * srcSize + x0];
                    float s10 = src[y0 * srcSize + x1];
                    float s11 = src[y1 * srcSize + x1];
                    float s01 = src[y1 * srcSize + x0];

                    dst[y * dstSize + x] = (s00 * bx + s10 * ax) * by + (s01 * bx + s11 * ax) * ay;
                }
            }

            return dst;
        }

        private static float[] ComputeLogArray(float[] linear)
        {
            var log = new float[linear.Length];
            for (int i = 0; i < linear.Length; i++)
                log[i] = Mathf.Log(Mathf.Max(linear[i], 1e-6f));
            return log;
        }

        private static Vector2 ParabolicUV(Vector3 dir)
        {
            dir.Normalize();
            float denom = 1.0f + dir.z;
            if (denom <= 1e-6f)
                denom = 1e-6f;
            Vector2 uv = new Vector2(dir.x / denom, dir.y / denom);
            uv = uv * 0.5f + new Vector2(0.5f, 0.5f);
            uv.x = Mathf.Clamp01(uv.x);
            uv.y = Mathf.Clamp01(uv.y);
            return uv;
        }

        private static BilinearSample BuildBilinear(Vector2 uv, int size, int offset)
        {
            float u = Mathf.Clamp01(uv.x) * (size - 1);
            float v = Mathf.Clamp01(uv.y) * (size - 1);
            int x0 = Mathf.FloorToInt(u);
            int y0 = Mathf.FloorToInt(v);
            int x1 = Mathf.Min(x0 + 1, size - 1);
            int y1 = Mathf.Min(y0 + 1, size - 1);
            float ax = u - x0;
            float ay = v - y0;
            float bx = 1.0f - ax;
            float by = 1.0f - ay;

            return new BilinearSample
            {
                i0 = offset + x0 + y0 * size,
                i1 = offset + x1 + y0 * size,
                i2 = offset + x1 + y1 * size,
                i3 = offset + x0 + y1 * size,
                w0 = bx * by,
                w1 = ax * by,
                w2 = ax * ay,
                w3 = bx * ay
            };
        }

        private static Vector2 Hammersley(uint i, uint n)
        {
            uint bits = (i << 16) | (i >> 16);
            bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
            bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
            bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
            bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
            float rdi = bits * 2.3283064365386963e-10f;
            return new Vector2((float)i / n, rdi);
        }

        private static Vector3 SampleHemisphere(Vector2 xi)
        {
            float phi = 2.0f * Mathf.PI * xi.x;
            float cosTheta = 1.0f - xi.y;
            float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1.0f - cosTheta * cosTheta));
            return new Vector3(Mathf.Cos(phi) * sinTheta, Mathf.Sin(phi) * sinTheta, cosTheta);
        }

        private static Vector3 EvaluateBrdf(BrdfFactorizationSettings settings, Vector3 wo, Vector3 wi)
        {
            float NoL = Mathf.Max(0f, wi.z);
            float NoV = Mathf.Max(0f, wo.z);
            if (NoL <= 0f || NoV <= 0f)
                return Vector3.zero;

            Vector3 h = (wo + wi).normalized;
            float NoH = Mathf.Clamp01(h.z);
            float VoH = Mathf.Clamp01(Vector3.Dot(wo, h));
            float LoH = Mathf.Clamp01(Vector3.Dot(wi, h));

            float metallic = Mathf.Clamp01(settings.metallic);
            float specularWeight = Mathf.Clamp01(settings.specularWeight);
            float specularF0 = Mathf.Clamp01(settings.specularF0);
            float pr = Mathf.Clamp(settings.roughness, 0.001f, 1f);
            float alphaRoughness = pr * pr;

            float D = D_GGX(NoH, alphaRoughness);
            float G = G_Smith(NoL, NoV, alphaRoughness);
            float spec = (G * D) / Mathf.Max(4.0f * NoL * NoV, 1e-6f);
            Vector3 specContrib = new Vector3(spec, spec, spec);

            Vector3 albedo = new Vector3(settings.albedo.r, settings.albedo.g, settings.albedo.b);
            Vector3 diffuseColor = albedo * (1.0f - metallic);
            Vector3 diffuse = DiffuseBurley(diffuseColor, pr, NoV, NoL, LoH);

            Vector3 dielectricF0 = Vector3.one * (specularF0 * specularWeight);
            Vector3 dielectricF90 = new Vector3(specularWeight, specularWeight, specularWeight);
            Vector3 dielectricF = F_Schlick(dielectricF0, dielectricF90, VoH);
            Vector3 metalF = F_Schlick(albedo, Vector3.one, VoH);

            Vector3 dielectricBrdf = new Vector3(
                Mathf.Lerp(diffuse.x, specContrib.x, dielectricF.x),
                Mathf.Lerp(diffuse.y, specContrib.y, dielectricF.y),
                Mathf.Lerp(diffuse.z, specContrib.z, dielectricF.z));

            Vector3 metalBrdf = new Vector3(
                specContrib.x * metalF.x,
                specContrib.y * metalF.y,
                specContrib.z * metalF.z);

            return Vector3.Lerp(dielectricBrdf, metalBrdf, metallic);
        }

        private static Vector3 DiffuseBurley(Vector3 albedo, float pr, float NoV, float NoL, float LoH)
        {
            float fd90 = 0.5f + 2.0f * pr * LoH * LoH;
            float fdV = 1.0f + (fd90 - 1.0f) * Pow5(1.0f - NoV);
            float fdL = 1.0f + (fd90 - 1.0f) * Pow5(1.0f - NoL);
            return albedo * (fdV * fdL * InvPi);
        }

        private static float D_GGX(float NoH, float alphaRoughness)
        {
            float a2 = alphaRoughness * alphaRoughness;
            float f = (NoH * NoH) * (a2 - 1.0f) + 1.0f;
            return a2 / (Mathf.PI * f * f);
        }

        private static float G_Smith(float NoL, float NoV, float alphaRoughness)
        {
            float r = alphaRoughness;
            float gL = 2.0f * NoL / (NoL + Mathf.Sqrt(r * r + (1.0f - r * r) * (NoL * NoL)));
            float gV = 2.0f * NoV / (NoV + Mathf.Sqrt(r * r + (1.0f - r * r) * (NoV * NoV)));
            return gL * gV;
        }

        private static Vector3 F_Schlick(Vector3 f0, Vector3 f90, float VoH)
        {
            float x = Mathf.Clamp01(1.0f - VoH);
            float x2 = x * x;
            float x5 = x2 * x2 * x;
            return f0 + (f90 - f0) * x5;
        }

        private static float Pow5(float x)
        {
            float x2 = x * x;
            return x2 * x2 * x;
        }

        private static Vector3 Average(Vector3[] samples)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i];
            return sum / Mathf.Max(1, samples.Length);
        }

        private static double Dot(float[] a, float[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
                sum += a[i] * b[i];
            return sum;
        }

        private static void AddScaled(float[] target, float[] src, double scale)
        {
            float s = (float)scale;
            for (int i = 0; i < target.Length; i++)
                target[i] += src[i] * s;
        }

        private static void ScaleAndAdd(float[] target, float[] src, double scale)
        {
            float s = (float)scale;
            for (int i = 0; i < target.Length; i++)
                target[i] = src[i] + target[i] * s;
        }

        private static Texture2D WriteExrTexture(string path, int size, Color[] pixels)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            tex.SetPixels(pixels);
            tex.Apply(false, false);

            var bytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
            UnityEngine.Object.DestroyImmediate(tex);

            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 1;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private sealed class LinearSolver
        {
            private enum LaplaceMask
            {
                Cross,
                Diagonal
            }

            private readonly SampleConstraint[] samples;
            private readonly int size;
            private readonly int texelCount;
            private readonly int totalCount;
            private readonly float lambda;
            private readonly float[] ax;
            private readonly float[] tmp;
            private readonly float[] laplace;

            public LinearSolver(SampleConstraint[] samples, int size, float lambda)
            {
                this.samples = samples;
                this.size = size;
                texelCount = size * size;
                totalCount = texelCount * 2;
                this.lambda = Mathf.Max(0f, lambda);
                ax = new float[samples.Length];
                tmp = new float[totalCount];
                laplace = new float[totalCount];
            }

            public void ApplyA(float[] x, float[] y)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    var s = samples[i];
                    y[i] = s.pWo.Evaluate(x) + s.qH.Evaluate(x) + s.pWi.Evaluate(x);
                }
            }

            public void ApplyAT(float[] y, float[] xOut)
            {
                Array.Clear(xOut, 0, xOut.Length);
                for (int i = 0; i < samples.Length; i++)
                {
                    float value = y[i];
                    var s = samples[i];
                    s.pWo.Accumulate(xOut, value);
                    s.qH.Accumulate(xOut, value);
                    s.pWi.Accumulate(xOut, value);
                }
            }

            public void ApplyM(float[] x, float[] y)
            {
                ApplyA(x, ax);
                ApplyAT(ax, y);

                if (lambda > 0f)
                {
                    ApplyLTL(x, laplace);
                    float scale = lambda * lambda;
                    for (int i = 0; i < y.Length; i++)
                        y[i] += laplace[i] * scale;
                }
            }

            private void ApplyLTL(float[] x, float[] y)
            {
                Array.Clear(y, 0, totalCount);
                ApplyLTLMask(x, y, 0, LaplaceMask.Cross);
                ApplyLTLMask(x, y, 0, LaplaceMask.Diagonal);
                ApplyLTLMask(x, y, texelCount, LaplaceMask.Cross);
                ApplyLTLMask(x, y, texelCount, LaplaceMask.Diagonal);
            }

            private void ApplyLTLMask(float[] x, float[] y, int offset, LaplaceMask mask)
            {
                Array.Clear(tmp, 0, totalCount);
                ApplyLaplace(x, tmp, offset, mask, false);
                ApplyLaplace(tmp, y, offset, mask, true);
            }

            private void ApplyLaplace(float[] src, float[] dst, int offset, LaplaceMask mask, bool accumulate)
            {
                for (int y = 0; y < size; y++)
                {
                    int row = y * size;
                    for (int x = 0; x < size; x++)
                    {
                        int idx = offset + row + x;
                        float center = src[idx];
                        float sum = 0f;
                        int count = 0;

                        if (mask == LaplaceMask.Cross)
                        {
                            if (x > 0)
                            {
                                sum += src[idx - 1];
                                count++;
                            }
                            if (x < size - 1)
                            {
                                sum += src[idx + 1];
                                count++;
                            }
                            if (y > 0)
                            {
                                sum += src[idx - size];
                                count++;
                            }
                            if (y < size - 1)
                            {
                                sum += src[idx + size];
                                count++;
                            }
                        }
                        else
                        {
                            if (x > 0 && y > 0)
                            {
                                sum += src[idx - size - 1];
                                count++;
                            }
                            if (x < size - 1 && y > 0)
                            {
                                sum += src[idx - size + 1];
                                count++;
                            }
                            if (x > 0 && y < size - 1)
                            {
                                sum += src[idx + size - 1];
                                count++;
                            }
                            if (x < size - 1 && y < size - 1)
                            {
                                sum += src[idx + size + 1];
                                count++;
                            }
                        }

                        float value = count * center - sum;
                        if (accumulate)
                            dst[idx] += value;
                        else
                            dst[idx] = value;
                    }
                }
            }
        }
    }
}
