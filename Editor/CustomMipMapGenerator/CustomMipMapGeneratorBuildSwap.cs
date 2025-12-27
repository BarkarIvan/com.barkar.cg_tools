using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CustomMipMapGenerator
{
    public sealed class CustomMipMapGeneratorBuildSwap : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // Naming: <name>.mobile.asset and <name>.standalone.asset are normalized to target at build time.
        private const string MobileSuffix = ".mobile.asset";
        private const string StandaloneSuffix = ".standalone.asset";
        private const string LegacySuffix = ".asset";
        private const string StateFileName = "swap_state.json";
        private static readonly string[] VariantSuffixes = { MobileSuffix, StandaloneSuffix };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            RestorePendingSwaps();

            var targetSuffix = GetSuffixForTarget(report.summary.platform);
            if (targetSuffix == null)
                return;

            var swaps = SwapAssetsForTarget(targetSuffix);
            SaveState(swaps);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RestorePendingSwaps();
        }

        [MenuItem("Tools/Custom MipMap Generator/Restore Build Swap Backups")]
        private static void RestoreFromMenu()
        {
            RestorePendingSwaps();
        }

        private static string GetSuffixForTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                case BuildTarget.tvOS:
                    return MobileSuffix;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                    return StandaloneSuffix;
                case BuildTarget.StandaloneLinux64:
                    return StandaloneSuffix;
                default:
                    Debug.Log($"Custom MipMap build swap: no rule for build target {target}, skipping.");
                    return null;
            }
        }

        private static List<SwapEntry> SwapAssetsForTarget(string targetSuffix)
        {
            var entries = new List<SwapEntry>();
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                return entries;

            var groups = CollectVariantGroups(dataPath);
            if (groups.Count == 0)
            {
                Debug.Log("Custom MipMap build swap: no variant groups found.");
                return entries;
            }

            var swapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups.Values)
            {
                if (!group.variants.TryGetValue(targetSuffix, out var targetAssetPath))
                {
                    Debug.LogWarning($"Custom MipMap build swap: missing {targetSuffix} for {group.baseKey}.");
                    continue;
                }

                var targetFullPath = ToFullPath(targetAssetPath);
                if (string.IsNullOrEmpty(targetFullPath) || !File.Exists(targetFullPath))
                {
                    Debug.LogWarning($"Custom MipMap build swap: target asset missing for {targetAssetPath}.");
                    continue;
                }

                foreach (var assetPath in group.variants.Values)
                {
                    if (string.Equals(assetPath, targetAssetPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!swapped.Add(assetPath))
                        continue;
                    if (!TrySwapAsset(assetPath, targetAssetPath, targetFullPath, entries))
                        continue;
                }
            }

            if (entries.Count > 0)
                Debug.Log($"Custom MipMap build swap: swapped {entries.Count} assets to '{targetSuffix}'.");
            return entries;
        }

        private static void RestorePendingSwaps()
        {
            var state = LoadState();
            if (state == null || state.entries == null || state.entries.Count == 0)
                return;

            int restored = 0;
            foreach (var entry in state.entries)
            {
                if (string.IsNullOrEmpty(entry.assetPath) || string.IsNullOrEmpty(entry.backupPath))
                    continue;

                var baseFullPath = ToFullPath(entry.assetPath);
                if (string.IsNullOrEmpty(baseFullPath) || !File.Exists(entry.backupPath))
                {
                    Debug.LogWarning($"Custom MipMap build swap: missing backup for {entry.assetPath}.");
                    continue;
                }

                File.Copy(entry.backupPath, baseFullPath, true);
                File.Delete(entry.backupPath);
                AssetDatabase.ImportAsset(entry.assetPath, ImportAssetOptions.ForceUpdate);
                restored++;
            }

            ClearState();
            if (restored > 0)
                Debug.Log($"Custom MipMap build swap: restored {restored} assets.");
        }

        private static string CreateBackupPath(string assetPath)
        {
            Directory.CreateDirectory(BackupRoot);
            var hash = Hash128.Compute(assetPath).ToString();
            return Path.Combine(BackupRoot, hash + ".asset");
        }

        private static void SaveState(List<SwapEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            Directory.CreateDirectory(BackupRoot);
            var state = new SwapState { entries = entries };
            File.WriteAllText(StatePath, JsonUtility.ToJson(state, true));
        }

        private static SwapState LoadState()
        {
            if (!File.Exists(StatePath))
                return null;

            try
            {
                var json = File.ReadAllText(StatePath);
                return JsonUtility.FromJson<SwapState>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Custom MipMap build swap: failed to read state. {exception.Message}");
                return null;
            }
        }

        private static void ClearState()
        {
            if (File.Exists(StatePath))
                File.Delete(StatePath);
        }

        private static string ToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;

            var dataPath = Application.dataPath.Replace('\\', '/');
            var normalized = fullPath.Replace('\\', '/');
            if (!normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return "Assets" + normalized.Substring(dataPath.Length);
        }

        private static string ToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static bool TrySwapAsset(string assetPath, string targetAssetPath, string targetFullPath, List<SwapEntry> entries)
        {
            var assetFullPath = ToFullPath(assetPath);
            if (string.IsNullOrEmpty(assetFullPath) || !File.Exists(assetFullPath))
            {
                Debug.LogWarning($"Custom MipMap build swap: asset missing for {assetPath}.");
                return false;
            }

            var backupPath = CreateBackupPath(assetPath);
            File.Copy(assetFullPath, backupPath, true);
            File.Copy(targetFullPath, assetFullPath, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            entries.Add(new SwapEntry
            {
                assetPath = assetPath,
                backupPath = backupPath,
                variantPath = targetAssetPath
            });

            return true;
        }

        private static Dictionary<string, VariantGroup> CollectVariantGroups(string dataPath)
        {
            var groups = new Dictionary<string, VariantGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var suffix in VariantSuffixes)
            {
                var files = Directory.GetFiles(dataPath, "*" + suffix, SearchOption.AllDirectories);
                foreach (var variantFullPath in files)
                {
                    var variantAssetPath = ToAssetPath(variantFullPath);
                    if (string.IsNullOrEmpty(variantAssetPath))
                        continue;
                    if (variantAssetPath.Length <= suffix.Length)
                        continue;
                    var baseKey = variantAssetPath.Substring(0, variantAssetPath.Length - suffix.Length);
                    if (!groups.TryGetValue(baseKey, out var group))
                    {
                        group = new VariantGroup(baseKey);
                        groups.Add(baseKey, group);
                    }
                    group.variants[suffix] = variantAssetPath;
                }
            }

            foreach (var group in groups.Values)
            {
                var legacyPath = group.baseKey + LegacySuffix;
                var legacyFullPath = ToFullPath(legacyPath);
                if (!string.IsNullOrEmpty(legacyFullPath) && File.Exists(legacyFullPath))
                    group.variants[LegacySuffix] = legacyPath;
            }

            return groups;
        }

        private static string BackupRoot
        {
            get
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.Combine(projectRoot ?? string.Empty, "Library", "CustomMipMapGenerator", "BuildSwap");
            }
        }

        private static string StatePath => Path.Combine(BackupRoot, StateFileName);

        [Serializable]
        private sealed class SwapState
        {
            public List<SwapEntry> entries = new List<SwapEntry>();
        }

        [Serializable]
        private sealed class SwapEntry
        {
            public string assetPath;
            public string backupPath;
            public string variantPath;
        }

        private sealed class VariantGroup
        {
            public readonly string baseKey;
            public readonly Dictionary<string, string> variants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public VariantGroup(string baseKeyValue)
            {
                baseKey = baseKeyValue;
            }
        }
    }
}
