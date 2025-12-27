using UnityEditor;
using UnityEngine;

namespace CustomMipMapGenerator
{
    internal sealed class CustomMipMapGeneratorAutoProcessor : AssetPostprocessor
    {
        private static bool warnedMissingProfileSet;
        private static bool warnedMultipleProfileSets;

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets == null || importedAssets.Length == 0)
                return;

            var profileSet = FindProfileSet();
            if (profileSet == null)
            {
                if (!warnedMissingProfileSet)
                {
                    Debug.LogWarning("Custom MipMap auto-import: no Profile Set asset found. Create one via Tools/Custom MipMap Generator/Create Profile Set.");
                    warnedMissingProfileSet = true;
                }
                return;
            }

            var shader = profileSet.ResolveShader();
            if (shader == null)
            {
                Debug.LogWarning("Custom MipMap auto-import: compute shader not assigned or found.");
                return;
            }

            foreach (var path in importedAssets)
            {
                if (CustomMipMapGeneratorAutoGeneration.IsSuppressed(path))
                    continue;
                CustomMipMapGeneratorAutoGeneration.TryGenerateForAsset(path, profileSet, shader);
            }
        }

        private static CustomMipMapGeneratorProfileSet FindProfileSet()
        {
            var guids = AssetDatabase.FindAssets("t:CustomMipMapGeneratorProfileSet");
            if (guids.Length == 0)
                return null;

            if (guids.Length > 1 && !warnedMultipleProfileSets)
            {
                Debug.LogWarning("Custom MipMap auto-import: multiple Profile Set assets found. Using the first.");
                warnedMultipleProfileSets = true;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<CustomMipMapGeneratorProfileSet>(path);
        }
    }
}
