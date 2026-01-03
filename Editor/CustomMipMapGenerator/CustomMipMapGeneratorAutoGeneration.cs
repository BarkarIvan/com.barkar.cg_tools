using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CustomMipMapGenerator
{
    internal static class CustomMipMapGeneratorAutoGeneration
    {
        private static readonly HashSet<string> InProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SuppressedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] SupportedExtensions = { ".png", ".tif", ".tiff" };
        private static bool clearScheduled;

        public static bool TryGenerateForAsset(string assetPath, CustomMipMapGeneratorProfileSet profileSet, ComputeShader shader)
        {
            if (string.IsNullOrEmpty(assetPath) || profileSet == null || shader == null)
                return false;
            if (IsSuppressed(assetPath))
                return false;
            if (InProgress.Contains(assetPath))
                return false;
            if (!HasSupportedExtension(assetPath))
                return false;

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (!profileSet.TryGetSettingsForFileName(fileName, out var settings))
                return false;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                return false;

            try
            {
                InProgress.Add(assetPath);
                SuppressPath(assetPath);
                SuppressPath(BuildCustomMipPath(assetPath));
                CustomMipMapGeneratorGpu.GenerateCustomMipFile(texture, settings, shader);
            }
            finally
            {
                InProgress.Remove(assetPath);
            }

            return true;
        }

        public static int RegenerateAll(CustomMipMapGeneratorProfileSet profileSet, ComputeShader shader)
        {
            if (profileSet == null || shader == null)
                return 0;

            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                return 0;

            int generated = 0;
            foreach (var extension in SupportedExtensions)
            {
                var files = Directory.GetFiles(dataPath, "*" + extension, SearchOption.AllDirectories);
                foreach (var fullPath in files)
                {
                    var assetPath = ToAssetPath(fullPath);
                    if (string.IsNullOrEmpty(assetPath))
                        continue;
                    if (TryGenerateForAsset(assetPath, profileSet, shader))
                        generated++;
                }
            }

            return generated;
        }

        public static bool IsSuppressed(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            return SuppressedPaths.Contains(assetPath);
        }

        private static void SuppressPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            SuppressedPaths.Add(assetPath);
            if (clearScheduled)
                return;

            clearScheduled = true;
            EditorApplication.delayCall += ClearSuppressedPaths;
        }

        private static void ClearSuppressedPaths()
        {
            SuppressedPaths.Clear();
            clearScheduled = false;
        }

        private static string BuildCustomMipPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var dir = Path.GetDirectoryName(assetPath);
            var baseName = Path.GetFileNameWithoutExtension(assetPath);
            var safeDir = string.IsNullOrEmpty(dir) ? "Assets" : dir.Replace('\\', '/');
            return safeDir + "/" + baseName + CustomMipMapGeneratorMipFile.Extension;
        }

        private static bool HasSupportedExtension(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            foreach (var extension in SupportedExtensions)
            {
                if (assetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string ToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;

            var dataPath = Application.dataPath.Replace('\\', '/');
            var normalized = fullPath.Replace('\\', '/');
            if (!normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return "Assets" + normalized.Substring(dataPath.Length);
        }
    }
}
