using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CustomMipMapGenerator
{
    [InitializeOnLoad]
    internal static class CustomMipMapGeneratorImportDependency
    {
        public const string DependencyName = "CustomMipMapGenerator.BuildTarget";

        static CustomMipMapGeneratorImportDependency()
        {
            UpdateDependency();
        }

        internal static void UpdateDependency()
        {
            var hash = Hash128.Compute(EditorUserBuildSettings.activeBuildTarget.ToString());
            AssetDatabase.RegisterCustomDependency(DependencyName, hash);
        }
    }

    internal sealed class CustomMipMapGeneratorBuildTargetWatcher : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            CustomMipMapGeneratorImportDependency.UpdateDependency();
        }
    }
}
