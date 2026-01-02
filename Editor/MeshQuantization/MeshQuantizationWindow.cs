using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshQuantization
{
    internal sealed class MeshQuantizationWindow : EditorWindow
    {
        [SerializeField] private Object source;
        [SerializeField] private string suffix = "_MQ";
        [SerializeField] private bool overwriteExisting;
        [SerializeField] private bool forceReadable = true;
        [SerializeField] private bool restoreReadable = true;
        [SerializeField] private MeshQuantizationSettings settings = new MeshQuantizationSettings();

        [MenuItem("Tools/Mesh Quantization/Quantize Meshes")]
        private static void ShowWindow()
        {
            GetWindow<MeshQuantizationWindow>("Mesh Quantization");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            source = EditorGUILayout.ObjectField("Source", source, typeof(Object), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                    source = Selection.activeObject;
                if (GUILayout.Button("Clear"))
                    source = null;
            }

            var meshes = CollectMeshes(source, out var sourcePath, out var sourceInfo);
            EditorGUILayout.LabelField($"Source: {sourceInfo}");
            EditorGUILayout.LabelField($"Meshes found: {meshes.Count}");
            if (meshes.Count == 0)
            {
                EditorGUILayout.HelpBox("Select a Mesh asset, a model asset (FBX/OBJ), or a GameObject with MeshFilter.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            forceReadable = EditorGUILayout.Toggle("Force Read/Write", forceReadable);
            restoreReadable = EditorGUILayout.Toggle("Restore Read/Write", restoreReadable);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            suffix = EditorGUILayout.TextField("Suffix", suffix);
            overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", overwriteExisting);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quantization", EditorStyles.boldLabel);
            settings.overwriteVertexColors = EditorGUILayout.Toggle("Overwrite Vertex Colors", settings.overwriteVertexColors);
            settings.generateMissingNormals = EditorGUILayout.Toggle("Generate Missing Normals", settings.generateMissingNormals);
            settings.generateMissingTangents = EditorGUILayout.Toggle("Generate Missing Tangents", settings.generateMissingTangents);
            settings.disableReadWrite = EditorGUILayout.Toggle("Disable Read/Write (Output)", settings.disableReadWrite);

            using (new EditorGUI.DisabledScope(meshes.Count == 0))
            {
                if (GUILayout.Button("Quantize & Save"))
                    QuantizeAndSave();
            }
        }

        private void QuantizeAndSave()
        {
            var meshes = CollectMeshes(source, out var sourcePath, out _);
            if (meshes.Count == 0)
                return;

            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError("Mesh Quantization: source asset path not found. Select a Mesh asset or a model asset in the Project.");
                return;
            }

            bool wasReadable = true;
            bool reimported = EnsureReadable(sourcePath, forceReadable, out wasReadable);
            if (reimported)
                meshes = CollectMeshes(source, out sourcePath, out _);

            string dir = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogError("Mesh Quantization: unable to resolve output folder.");
                return;
            }

            int saved = 0;
            foreach (var mesh in meshes)
            {
                if (mesh == null)
                    continue;

                if (MeshQuantizationUtility.IsAlreadyQuantized(mesh))
                {
                    Debug.LogWarning($"Mesh Quantization: mesh already quantized, skipped: {mesh.name}");
                    continue;
                }

                var copy = Instantiate(mesh);
                copy.name = mesh.name + suffix;
                if (!MeshQuantizationUtility.TryQuantize(copy, settings, sourcePath))
                {
                    DestroyImmediate(copy);
                    continue;
                }

                string targetPath = BuildTargetPath(dir, copy.name);
                if (overwriteExisting && AssetDatabase.LoadAssetAtPath<Mesh>(targetPath) != null)
                    AssetDatabase.DeleteAsset(targetPath);
                else
                    targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);

                AssetDatabase.CreateAsset(copy, targetPath);
                saved++;
            }

            AssetDatabase.SaveAssets();
            RestoreReadable(sourcePath, restoreReadable, wasReadable);
            Debug.Log($"Mesh Quantization: saved {saved} mesh asset(s) to {dir}.");
        }

        private static string BuildTargetPath(string dir, string meshName)
        {
            string fileName = string.IsNullOrEmpty(meshName) ? "Mesh_MQ" : meshName;
            string path = Path.Combine(dir, fileName + ".asset");
            return path.Replace('\\', '/');
        }

        private static List<Mesh> CollectMeshes(Object target, out string sourcePath, out string sourceInfo)
        {
            sourcePath = null;
            sourceInfo = "None";
            var result = new List<Mesh>();
            if (target == null)
                return result;

            var unique = new HashSet<Mesh>();
            if (target is Mesh mesh)
            {
                sourceInfo = "Mesh asset";
                sourcePath = AssetDatabase.GetAssetPath(mesh);
                Add(unique, result, mesh);
                return result;
            }

            if (target is GameObject go)
            {
                sourceInfo = "GameObject";
                var filters = go.GetComponentsInChildren<MeshFilter>(true);
                foreach (var filter in filters)
                    Add(unique, result, filter.sharedMesh);

                var path = ResolvePathFromMeshes(result);
                sourcePath = path;
                return result;
            }

            var assetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(assetPath))
            {
                sourceInfo = $"Asset ({Path.GetFileName(assetPath)})";
                sourcePath = assetPath;
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var asset in assets)
                    Add(unique, result, asset as Mesh);
            }

            return result;
        }

        private static void Add(HashSet<Mesh> unique, List<Mesh> list, Mesh mesh)
        {
            if (mesh == null)
                return;
            if (unique.Add(mesh))
                list.Add(mesh);
        }

        private static string ResolvePathFromMeshes(List<Mesh> meshes)
        {
            foreach (var mesh in meshes)
            {
                var path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            return null;
        }

        private static bool EnsureReadable(string sourcePath, bool force, out bool wasReadable)
        {
            wasReadable = true;
            if (!force || string.IsNullOrEmpty(sourcePath))
                return false;

            if (!(AssetImporter.GetAtPath(sourcePath) is ModelImporter modelImporter))
                return false;

            wasReadable = modelImporter.isReadable;
            if (modelImporter.isReadable)
                return false;

            MeshQuantizationAutoProcessor.SuppressAutoImport = true;
            try
            {
                modelImporter.isReadable = true;
                modelImporter.SaveAndReimport();
            }
            finally
            {
                MeshQuantizationAutoProcessor.SuppressAutoImport = false;
            }
            return true;
        }

        private static void RestoreReadable(string sourcePath, bool restore, bool wasReadable)
        {
            if (!restore || wasReadable || string.IsNullOrEmpty(sourcePath))
                return;

            if (!(AssetImporter.GetAtPath(sourcePath) is ModelImporter modelImporter))
                return;

            if (!modelImporter.isReadable)
                return;

            MeshQuantizationAutoProcessor.SuppressAutoImport = true;
            try
            {
                modelImporter.isReadable = false;
                modelImporter.SaveAndReimport();
            }
            finally
            {
                MeshQuantizationAutoProcessor.SuppressAutoImport = false;
            }
        }
    }
}
