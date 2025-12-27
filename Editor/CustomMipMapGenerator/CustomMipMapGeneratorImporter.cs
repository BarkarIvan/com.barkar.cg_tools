using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace CustomMipMapGenerator
{
    [ScriptedImporter(1, "cmips")]
    public sealed class CustomMipMapGeneratorImporter : ScriptedImporter
    {
        [SerializeField] private TextureFormat compressionMobile = TextureFormat.ASTC_6x6;
        [SerializeField] private TextureFormat compressionStandalone = TextureFormat.BC7;
        [SerializeField] private TextureWrapMode wrapModeU = TextureWrapMode.Repeat;
        [SerializeField] private TextureWrapMode wrapModeV = TextureWrapMode.Repeat;
        [SerializeField] private UnityEngine.FilterMode samplerFilterMode = UnityEngine.FilterMode.Bilinear;
        [SerializeField] private int anisoLevel = 1;
        [SerializeField] private float mipBias = 0f;

        public TextureFormat CompressionMobile
        {
            get => compressionMobile;
            set => compressionMobile = value;
        }

        public TextureFormat CompressionStandalone
        {
            get => compressionStandalone;
            set => compressionStandalone = value;
        }

        public TextureWrapMode WrapModeU
        {
            get => wrapModeU;
            set => wrapModeU = value;
        }

        public TextureWrapMode WrapModeV
        {
            get => wrapModeV;
            set => wrapModeV = value;
        }

        public UnityEngine.FilterMode SamplerFilterMode
        {
            get => samplerFilterMode;
            set => samplerFilterMode = value;
        }

        public int AnisoLevel
        {
            get => anisoLevel;
            set => anisoLevel = value;
        }

        public float MipBias
        {
            get => mipBias;
            set => mipBias = value;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnCustomDependency(CustomMipMapGeneratorImportDependency.DependencyName);

            if (!CustomMipMapGeneratorMipFile.TryRead(ctx.assetPath, out var header, out var rawData, out var error))
            {
                Debug.LogError($"Custom MipMap import failed for {ctx.assetPath}. {error}");
                return;
            }

            bool isLinear = header.textureKind != TextureKind.Color;
            var texture = new Texture2D(header.width, header.height, TextureFormat.RGBA32, header.mipCount, isLinear)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath)
            };

            int offset = 0;
            for (int mip = 0; mip < header.mipCount; mip++)
            {
                int mipWidth = Mathf.Max(1, header.width >> mip);
                int mipHeight = Mathf.Max(1, header.height >> mip);
                int byteCount = mipWidth * mipHeight * 4;
                texture.SetPixelData(rawData, mip, offset);
                offset += byteCount;
            }

            texture.Apply(false, false);
            ApplySamplingSettings(texture);

            var compression = GetCompressionForTarget(EditorUserBuildSettings.activeBuildTarget);
            if (!SystemInfo.SupportsTextureFormat(compression))
                Debug.LogWarning($"Compression format {compression} is not supported on this platform. Preview may be black.");
            EditorUtility.CompressTexture(texture, compression, TextureCompressionQuality.Best);

            ctx.AddObjectToAsset("texture", texture);
            ctx.SetMainObject(texture);
        }

        private void ApplySamplingSettings(Texture2D texture)
        {
            if (texture == null)
                return;

            texture.wrapModeU = wrapModeU;
            texture.wrapModeV = wrapModeV;
            texture.filterMode = samplerFilterMode;
            texture.anisoLevel = Mathf.Clamp(anisoLevel, 1, 16);
            texture.mipMapBias = mipBias;
        }

        private TextureFormat GetCompressionForTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                case BuildTarget.tvOS:
                    return compressionMobile;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return compressionStandalone;
                default:
                    return compressionStandalone;
            }
        }
    }
}
