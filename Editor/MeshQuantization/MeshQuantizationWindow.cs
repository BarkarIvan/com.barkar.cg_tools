using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshQuantization
{
    internal sealed class MeshQuantizationWindow : EditorWindow
    {
        private const string PrefKeyPrefix = "MeshQuantizationWindow.";
        private const string PrefSuffix = PrefKeyPrefix + "Suffix";
        private const string PrefOverwriteExisting = PrefKeyPrefix + "OverwriteExisting";
        private const string PrefForceReadable = PrefKeyPrefix + "ForceReadable";
        private const string PrefRestoreReadable = PrefKeyPrefix + "RestoreReadable";
        private const string PrefOverwriteVertexColors = PrefKeyPrefix + "OverwriteVertexColors";
        private const string PrefDisableReadWrite = PrefKeyPrefix + "DisableReadWrite";

        [SerializeField] private Object source;
        [SerializeField] private string suffix = "_MQ";
        [SerializeField] private bool overwriteExisting;
        [SerializeField] private bool forceReadable = true;
        [SerializeField] private bool restoreReadable = true;
        [SerializeField] private MeshQuantizationSettings settings = new MeshQuantizationSettings();

        private void OnEnable()
        {
            InitializeSettings();
            LoadPrefs();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        [MenuItem("Tools/Mesh Quantization/Quantize Meshes")]
        private static void ShowWindow()
        {
            GetWindow<MeshQuantizationWindow>("Mesh Quantization");
        }

        [MenuItem("Assets/Mesh Quantization/Quantize Meshes")]
        private static void QuantizeSelection()
        {
            var target = Selection.activeObject;
            if (target == null)
                return;

            MeshQuantizationWindow runner = null;
            bool destroyAfter = false;
            var windows = Resources.FindObjectsOfTypeAll<MeshQuantizationWindow>();
            if (windows != null && windows.Length > 0)
            {
                runner = windows[0];
            }
            else
            {
                runner = CreateInstance<MeshQuantizationWindow>();
                destroyAfter = true;
            }

            runner.InitializeSettings();
            if (destroyAfter)
                runner.LoadPrefs();

            runner.source = target;
            runner.QuantizeAndSave();

            if (destroyAfter)
                DestroyImmediate(runner);
        }

        [MenuItem("Assets/Mesh Quantization/Quantize Meshes", true)]
        private static bool QuantizeSelectionValidate()
        {
            var target = Selection.activeObject;
            if (target == null)
                return false;

            return CollectMeshes(target, out _, out _).Count > 0;
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
            settings.disableReadWrite = EditorGUILayout.Toggle("Disable Read/Write (Output)", settings.disableReadWrite);

            using (new EditorGUI.DisabledScope(meshes.Count == 0))
            {
                if (GUILayout.Button("Quantize & Save"))
                    QuantizeAndSave();
            }
        }

        private void QuantizeAndSave()
        {
            SavePrefs();
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
                Mesh existing = overwriteExisting ? AssetDatabase.LoadAssetAtPath<Mesh>(targetPath) : null;
                if (existing != null)
                {
                    EditorUtility.CopySerialized(copy, existing);
                    existing.name = copy.name;
                    EditorUtility.SetDirty(existing);
                    DestroyImmediate(copy);
                    saved++;
                    continue;
                }

                if (!overwriteExisting)
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

            modelImporter.isReadable = true;
            modelImporter.SaveAndReimport();
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

            modelImporter.isReadable = false;
            modelImporter.SaveAndReimport();
        }

        private void InitializeSettings()
        {
            if (settings == null)
                settings = new MeshQuantizationSettings();
            settings.generateMissingNormals = false;
            settings.generateMissingTangents = false;
        }

        private void LoadPrefs()
        {
            suffix = EditorPrefs.GetString(PrefSuffix, suffix);
            overwriteExisting = EditorPrefs.GetBool(PrefOverwriteExisting, overwriteExisting);
            forceReadable = EditorPrefs.GetBool(PrefForceReadable, forceReadable);
            restoreReadable = EditorPrefs.GetBool(PrefRestoreReadable, restoreReadable);
            settings.overwriteVertexColors = EditorPrefs.GetBool(PrefOverwriteVertexColors, settings.overwriteVertexColors);
            settings.disableReadWrite = EditorPrefs.GetBool(PrefDisableReadWrite, settings.disableReadWrite);
        }

        private void SavePrefs()
        {
            if (settings == null)
                settings = new MeshQuantizationSettings();

            EditorPrefs.SetString(PrefSuffix, suffix ?? string.Empty);
            EditorPrefs.SetBool(PrefOverwriteExisting, overwriteExisting);
            EditorPrefs.SetBool(PrefForceReadable, forceReadable);
            EditorPrefs.SetBool(PrefRestoreReadable, restoreReadable);
            EditorPrefs.SetBool(PrefOverwriteVertexColors, settings.overwriteVertexColors);
            EditorPrefs.SetBool(PrefDisableReadWrite, settings.disableReadWrite);
        }
    }
}
