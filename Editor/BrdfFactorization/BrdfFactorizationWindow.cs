using UnityEditor;
using UnityEngine;

namespace BrdfFactorization
{
    internal sealed class BrdfFactorizationWindow : EditorWindow
    {
        private const string PrefKeySettings = "BrdfFactorizationWindow.Settings";
        private const string PrefKeyAssetPath = "BrdfFactorizationWindow.AssetPath";
        private const string DefaultAssetPath = "Assets/BrdfFactorizationAsset.asset";

        [SerializeField] private BrdfFactorizationSettings settings = new BrdfFactorizationSettings();
        [SerializeField] private string assetPath = DefaultAssetPath;

        [MenuItem("Tools/BRDF Factorization/Bake Factor Textures")]
        private static void ShowWindow()
        {
            GetWindow<BrdfFactorizationWindow>("BRDF Factorization");
        }

        private void OnEnable()
        {
            if (settings == null)
                settings = new BrdfFactorizationSettings();
            LoadPrefs();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        private void OnGUI()
        {
            GUILayout.Label("BRDF Factorization (glTF base)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bakes factor textures for the base glTF BRDF (Burley + GGX, no extensions). Use ShaderLibrary/BrdfFactorization.hlsl to sample p/q with the provided scale.", MessageType.Info);

            EditorGUILayout.Space();
            GUILayout.Label("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            assetPath = EditorGUILayout.TextField("Asset Path", assetPath);
            if (GUILayout.Button("Pick", GUILayout.Width(64f)))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save BRDF Factorization Asset", "BrdfFactorizationAsset", "asset", "Pick output asset path.");
                if (!string.IsNullOrEmpty(path))
                    assetPath = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            GUILayout.Label("Material", EditorStyles.boldLabel);
            settings.albedo = EditorGUILayout.ColorField("Albedo", settings.albedo);
            settings.metallic = EditorGUILayout.Slider("Metallic", settings.metallic, 0f, 1f);
            settings.roughness = EditorGUILayout.Slider("Roughness", settings.roughness, 0f, 1f);
            settings.specularWeight = EditorGUILayout.Slider("Specular Weight", settings.specularWeight, 0f, 1f);
            settings.specularF0 = EditorGUILayout.Slider("Specular F0", settings.specularF0, 0f, 1f);

            EditorGUILayout.Space();
            GUILayout.Label("Factorization", EditorStyles.boldLabel);
            settings.textureSize = Mathf.Clamp(EditorGUILayout.IntField("Texture Size", settings.textureSize), 4, 1024);
            settings.sampleCount = Mathf.Clamp(EditorGUILayout.IntField("Sample Count", settings.sampleCount), 256, 1_000_000);
            settings.smoothness = EditorGUILayout.Slider("Smoothness (Lambda)", settings.smoothness, 0f, 2f);
            settings.maxIterations = Mathf.Clamp(EditorGUILayout.IntField("Max Iterations", settings.maxIterations), 1, 10000);
            settings.tolerance = Mathf.Clamp(EditorGUILayout.FloatField("Tolerance", settings.tolerance), 1e-8f, 1f);

            EditorGUILayout.Space();
            if (GUILayout.Button("Bake"))
            {
                if (string.IsNullOrEmpty(assetPath))
                {
                    Debug.LogError("BRDF factorization: output asset path is empty.");
                    return;
                }

                var asset = BrdfFactorizationBaker.BakeAndSave(settings, assetPath);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        private void LoadPrefs()
        {
            if (EditorPrefs.HasKey(PrefKeySettings))
            {
                var json = EditorPrefs.GetString(PrefKeySettings, string.Empty);
                if (!string.IsNullOrEmpty(json))
                    JsonUtility.FromJsonOverwrite(json, settings);
            }

            assetPath = EditorPrefs.GetString(PrefKeyAssetPath, assetPath);
        }

        private void SavePrefs()
        {
            if (settings != null)
                EditorPrefs.SetString(PrefKeySettings, JsonUtility.ToJson(settings));
            EditorPrefs.SetString(PrefKeyAssetPath, assetPath ?? DefaultAssetPath);
        }
    }
}
