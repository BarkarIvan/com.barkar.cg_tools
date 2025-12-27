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
        // Naming: <name>.mobile.asset, <name>.pc.asset, <name>.linux.asset swap into <name>.asset at build time.
        private const string MobileSuffix = ".mobile.asset";
        private const string PcSuffix = ".pc.asset";
        private const string LinuxSuffix = ".linux.asset";
        private const string StateFileName = "swap_state.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            RestorePendingSwaps();

            var suffix = GetSuffixForTarget(report.summary.platform);
            if (suffix == null)
                return;

            var swaps = SwapAssetsForSuffix(suffix);
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
                    return PcSuffix;
                case BuildTarget.StandaloneLinux64:
                    return LinuxSuffix;
                default:
                    Debug.Log($"Custom MipMap build swap: no rule for build target {target}, skipping.");
                    return null;
            }
        }

        private static List<SwapEntry> SwapAssetsForSuffix(string suffix)
        {
            var entries = new List<SwapEntry>();
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                return entries;

            var variantFiles = Directory.GetFiles(dataPath, "*" + suffix, SearchOption.AllDirectories);
            if (variantFiles.Length == 0)
            {
                Debug.Log($"Custom MipMap build swap: no '{suffix}' variants found.");
                return entries;
            }

            foreach (var variantFullPath in variantFiles)
            {
                var variantAssetPath = ToAssetPath(variantFullPath);
                if (string.IsNullOrEmpty(variantAssetPath))
                    continue;

                var baseAssetPath = variantAssetPath.Substring(0, variantAssetPath.Length - suffix.Length) + ".asset";
                var baseFullPath = ToFullPath(baseAssetPath);
                if (string.IsNullOrEmpty(baseFullPath) || !File.Exists(baseFullPath))
                {
                    Debug.LogWarning($"Custom MipMap build swap: base asset not found for {variantAssetPath}.");
                    continue;
                }

                var backupPath = CreateBackupPath(baseAssetPath);
                File.Copy(baseFullPath, backupPath, true);
                File.Copy(variantFullPath, baseFullPath, true);
                AssetDatabase.ImportAsset(baseAssetPath, ImportAssetOptions.ForceUpdate);

                entries.Add(new SwapEntry
                {
                    assetPath = baseAssetPath,
                    backupPath = backupPath,
                    variantPath = variantAssetPath
                });
            }

            if (entries.Count > 0)
                Debug.Log($"Custom MipMap build swap: swapped {entries.Count} assets using '{suffix}'.");
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
    }
}
