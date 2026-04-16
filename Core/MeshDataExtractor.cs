using System;
using System.Collections.Generic;
using System.IO;
using MeshSetExtender.Math;
using FrostySdk.IO;
using FrostySdk.Managers;
using Frosty.Core;

namespace MeshSetExtender.Core
{
    /// <summary>
    /// Extracts mesh data from Frosty's MeshSet resources
    /// </summary>
    public static class MeshDataExtractor
    {
        /// <summary>
        /// Extracts mesh data from a MeshSet resource for the specified LOD
        /// </summary>
        public static MeshData ExtractFromMeshSet(dynamic meshSet, int lodIndex = 0)
        {
            if (meshSet == null)
                throw new ArgumentNullException(nameof(meshSet));

            var lods = meshSet.Lods;
            if (lods == null || lods.Count <= lodIndex)
                throw new ArgumentException($"LOD {lodIndex} not found in mesh set");

            var lod = lods[lodIndex];
            return ExtractFromLod(meshSet, lod);
        }

        /// <summary>
        /// Extracts mesh data from a specific LOD
        /// </summary>
        public static MeshData ExtractFromLod(dynamic meshSet, dynamic lod)
        {
            var meshData = new MeshData();

            // Get chunk data
            Stream chunkStream = null;
            Guid chunkId = lod.ChunkId;
            
            if (chunkId != Guid.Empty)
            {
                var chunkEntry = App.AssetManager.GetChunkEntry(chunkId);
                if (chunkEntry != null)
                {
                    chunkStream = App.AssetManager.GetChunk(chunkEntry);
                }
            }
            else if (lod.InlineData != null)
            {
                chunkStream = new MemoryStream(lod.InlineData);
            }

            if (chunkStream == null)
                throw new InvalidOperationException("Could not get mesh chunk data");

            // Collect all vertices and indices from all sections
            var allVertices = new List<Vector3d>();
            var allNormals = new List<Vector3>();
            var allTangents = new List<Vector4>();
            var allUV1 = new List<Vector2>();
            var allBoneWeights = new List<BoneWeight>();
            var allIndices = new List<int>();

            using (var reader = new NativeReader(chunkStream))
            {
                int vertexOffset = 0;

                foreach (var section in lod.Sections)
                {
                    if (string.IsNullOrEmpty(section.Name))
                        continue;

                    // Read vertices
                    var sectionData = ReadMeshSection(reader, section, lod);
                    
                    // Add to combined data with index offset
                    allVertices.AddRange(sectionData.Vertices);
                    allNormals.AddRange(sectionData.Normals);
                    if (sectionData.Tangents != null)
                        allTangents.AddRange(sectionData.Tangents);
                    if (sectionData.UV1 != null)
                        allUV1.AddRange(sectionData.UV1);
                    if (sectionData.BoneWeights != null)
                        allBoneWeights.AddRange(sectionData.BoneWeights);

                    // Offset indices
                    foreach (var idx in sectionData.Indices)
                    {
                        allIndices.Add(idx + vertexOffset);
                    }

                    vertexOffset += sectionData.Vertices.Length;
                }
            }

            meshData.Vertices = allVertices.ToArray();
            meshData.Normals = allNormals.ToArray();
            meshData.Tangents = allTangents.Count > 0 ? allTangents.ToArray() : null;
            meshData.UV1 = allUV1.Count > 0 ? allUV1.ToArray() : null;
            meshData.BoneWeights = allBoneWeights.Count > 0 ? allBoneWeights.ToArray() : null;
            meshData.Indices = allIndices.ToArray();

            return meshData;
        }

        private static MeshData ReadMeshSection(NativeReader reader, dynamic section, dynamic lod)
        {
            var meshData = new MeshData
            {
                Name = section.Name
            };

            int vertexCount = (int)section.VertexCount;
            int indexCount = (int)section.PrimitiveCount * 3;

            meshData.Vertices = new Vector3d[vertexCount];
            meshData.Normals = new Vector3[vertexCount];
            meshData.Indices = new int[indexCount];

            var tangents = new List<Vector4>();
            var uv1 = new List<Vector2>();
            var boneWeights = new List<BoneWeight>();

            // Read vertex data based on geometry declaration
            long vertexBufferOffset = lod.VertexBufferOffset + section.VertexOffset;
            
            foreach (var stream in section.GeometryDeclDesc.Streams)
            {
                if (stream.VertexStride == 0)
                    continue;

                reader.Position = vertexBufferOffset;

                for (int i = 0; i < vertexCount; i++)
                {
                    foreach (var element in section.GeometryDeclDesc.Elements)
                    {
                        if (element.StreamIndex != stream.Index)
                            continue;

                        switch ((int)element.Usage)
                        {
                            case 0: // Position
                                ReadPosition(reader, meshData, i, element);
                                break;
                            case 1: // Normal
                                ReadNormal(reader, meshData, i, element);
                                break;
                            case 2: // Tangent
                                ReadTangent(reader, tangents, i, element, vertexCount);
                                break;
                            case 6: // BoneIndices
                            case 7: // BoneWeights
                                ReadBoneData(reader, boneWeights, i, element, vertexCount);
                                break;
                            case 14: // TexCoord0
                                ReadUV(reader, uv1, i, element, vertexCount);
                                break;
                            default:
                                // Skip unknown elements
                                reader.Position += GetElementSize(element);
                                break;
                        }
                    }
                }

                vertexBufferOffset += stream.VertexStride * vertexCount;
            }

            // Read indices
            long indexBufferOffset = lod.IndexBufferOffset + section.StartIndex * 2;
            reader.Position = indexBufferOffset;

            for (int i = 0; i < indexCount; i++)
            {
                meshData.Indices[i] = reader.ReadUShort();
            }

            if (tangents.Count == vertexCount)
                meshData.Tangents = tangents.ToArray();
            if (uv1.Count == vertexCount)
                meshData.UV1 = uv1.ToArray();
            if (boneWeights.Count == vertexCount)
                meshData.BoneWeights = boneWeights.ToArray();

            return meshData;
        }

