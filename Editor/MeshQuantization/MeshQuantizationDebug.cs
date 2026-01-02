using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshQuantization
{
    internal static class MeshQuantizationDebug
    {
        private const string PrefKey = "MeshQuantization.DebugLogs";

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static void Log(string message)
        {
            if (IsEnabled)
                Debug.Log(message);
        }

        [MenuItem("Tools/Mesh Quantization/Debug Logs")]
        private static void ToggleDebug()
        {
            IsEnabled = !IsEnabled;
            Debug.Log($"Mesh Quantization debug logs: {(IsEnabled ? "ON" : "OFF")}");
        }

        [MenuItem("Tools/Mesh Quantization/Debug Logs", true)]
        private static bool ToggleDebugValidate()
        {
            Menu.SetChecked("Tools/Mesh Quantization/Debug Logs", IsEnabled);
            return true;
        }

        [MenuItem("Tools/Mesh Quantization/Log Selected Mesh Info")]
        private static void LogSelectedMeshInfo()
        {
            Mesh mesh = GetSelectedMesh(out var source);
            if (mesh == null)
            {
                Debug.LogWarning("Mesh Quantization: select a Mesh asset, a Model (FBX/OBJ) asset, or a GameObject with MeshFilter.");
                return;
            }

            var path = AssetDatabase.GetAssetPath(mesh);
            string profileInfo = "profile=missing";
            if (!string.IsNullOrEmpty(path))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var profileSet = FindProfileSet(out var profilePath, out var multiple);
                if (profileSet != null)
                {
                    bool match = profileSet.TryGetSettingsForFileName(fileName, out _);
                    profileInfo = $"profile={profilePath} match={match}";
                    if (multiple)
                        profileInfo += " (multiple)";
                }
            }
            int colorsCount = 0;
            if (mesh.isReadable)
                colorsCount = mesh.colors32?.Length ?? 0;
            Debug.Log($"[MQ] Mesh info: {mesh.name} source={source} path={path} vtx={mesh.vertexCount} " +
                      $"normals={mesh.HasVertexAttribute(VertexAttribute.Normal)} " +
                      $"tangents={mesh.HasVertexAttribute(VertexAttribute.Tangent)} " +
                      $"colors={colorsCount} readable={mesh.isReadable} {profileInfo}");
        }

        private static Mesh GetSelectedMesh(out string source)
        {
            source = "unknown";
            var selected = Selection.activeObject;
            if (selected == null)
                return null;

            if (selected is Mesh mesh)
            {
                source = "Mesh asset";
                return mesh;
            }

            if (selected is GameObject go)
            {
                var filter = go.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    source = "MeshFilter";
                    return filter.sharedMesh;
                }

                var renderer = go.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null && renderer.sharedMesh != null)
                {
                    source = "SkinnedMeshRenderer";
                    return renderer.sharedMesh;
                }
            }

            var path = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(path))
            {
                Mesh firstMesh = null;
                int meshCount = 0;
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var asset in assets)
                {
                    if (asset is Mesh meshAsset)
                    {
                        meshCount++;
                        if (firstMesh == null)
                            firstMesh = meshAsset;
                    }
                }

                if (firstMesh != null)
                {
                    var fileName = Path.GetFileName(path);
                    source = meshCount > 1 ? $"Model asset ({fileName}, meshes={meshCount})" : $"Model asset ({fileName})";
                    return firstMesh;
                }
            }

            return null;
        }

        private static MeshQuantizationProfileSet FindProfileSet(out string path, out bool multiple)
        {
            path = null;
            multiple = false;
            var guids = AssetDatabase.FindAssets("t:MeshQuantizationProfileSet");
            if (guids == null || guids.Length == 0)
                return null;

            multiple = guids.Length > 1;
            path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<MeshQuantizationProfileSet>(path);
        }
    }
}
