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
        private const string PrefAutoEnableReadWrite = PrefKeyPrefix + "AutoEnableReadWrite";
        private const string PrefBakeQuantizedMeshAssets = PrefKeyPrefix + "BakeQuantizedMeshAssets";

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

            if (!GetAutoEnableReadWrite())
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

            if (GetBakeQuantizedMeshAssets())
                return;

            var settings = CreateSettings();
            if (MeshQuantizationUtility.TryQuantizeImport(mesh, settings, assetPath))
                Debug.Log($"Mesh quantization auto-import: {assetPath} ({mesh.name})");
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!GetBakeQuantizedMeshAssets())
                return;

            if (IsAssetImportWorker())
                return;

            if (importedAssets == null || importedAssets.Length == 0)
                return;

            for (int i = 0; i < importedAssets.Length; ++i)
            {
                string importedPath = importedAssets[i];
                if (!IsModelAsset(importedPath) || !ShouldQuantize(importedPath))
                    continue;

                BakeQuantizedMeshAssets(importedPath);
            }
        }

        private static bool GetAutoEnableReadWrite()
        {
            return EditorPrefs.GetBool(PrefAutoEnableReadWrite, true);
        }

        private static bool GetBakeQuantizedMeshAssets()
        {
            return EditorPrefs.GetBool(PrefBakeQuantizedMeshAssets, true);
        }

        private static void BakeQuantizedMeshAssets(string assetPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0)
                return;

            var settings = CreateSettings();
            string directory = Path.GetDirectoryName(assetPath) ?? "Assets";
            directory = directory.Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(baseName))
                baseName = "Mesh";

            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int created = 0;
            int updated = 0;
            int skipped = 0;
            int failed = 0;

            foreach (var asset in assets)
            {
                if (!(asset is Mesh mesh))
                    continue;

                if (mesh.vertexCount == 0)
                {
                    skipped++;
                    continue;
                }

                if (MeshQuantizationUtility.IsAlreadyQuantized(mesh))
                {
                    skipped++;
                    continue;
                }

                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"Mesh quantization skipped (Read/Write disabled): {assetPath} ({mesh.name})");
                    skipped++;
                    continue;
                }

                string meshName = string.IsNullOrEmpty(mesh.name) ? "Mesh" : mesh.name;
                string safeMeshName = SanitizeFileName(meshName);
                if (string.IsNullOrEmpty(safeMeshName))
                    safeMeshName = "Mesh";

                string key = $"{baseName}_{safeMeshName}";
                int count = 0;
                if (nameCounts.TryGetValue(key, out count))
                    count++;
                nameCounts[key] = count;

                string fileName = count == 0 ? key : $"{key}_{count}";
                string outputPath = $"{directory}/{fileName}.asset";
                outputPath = outputPath.Replace('\\', '/');

                var quantizedMesh = UnityEngine.Object.Instantiate(mesh);
                quantizedMesh.name = meshName;
                if (!MeshQuantizationUtility.TryQuantize(quantizedMesh, settings, assetPath))
                {
                    UnityEngine.Object.DestroyImmediate(quantizedMesh);
                    failed++;
                    continue;
                }

                var existingAsset = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
                if (existingAsset != null)
                {
                    EditorUtility.CopySerialized(quantizedMesh, existingAsset);
                    EditorUtility.SetDirty(existingAsset);
                    UnityEngine.Object.DestroyImmediate(quantizedMesh);
                    updated++;
                }
                else
                {
                    var existingObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputPath);
                    if (existingObject != null)
                    {
                        UnityEngine.Object.DestroyImmediate(quantizedMesh);
                        Debug.LogWarning($"Mesh quantization skipped (asset path in use): {outputPath}");
                        failed++;
                        continue;
                    }

                    AssetDatabase.CreateAsset(quantizedMesh, outputPath);
                    created++;
                }
            }

            if (created > 0 || updated > 0)
                AssetDatabase.SaveAssets();

            if (created > 0 || updated > 0 || failed > 0)
            {
                Debug.Log($"Mesh quantization baked: {assetPath} (created {created}, updated {updated}, skipped {skipped}, failed {failed})");
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] chars = value.ToCharArray();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; ++i)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private static bool IsAssetImportWorker()
        {
            var method = typeof(AssetDatabase).GetMethod("IsAssetImportWorkerProcess");
            if (method != null && method.ReturnType == typeof(bool))
                return (bool)method.Invoke(null, null);

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], "-assetImportWorker", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
