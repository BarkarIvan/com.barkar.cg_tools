using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CustomMipMapGenerator
{
    [CreateAssetMenu(menuName = "Custom MipMap Generator/Profile Set", fileName = "CustomMipMapGeneratorProfileSet")]
    public sealed class CustomMipMapGeneratorProfileSet : ScriptableObject
    {
        public ComputeShader computeShader;
        public bool useDefaultProfile = true;
        public CustomMipMapGeneratorSettings defaultSettings = new CustomMipMapGeneratorSettings();
        public List<Profile> profiles = new List<Profile>();

        [Serializable]
        public sealed class Profile
        {
            public string name = "Profile";
            public bool enabled = true;
            public string[] suffixes = Array.Empty<string>();
            public CustomMipMapGeneratorSettings settings = new CustomMipMapGeneratorSettings();
        }

        public bool TryGetSettingsForFileName(string fileName, out CustomMipMapGeneratorSettings settings)
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

        public ComputeShader ResolveShader()
        {
            if (computeShader != null)
                return computeShader;

            var guids = AssetDatabase.FindAssets("CustomMipMapGenerator t:ComputeShader");
            if (guids.Length == 0)
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            return computeShader;
        }

        [MenuItem("Tools/Custom MipMap Generator/Create Profile Set")]
        private static void CreateDefaultProfileSet()
        {
            const string defaultPath = "Assets/CustomMipMapGeneratorProfileSet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CustomMipMapGeneratorProfileSet>(defaultPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var asset = CreateInstance<CustomMipMapGeneratorProfileSet>();
            asset.computeShader = FindDefaultComputeShader();
            asset.profiles = new List<Profile>
            {
                CreateProfile("BaseColor", new []{ "_BaseColor", "_Albedo" }, TextureKind.Color),
                CreateProfile("Normal", new []{ "_Normal", "_N" }, TextureKind.NormalMap),
                CreateProfile("ARM", new []{ "_ARM", "_ORM", "_RMA" }, TextureKind.DataMap)
            };

            var dir = Path.GetDirectoryName(defaultPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(asset, defaultPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Tools/Custom MipMap Generator/Regenerate CMips From Profiles")]
        private static void RegenerateFromProfiles()
        {
            var profileSet = FindProfileSet();
            if (profileSet == null)
            {
                Debug.LogWarning("Custom MipMap auto-import: no Profile Set asset found.");
                return;
            }

            var shader = profileSet.ResolveShader();
            if (shader == null)
            {
                Debug.LogWarning("Custom MipMap auto-import: compute shader not assigned or found.");
                return;
            }

            int generated = CustomMipMapGeneratorAutoGeneration.RegenerateAll(profileSet, shader);
            Debug.Log($"Custom MipMap auto-import: regenerated {generated} cmips files.");
        }

        private static Profile CreateProfile(string profileName, string[] suffixes, TextureKind kind)
        {
            return new Profile
            {
                name = profileName,
                suffixes = suffixes,
                settings = new CustomMipMapGeneratorSettings
                {
                    textureKind = kind
                }
            };
        }

        private static ComputeShader FindDefaultComputeShader()
        {
            var guids = AssetDatabase.FindAssets("CustomMipMapGenerator t:ComputeShader");
            if (guids.Length == 0)
                return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
        }

        private static CustomMipMapGeneratorSettings CloneSettings(CustomMipMapGeneratorSettings source)
        {
            return source == null ? new CustomMipMapGeneratorSettings() : source.Clone();
        }

        private static CustomMipMapGeneratorProfileSet FindProfileSet()
        {
            var guids = AssetDatabase.FindAssets("t:CustomMipMapGeneratorProfileSet");
            if (guids.Length == 0)
                return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<CustomMipMapGeneratorProfileSet>(path);
        }
    }
}
