using UnityEngine;

namespace BrdfFactorization
{
    [CreateAssetMenu(menuName = "BRDF Factorization/Factorized BRDF", fileName = "BrdfFactorizationAsset")]
    public sealed class BrdfFactorizationAsset : ScriptableObject
    {
        public Texture2D pTexture;
        public Texture2D qTexture;
        public Vector3 scale = Vector3.one;
    }
}
