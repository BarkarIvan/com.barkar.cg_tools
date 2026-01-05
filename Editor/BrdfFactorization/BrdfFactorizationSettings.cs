using System;
using UnityEngine;

namespace BrdfFactorization
{
    [Serializable]
    internal sealed class BrdfFactorizationSettings
    {
        public int textureSize = 128;
        public int sampleCount = 8192;
        public float smoothness = 0.1f;
        public int maxIterations = 200;
        public float tolerance = 1e-4f;

        public Color albedo = new Color(0.8f, 0.8f, 0.8f, 1f);
        public float metallic = 0.0f;
        public float roughness = 0.5f;
        public float specularF0 = 0.04f;
        public float specularWeight = 1.0f;
    }
}
