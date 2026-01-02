using System;
using UnityEngine;

namespace MeshQuantization
{
    [Serializable]
    public sealed class MeshQuantizationSettings
    {
        public bool overwriteVertexColors = true;
        public bool generateMissingNormals = false;
        public bool generateMissingTangents = false;
        public bool disableReadWrite = true;

        public MeshQuantizationSettings Clone()
        {
            return (MeshQuantizationSettings)MemberwiseClone();
        }
    }
}
