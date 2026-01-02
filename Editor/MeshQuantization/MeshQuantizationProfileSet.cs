using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshQuantization
{
    [CreateAssetMenu(menuName = "Mesh Quantization/Profile Set", fileName = "MeshQuantizationProfileSet")]
    public sealed class MeshQuantizationProfileSet : ScriptableObject
    {
        public bool useDefaultProfile = true;
        public MeshQuantizationSettings defaultSettings = new MeshQuantizationSettings();
        public List<Profile> profiles = new List<Profile>();

        [Serializable]
        public sealed class Profile
        {
            public string name = "Profile";
            public bool enabled = true;
            public string[] suffixes = Array.Empty<string>();
            public MeshQuantizationSettings settings = new MeshQuantizationSettings();
        }

        public bool TryGetSettingsForFileName(string fileName, out MeshQuantizationSettings settings)
        {
            settings = null;
            if (string.IsNullOrEmpty(fileName))
                return false;

            Profile best = null;
            int bestLength = -1;
            foreach (var profile in profiles)
            {
                if (profile == null || !profile.enabled || profile.suffixes == null)
                    continue;

                foreach (var suffix in profile.suffixes)
                {
                    if (string.IsNullOrEmpty(suffix))
                        continue;
                    if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int length = suffix.Length;
                    if (length > bestLength)
                    {
                        bestLength = length;
                        best = profile;
                    }
                }
            }

            if (best != null && best.settings != null)
            {
                settings = best.settings;
                return true;
            }

            if (useDefaultProfile)
            {
                settings = defaultSettings;
                return settings != null;
            }

            return false;
        }

        private void OnValidate()
        {
            if (profiles == null || profiles.Count == 0)
                return;

            bool changed = false;
            foreach (var profile in profiles)
            {
                if (profile == null)
                    continue;
                if (profile.suffixes == null)
                {
                    profile.suffixes = Array.Empty<string>();
                    changed = true;
                }
                if (profile.settings == null)
                {
                    profile.settings = CloneSettings(defaultSettings);
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(this);
        }

        [MenuItem("Tools/Mesh Quantization/Create Profile Set")]
        private static void CreateDefaultProfileSet()
        {
            const string defaultPath = "Assets/MeshQuantizationProfileSet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<MeshQuantizationProfileSet>(defaultPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var asset = CreateInstance<MeshQuantizationProfileSet>();
            asset.profiles = new List<Profile>
            {
                new Profile
                {
                    name = "Quantized",
                    suffixes = new[] { "_mq", "_qmesh" }
                }
            };

            var dir = Path.GetDirectoryName(defaultPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(asset, defaultPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static MeshQuantizationSettings CloneSettings(MeshQuantizationSettings source)
        {
            return source == null ? new MeshQuantizationSettings() : source.Clone();
        }
    }
}
