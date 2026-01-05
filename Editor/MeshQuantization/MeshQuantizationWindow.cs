using UnityEditor;
using UnityEngine;

namespace MeshQuantization
{
    internal sealed class MeshQuantizationWindow : EditorWindow
    {
        private const string PrefKeyPrefix = "MeshQuantizationWindow.";
        private const string PrefSuffix = PrefKeyPrefix + "Suffix";
        private const string PrefOverwriteVertexColors = PrefKeyPrefix + "OverwriteVertexColors";
        private const string PrefDisableReadWrite = PrefKeyPrefix + "DisableReadWrite";
        private const string PrefAutoEnableReadWrite = PrefKeyPrefix + "AutoEnableReadWrite";
        private const string PrefBakeQuantizedMeshAssets = PrefKeyPrefix + "BakeQuantizedMeshAssets";

        [SerializeField] private string suffix = "_MQ";
        [SerializeField] private bool autoEnableReadWrite = true;
        [SerializeField] private bool bakeQuantizedMeshAssets = true;
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

        [MenuItem("Tools/Mesh Quantization/Settings")]
        private static void ShowWindow()
        {
            GetWindow<MeshQuantizationWindow>("Mesh Quantization");
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Auto-Import", EditorStyles.boldLabel);
            suffix = EditorGUILayout.TextField("Filename Suffix", suffix);
            autoEnableReadWrite = EditorGUILayout.Toggle("Auto Enable Read/Write", autoEnableReadWrite);
            bakeQuantizedMeshAssets = EditorGUILayout.Toggle("Bake Quantized Mesh Assets", bakeQuantizedMeshAssets);
            EditorGUILayout.HelpBox("FBX/OBJ/DAE/BLEND with this suffix will be quantized on import. Quantized meshes are saved as .asset files next to the model.", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quantization", EditorStyles.boldLabel);
            settings.overwriteVertexColors = EditorGUILayout.Toggle("Overwrite Vertex Colors", settings.overwriteVertexColors);
            settings.disableReadWrite = EditorGUILayout.Toggle("Disable Read/Write (Output)", settings.disableReadWrite);

            if (EditorGUI.EndChangeCheck())
                SavePrefs();
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
            autoEnableReadWrite = EditorPrefs.GetBool(PrefAutoEnableReadWrite, autoEnableReadWrite);
            bakeQuantizedMeshAssets = EditorPrefs.GetBool(PrefBakeQuantizedMeshAssets, bakeQuantizedMeshAssets);
            settings.overwriteVertexColors = EditorPrefs.GetBool(PrefOverwriteVertexColors, settings.overwriteVertexColors);
            settings.disableReadWrite = EditorPrefs.GetBool(PrefDisableReadWrite, settings.disableReadWrite);
        }

        private void SavePrefs()
        {
            if (settings == null)
                settings = new MeshQuantizationSettings();

            EditorPrefs.SetString(PrefSuffix, suffix ?? string.Empty);
            EditorPrefs.SetBool(PrefAutoEnableReadWrite, autoEnableReadWrite);
            EditorPrefs.SetBool(PrefBakeQuantizedMeshAssets, bakeQuantizedMeshAssets);
            EditorPrefs.SetBool(PrefOverwriteVertexColors, settings.overwriteVertexColors);
            EditorPrefs.SetBool(PrefDisableReadWrite, settings.disableReadWrite);
        }
    }
}
