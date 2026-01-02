using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshQuantization
{
    internal sealed class MeshQuantizationAutoProcessor : AssetPostprocessor
    {
        internal static bool SuppressAutoImport;
        private static bool warnedMissingProfileSet;
        private static bool warnedMultipleProfileSets;

        private void OnPreprocessModel()
        {
            if (SuppressAutoImport)
                return;

            if (!TryGetSettings(out var settings))
                return;

            MeshQuantizationDebug.Log($"[MQ] Preprocess model: {assetPath}");
            if (assetImporter is ModelImporter modelImporter && !modelImporter.isReadable)
            {
                modelImporter.isReadable = true;
                MeshQuantizationDebug.Log($"[MQ] Enabled Read/Write for quantization: {assetPath}");
            }
        }

        private void OnPostprocessMesh(Mesh mesh)
        {
            if (SuppressAutoImport)
                return;

            if (!TryGetSettings(out var settings))
                return;

            if (MeshQuantizationUtility.IsAlreadyQuantized(mesh))
            {
                MeshQuantizationDebug.Log($"[MQ] Skip already quantized mesh: {assetPath} ({mesh.name})");
                return;
            }

            MeshQuantizationDebug.Log($"[MQ] Postprocess mesh: {assetPath} ({mesh.name}) vtx={mesh.vertexCount}");
            MeshQuantizationUtility.TryQuantize(mesh, settings, assetPath);
        }

        private void OnPostprocessModel(GameObject gameObject)
        {
            if (SuppressAutoImport)
                return;

            if (!TryGetSettings(out var settings))
                return;

            MeshQuantizationDebug.Log($"[MQ] Postprocess model: {assetPath} ({gameObject.name})");
            var filters = gameObject.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                if (MeshQuantizationUtility.IsAlreadyQuantized(mesh))
                    continue;

                MeshQuantizationDebug.Log($"[MQ] Quantize mesh from model: {mesh.name}");
                MeshQuantizationUtility.TryQuantize(mesh, settings, assetPath);
            }

            var skinned = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in skinned)
            {
                if (renderer == null || renderer.sharedMesh == null)
                    continue;
                MeshQuantizationDebug.Log($"[MQ] Skipped skinned mesh: {renderer.sharedMesh.name}");
            }
        }

        private bool TryGetSettings(out MeshQuantizationSettings settings)
        {
            settings = null;
            var profileSet = FindProfileSet();
            if (profileSet == null)
            {
                if (!warnedMissingProfileSet)
                {
                    Debug.LogWarning("Mesh quantization auto-import: no Profile Set asset found. Create one via Tools/Mesh Quantization/Create Profile Set.");
                    warnedMissingProfileSet = true;
                }
                MeshQuantizationDebug.Log($"[MQ] Skipped: no profile set for {assetPath}");
                return false;
            }

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (!profileSet.TryGetSettingsForFileName(fileName, out settings))
            {
                MeshQuantizationDebug.Log($"[MQ] Skipped: file name '{fileName}' does not match profile suffixes.");
                return false;
            }

            return settings != null;
        }

        private static MeshQuantizationProfileSet FindProfileSet()
        {
            var guids = AssetDatabase.FindAssets("t:MeshQuantizationProfileSet");
            if (guids.Length == 0)
                return null;

            if (guids.Length > 1 && !warnedMultipleProfileSets)
            {
                Debug.LogWarning("Mesh quantization auto-import: multiple Profile Set assets found. Using the first.");
                warnedMultipleProfileSets = true;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<MeshQuantizationProfileSet>(path);
        }
    }
}
