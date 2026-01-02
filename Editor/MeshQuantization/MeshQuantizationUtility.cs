using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshQuantization
{
    internal static class MeshQuantizationUtility
    {
        private const float Epsilon = 1e-10f;

        public static bool IsAlreadyQuantized(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return false;

            if (!mesh.HasVertexAttribute(VertexAttribute.Color))
                return false;

            return !mesh.HasVertexAttribute(VertexAttribute.Normal) &&
                   !mesh.HasVertexAttribute(VertexAttribute.Tangent);
        }

        public static bool TryQuantize(Mesh mesh, MeshQuantizationSettings settings, string assetPath)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return false;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"Mesh quantization skipped (Read/Write disabled): {assetPath} ({mesh.name})");
                return false;
            }

            int vertexCount = mesh.vertexCount;

            if (!settings.overwriteVertexColors)
            {
                var existingColors = mesh.colors32;
                if (existingColors != null && existingColors.Length == vertexCount)
                {
                    Debug.LogWarning($"Mesh quantization skipped (vertex colors present): {assetPath} ({mesh.name})");
                    return false;
                }
            }

            var normals = mesh.normals;
            if (normals == null || normals.Length != vertexCount)
            {
                if (settings.generateMissingNormals)
                {
                    mesh.RecalculateNormals();
                    normals = mesh.normals;
                }
                if (normals == null || normals.Length != vertexCount)
                {
                    Debug.LogWarning($"Mesh quantization skipped (missing normals): {assetPath} ({mesh.name})");
                    return false;
                }
            }

            var tangents = mesh.tangents;
            if (tangents == null || tangents.Length != vertexCount)
            {
                if (settings.generateMissingTangents)
                {
                    mesh.RecalculateTangents();
                    tangents = mesh.tangents;
                }
                if (tangents == null || tangents.Length != vertexCount)
                {
                    Debug.LogWarning($"Mesh quantization skipped (missing tangents): {assetPath} ({mesh.name})");
                    return false;
                }
            }

            var vertices = mesh.vertices;
            var uvChannels = new UvChannel[8];
            for (int channel = 0; channel < uvChannels.Length; ++channel)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!mesh.HasVertexAttribute(attribute))
                    continue;

                int dimension = mesh.GetVertexAttributeDimension(attribute);
                if (dimension == 2)
                {
                    var list = new List<Vector2>();
                    mesh.GetUVs(channel, list);
                    if (list.Count == vertexCount)
                        uvChannels[channel] = new UvChannel { dimension = 2, uv2 = list };
                }
                else if (dimension == 3)
                {
                    var list = new List<Vector3>();
                    mesh.GetUVs(channel, list);
                    if (list.Count == vertexCount)
                        uvChannels[channel] = new UvChannel { dimension = 3, uv3 = list };
                }
                else if (dimension == 4)
                {
                    var list = new List<Vector4>();
                    mesh.GetUVs(channel, list);
                    if (list.Count == vertexCount)
                        uvChannels[channel] = new UvChannel { dimension = 4, uv4 = list };
                }
            }

            int subMeshCount = mesh.subMeshCount;
            var submeshIndices = new int[subMeshCount][];
            var submeshTopologies = new MeshTopology[subMeshCount];
            for (int i = 0; i < subMeshCount; ++i)
            {
                submeshIndices[i] = mesh.GetIndices(i);
                submeshTopologies[i] = mesh.GetTopology(i);
            }

            var bounds = mesh.bounds;
            var indexFormat = mesh.indexFormat;

            var packed = new Color32[vertexCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                Vector3 n = normals[i];
                if (n.sqrMagnitude < Epsilon)
                    n = Vector3.up;
                else
                    n.Normalize();

                Vector4 t4 = tangents[i];
                Vector3 t = new Vector3(t4.x, t4.y, t4.z);
                if (t.sqrMagnitude < Epsilon)
                    t = Vector3.right;
                else
                    t.Normalize();

                Vector2 oct = EncodeOct(n);
                float angle = EncodeTangentAngle(n, t);
                byte angleByte = QuantizeUnorm8(angle / (Mathf.PI * 2f));
                byte signByte = t4.w >= 0f ? (byte)255 : (byte)0;

                packed[i] = new Color32(
                    QuantizeUnorm8(oct.x),
                    QuantizeUnorm8(oct.y),
                    angleByte,
                    signByte);
            }

            mesh.Clear(false);
            mesh.indexFormat = indexFormat;
            mesh.vertices = vertices;
            mesh.colors32 = packed;
            for (int channel = 0; channel < uvChannels.Length; ++channel)
            {
                var uvs = uvChannels[channel];
                if (uvs == null)
                    continue;

                if (uvs.dimension == 2)
                    mesh.SetUVs(channel, uvs.uv2);
                else if (uvs.dimension == 3)
                    mesh.SetUVs(channel, uvs.uv3);
                else if (uvs.dimension == 4)
                    mesh.SetUVs(channel, uvs.uv4);
            }

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; ++i)
                mesh.SetIndices(submeshIndices[i], submeshTopologies[i], i, false);

            mesh.bounds = bounds;

            if (settings.disableReadWrite)
            {
                mesh.UploadMeshData(true);
            }

            return true;
        }

        public static void Quantize(Mesh mesh, MeshQuantizationSettings settings, string assetPath)
        {
            TryQuantize(mesh, settings, assetPath);
        }

        private static Vector3 CalculateTangentBase(Vector3 normal)
        {
            return Mathf.Abs(normal.x) > Mathf.Abs(normal.z)
                ? new Vector3(-normal.y, normal.x, 0f).normalized
                : new Vector3(0f, -normal.z, normal.y).normalized;
        }

        private static float EncodeTangentAngle(Vector3 normal, Vector3 tangent)
        {
            Vector3 tb = CalculateTangentBase(normal);
            Vector3 bn = Vector3.Cross(normal, tb);
            float x = Mathf.Clamp(Vector3.Dot(tangent, tb), -1f, 1f);
            float y = Mathf.Clamp(Vector3.Dot(tangent, bn), -1f, 1f);
            float angle = Mathf.Atan2(y, x);
            if (angle < 0f)
                angle += Mathf.PI * 2f;
            return angle;
        }

        private static Vector2 OctWrap(Vector2 v)
        {
            Vector2 t = Vector2.one - new Vector2(Mathf.Abs(v.y), Mathf.Abs(v.x));
            return new Vector2(v.x < 0f ? -t.x : t.x, v.y < 0f ? -t.y : t.y);
        }

        private static Vector2 EncodeOct(Vector3 n)
        {
            float inv = 1f / (Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z));
            n *= inv;
            Vector2 p = n.z >= 0f ? new Vector2(n.x, n.y) : OctWrap(new Vector2(n.x, n.y));
            return p * 0.5f + new Vector2(0.5f, 0.5f);
        }

        private static byte QuantizeUnorm8(float value)
        {
            int v = Mathf.RoundToInt(value * 255f);
            if (v < 0)
                return 0;
            if (v > 255)
                return 255;
            return (byte)v;
        }

        private sealed class UvChannel
        {
            public int dimension;
            public List<Vector2> uv2;
            public List<Vector3> uv3;
            public List<Vector4> uv4;
        }
    }
}
