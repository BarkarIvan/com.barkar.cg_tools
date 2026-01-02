using System;
using UnityEngine;

namespace MeshQuantization
{
    [Serializable]
    public sealed class MeshQuantizationSettings
    {
        public bool overwriteVertexColors = true;
        public bool generateMissingNormals = true;
        public bool generateMissingTangents = true;
        public bool disableReadWrite = true;

        public MeshQuantizationSettings Clone()
        {
            return (MeshQuantizationSettings)MemberwiseClone();
        }
    }
}