        private static void ReadPosition(NativeReader reader, MeshData meshData, int index, dynamic element)
        {
            int format = (int)element.Format;
            
            switch (format)
            {
                case 0: // Float3
                    meshData.Vertices[index] = new Vector3d(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                    );
                    break;
                case 6: // Half3
                case 7: // Half4
                    meshData.Vertices[index] = new Vector3d(
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort())
                    );
                    if (format == 7)
                        reader.ReadUShort(); // w component
                    break;
                default:
                    reader.Position += GetElementSize(element);
                    break;
            }
        }

        private static void ReadNormal(NativeReader reader, MeshData meshData, int index, dynamic element)
        {
            int format = (int)element.Format;

            switch (format)
            {
                case 6: // Half3
                case 7: // Half4
                    meshData.Normals[index] = new Vector3(
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort())
                    );
                    if (format == 7)
                        reader.ReadUShort();
                    break;
                default:
                    meshData.Normals[index] = new Vector3(0, 1, 0);
                    reader.Position += GetElementSize(element);
                    break;
            }
        }

        private static void ReadTangent(NativeReader reader, List<Vector4> tangents, int index, dynamic element, int count)
        {
            while (tangents.Count <= index)
                tangents.Add(new Vector4(1, 0, 0, 1));

            int format = (int)element.Format;

            switch (format)
            {
                case 7: // Half4
                    tangents[index] = new Vector4(
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort())
                    );
                    break;
                default:
                    reader.Position += GetElementSize(element);
                    break;
            }
        }

        private static void ReadUV(NativeReader reader, List<Vector2> uvs, int index, dynamic element, int count)
        {
            while (uvs.Count <= index)
                uvs.Add(new Vector2(0, 0));

            int format = (int)element.Format;

            switch (format)
            {
                case 1: // Float2
                    uvs[index] = new Vector2(reader.ReadFloat(), reader.ReadFloat());
                    break;
                case 5: // Half2
                    uvs[index] = new Vector2(
                        HalfToFloat(reader.ReadUShort()),
                        HalfToFloat(reader.ReadUShort())
                    );
                    break;
                default:
                    reader.Position += GetElementSize(element);
                    break;
            }
        }

        private static void ReadBoneData(NativeReader reader, List<BoneWeight> boneWeights, int index, dynamic element, int count)
        {
            while (boneWeights.Count <= index)
                boneWeights.Add(new BoneWeight());

            int usage = (int)element.Usage;
            var bw = boneWeights[index];

            if (usage == 6) // BoneIndices
            {
                bw.boneIndex0 = reader.ReadByte();
                bw.boneIndex1 = reader.ReadByte();
                bw.boneIndex2 = reader.ReadByte();
                bw.boneIndex3 = reader.ReadByte();
            }
            else if (usage == 7) // BoneWeights
            {
                bw.boneWeight0 = reader.ReadByte() / 255f;
                bw.boneWeight1 = reader.ReadByte() / 255f;
                bw.boneWeight2 = reader.ReadByte() / 255f;
                bw.boneWeight3 = reader.ReadByte() / 255f;
            }

            boneWeights[index] = bw;
        }

        private static int GetElementSize(dynamic element)
        {
            int format = (int)element.Format;
            switch (format)
            {
                case 0: return 12; // Float3
                case 1: return 8;  // Float2
                case 2: return 4;  // Float
                case 5: return 4;  // Half2
                case 6: return 6;  // Half3
                case 7: return 8;  // Half4
                case 8: return 4;  // Byte4
                case 9: return 4;  // Byte4N
                default: return 4;
            }
        }

        private static float HalfToFloat(ushort half)
        {
            int sign = (half >> 15) & 1;
            int exponent = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            if (exponent == 0)
            {
                if (mantissa == 0)
                    return sign == 0 ? 0f : -0f;
                // Denormalized
                float m = mantissa / 1024f;
                return (sign == 0 ? 1 : -1) * m * (float)System.Math.Pow(2, -14);
            }
            else if (exponent == 31)
            {
                if (mantissa == 0)
                    return sign == 0 ? float.PositiveInfinity : float.NegativeInfinity;
                return float.NaN;
            }
            else
            {
                float m = 1 + mantissa / 1024f;
                return (sign == 0 ? 1 : -1) * m * (float)System.Math.Pow(2, exponent - 15);
            }
        }
    }
}
