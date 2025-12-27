using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace CustomMipMapGenerator
{
    internal static class CustomMipMapGeneratorMipFile
    {
        public const string Extension = ".cmips";
        private const uint Magic = 0x50494D43; // CMIP
        private const int Version = 1;
        private const int HeaderSize = 24;

        internal struct Header
        {
            public int width;
            public int height;
            public int mipCount;
            public TextureKind textureKind;
        }

        public static bool TryWrite(string assetPath, Texture2D texture, TextureKind textureKind, out string error)
        {
            error = null;
            if (texture == null)
            {
                error = "Missing texture data.";
                return false;
            }

            if (texture.format != TextureFormat.RGBA32)
            {
                error = $"Expected RGBA32 source data but got {texture.format}.";
                return false;
            }

            int width = texture.width;
            int height = texture.height;
            int mipCount = texture.mipmapCount;
            long expectedSize = ComputeDataSize(width, height, mipCount);
            if (expectedSize <= 0 || expectedSize > int.MaxValue)
            {
                error = "Mip data size is invalid or too large.";
                return false;
            }

            var rawData = texture.GetRawTextureData<byte>();
            if (rawData.Length != expectedSize)
            {
                error = $"Mip data size mismatch. Expected {expectedSize} bytes, got {rawData.Length}.";
                return false;
            }

            try
            {
                using (var stream = new FileStream(assetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write(mipCount);
                    writer.Write((byte)textureKind);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);

                    var rawBytes = rawData.ToArray();
                    writer.Write(rawBytes);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        public static bool TryRead(string assetPath, out Header header, out byte[] rawData, out string error)
        {
            header = default;
            rawData = null;
            error = null;

            if (!File.Exists(assetPath))
            {
                error = "File does not exist.";
                return false;
            }

            try
            {
                using (var stream = new FileStream(assetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < HeaderSize)
                    {
                        error = "File is too small.";
                        return false;
                    }

                    uint magic = reader.ReadUInt32();
                    if (magic != Magic)
                    {
                        error = "Not a CMIP file.";
                        return false;
                    }

                    int version = reader.ReadInt32();
                    if (version != Version)
                    {
                        error = $"Unsupported version {version}.";
                        return false;
                    }

                    header.width = reader.ReadInt32();
                    header.height = reader.ReadInt32();
                    header.mipCount = reader.ReadInt32();
                    header.textureKind = (TextureKind)reader.ReadByte();
                    reader.ReadBytes(3);

                    if (header.width <= 0 || header.height <= 0 || header.mipCount <= 0)
                    {
                        error = "Invalid header dimensions.";
                        return false;
                    }

                    long expectedSize = ComputeDataSize(header.width, header.height, header.mipCount);
                    long remaining = stream.Length - HeaderSize;
                    if (expectedSize <= 0 || remaining < expectedSize || expectedSize > int.MaxValue)
                    {
                        error = "File payload size mismatch.";
                        return false;
                    }

                    rawData = reader.ReadBytes((int)expectedSize);
                    if (rawData.Length != expectedSize)
                    {
                        error = "Failed to read mip payload.";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        private static long ComputeDataSize(int width, int height, int mipCount)
        {
            long total = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Mathf.Max(1, width >> mip);
                int mipHeight = Mathf.Max(1, height >> mip);
                total += (long)mipWidth * mipHeight * 4;
            }

            return total;
        }
    }
}
