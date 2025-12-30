param(
    [string]$OutputPath = "Textures/lut_ggx.png",
    [string]$ResourceCopyPath = "Runtime/Resources/GltfBrdfLut.png",
    [int]$Size = 512,
    [int]$Samples = 1024
)

Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies "System.Drawing.dll" -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class BrdfLutGenerator
{
    struct Vec2
    {
        public double x;
        public double y;
        public Vec2(double x, double y) { this.x = x; this.y = y; }
    }

    struct Vec3
    {
        public double x;
        public double y;
        public double z;
        public Vec3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3 operator *(Vec3 a, double s) { return new Vec3(a.x * s, a.y * s, a.z * s); }
        public static Vec3 operator *(double s, Vec3 a) { return new Vec3(a.x * s, a.y * s, a.z * s); }
    }

    static double Dot(Vec3 a, Vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

    static Vec3 Cross(Vec3 a, Vec3 b)
    {
        return new Vec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x
        );
    }

    static Vec3 Normalize(Vec3 v)
    {
        double len = Math.Sqrt(Dot(v, v));
        return (len > 0.0) ? v * (1.0 / len) : new Vec3(0.0, 0.0, 0.0);
    }

    static double Fract(double x) { return x - Math.Floor(x); }

    static double Random(double x, double y)
    {
        double a = 12.9898;
        double b = 78.233;
        double c = 43758.5453;
        double dt = x * a + y * b;
        double sn = dt % 3.14;
        return Fract(Math.Sin(sn) * c);
    }

    static Vec2 Hammersley2d(uint i, uint n)
    {
        uint bits = (i << 16) | (i >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        double rdi = bits * 2.3283064365386963e-10;
        return new Vec2((double)i / n, rdi);
    }

    static Vec3 ImportanceSampleGGX(double xi1, double xi2, double roughness, Vec3 normal)
    {
        double alpha = roughness * roughness;
        double phi = 2.0 * Math.PI * xi1 + Random(normal.x, normal.z) * 0.1;
        double cosTheta = Math.Sqrt((1.0 - xi2) / (1.0 + (alpha * alpha - 1.0) * xi2));
        double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - cosTheta * cosTheta));
        Vec3 h = new Vec3(sinTheta * Math.Cos(phi), sinTheta * Math.Sin(phi), cosTheta);

        Vec3 up = Math.Abs(normal.z) < 0.999 ? new Vec3(0.0, 0.0, 1.0) : new Vec3(1.0, 0.0, 0.0);
        Vec3 tangentX = Normalize(Cross(up, normal));
        Vec3 tangentY = Normalize(Cross(normal, tangentX));
        Vec3 sampleVec = tangentX * h.x + tangentY * h.y + normal * h.z;
        return Normalize(sampleVec);
    }

    static double G_SchlicksmithGGX(double dotNL, double dotNV, double roughness)
    {
        double k = (roughness * roughness) / 2.0;
        double gL = dotNL / (dotNL * (1.0 - k) + k);
        double gV = dotNV / (dotNV * (1.0 - k) + k);
        return gL * gV;
    }

    static Vec2 BRDF(double noV, double roughness, int numSamples)
    {
        Vec3 n = new Vec3(0.0, 0.0, 1.0);
        Vec3 v = new Vec3(Math.Sqrt(Math.Max(0.0, 1.0 - noV * noV)), 0.0, noV);

        double a = 0.0;
        double b = 0.0;
        for (uint i = 0; i < numSamples; i++)
        {
            Vec2 xi = Hammersley2d(i, (uint)numSamples);
            Vec3 h = ImportanceSampleGGX(xi.x, xi.y, roughness, n);
            Vec3 l = h * (2.0 * Dot(v, h)) - v;

            double dotNL = Math.Max(Dot(n, l), 0.0);
            double dotNV = Math.Max(Dot(n, v), 0.0);
            double dotVH = Math.Max(Dot(v, h), 0.0);
            double dotNH = Math.Max(Dot(h, n), 0.0);

            if (dotNL > 0.0)
            {
                double g = G_SchlicksmithGGX(dotNL, dotNV, roughness);
                double gVis = (g * dotVH) / (dotNH * dotNV);
                double fc = Math.Pow(1.0 - dotVH, 5.0);
                a += (1.0 - fc) * gVis;
                b += fc * gVis;
            }
        }

        return new Vec2(a / numSamples, b / numSamples);
    }

    public static void Generate(string outputPath, int size, int numSamples)
    {
        int width = size;
        int height = size;

        using (var bmp = new Bitmap(width, height, PixelFormat.Format48bppRgb))
        {
            var rect = new Rectangle(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format48bppRgb);
            int stride = data.Stride;
            byte[] buffer = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                double v = (y + 0.5) / height;
                double roughness = 1.0 - v;
                for (int x = 0; x < width; x++)
                {
                    double u = (x + 0.5) / width;
                    double noV = u;
                    Vec2 lut = BRDF(noV, roughness, numSamples);

                    double r = Math.Min(Math.Max(lut.x, 0.0), 1.0);
                    double g = Math.Min(Math.Max(lut.y, 0.0), 1.0);
                    ushort r16 = (ushort)Math.Round(r * 65535.0);
                    ushort g16 = (ushort)Math.Round(g * 65535.0);

                    int index = y * stride + x * 6;
                    buffer[index + 0] = 0;
                    buffer[index + 1] = 0;
                    buffer[index + 2] = (byte)(g16 & 0xFF);
                    buffer[index + 3] = (byte)(g16 >> 8);
                    buffer[index + 4] = (byte)(r16 & 0xFF);
                    buffer[index + 5] = (byte)(r16 >> 8);
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            bmp.Save(outputPath, ImageFormat.Png);
        }
    }
}
'@

[BrdfLutGenerator]::Generate($OutputPath, $Size, $Samples)

if (-not [string]::IsNullOrWhiteSpace($ResourceCopyPath))
{
    $destDir = Split-Path -Path $ResourceCopyPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($destDir))
    {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }

    Copy-Item -Path $OutputPath -Destination $ResourceCopyPath -Force
}
