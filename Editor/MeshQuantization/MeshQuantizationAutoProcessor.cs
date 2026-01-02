using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshQuantization
{
    internal sealed class MeshQuantizationAutoProcessor : AssetPostprocessor
    {
        private const string PrefKeyPrefix = "MeshQuantizationWindow.";
        private const string PrefSuffix = PrefKeyPrefix + "Suffix";
        private const string PrefOverwriteVertexColors = PrefKeyPrefix + "OverwriteVertexColors";
        private const string PrefDisableReadWrite = PrefKeyPrefix + "DisableReadWrite";

        private static readonly HashSet<string> ReadableRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string GetSuffix()
        {
            var suffix = EditorPrefs.GetString(PrefSuffix, "_MQ");
            return string.IsNullOrEmpty(suffix) ? "_MQ" : suffix;
        }

        private static bool IsModelAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            string extension = Path.GetExtension(assetPath);
            return extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".dae", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".blend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldQuantize(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string suffix = GetSuffix();
            if (string.IsNullOrEmpty(suffix))
                return false;

            return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static MeshQuantizationSettings CreateSettings()
        {
            return new MeshQuantizationSettings
            {
                overwriteVertexColors = EditorPrefs.GetBool(PrefOverwriteVertexColors, true),
                generateMissingNormals = false,
                generateMissingTangents = false,
                disableReadWrite = EditorPrefs.GetBool(PrefDisableReadWrite, true),
            };
        }

        private void OnPreprocessModel()
        {
            if (!IsModelAsset(assetPath) || !ShouldQuantize(assetPath))
                return;

            if (assetImporter is ModelImporter modelImporter && !modelImporter.isReadable)
                modelImporter.isReadable = true;
        }

        private void OnPostprocessMesh(Mesh mesh)
        {
            if (!IsModelAsset(assetPath) || !ShouldQuantize(assetPath))
                return;

            if (mesh == null)
                return;

            if (MeshQuantizationUtility.IsAlreadyQuantized(mesh))
                return;

            var settings = CreateSettings();
            if (MeshQuantizationUtility.TryQuantize(mesh, settings, assetPath))
                Debug.Log($"Mesh quantization auto-import: {assetPath} ({mesh.name})");
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets == null || importedAssets.Length == 0)
                return;

            foreach (var path in importedAssets)
            {
                if (!IsModelAsset(path) || !ShouldQuantize(path))
                    continue;

                if (EnsureReadable(path))
                    continue;

                QuantizeImportedMeshes(path);
            }
        }

        private static bool EnsureReadable(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter modelImporter))
                return false;

            if (modelImporter.isReadable)
                return false;

            if (!ReadableRequested.Add(assetPath))
                return false;

            modelImporter.isReadable = true;
            modelImporter.SaveAndReimport();
            Debug.Log($"Mesh quantization auto-import: enabled Read/Write and reimporting {assetPath}");
            return true;
        }

        private static void QuantizeImportedMeshes(string assetPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning($"Mesh quantization auto-import: no assets at {assetPath}");
                ReadableRequested.Remove(assetPath);
                return;
            }

            int totalMeshes = 0;
            int quantized = 0;
            int skipped = 0;
            int failed = 0;
            var settings = CreateSettings();

            foreach (var asset in assets)
            {
                if (!(asset is Mesh mesh))
                    continue;

                totalMeshes++;
                if (MeshQuantizationUtility.IsAlreadyQuantized(mesh))
                {
                    skipped++;
                    continue;
                }

                if (MeshQuantizationUtility.TryQuantize(mesh, settings, assetPath))
                {
                    quantized++;
                    EditorUtility.SetDirty(mesh);
                }
                else
                {
                    failed++;
                }
            }

            ReadableRequested.Remove(assetPath);

            if (settings.disableReadWrite && (quantized > 0 || skipped > 0))
                DisableReadWrite(assetPath);

            if (quantized > 0)
                Debug.Log($"Mesh quantization auto-import: {assetPath} meshes={totalMeshes} quantized={quantized} skipped={skipped} failed={failed}");
            else if (failed > 0)
                Debug.LogWarning($"Mesh quantization auto-import: {assetPath} meshes={totalMeshes} quantized={quantized} skipped={skipped} failed={failed}");
        }

        private static void DisableReadWrite(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter modelImporter))
                return;

            if (!modelImporter.isReadable)
                return;

            modelImporter.isReadable = false;
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            Debug.Log($"Mesh quantization auto-import: disabled Read/Write for {assetPath}");
        }
    }
}
