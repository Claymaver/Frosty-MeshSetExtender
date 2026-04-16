using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrostySdk.IO;
using FrostySdk.Interfaces;
using FrostySdk.Managers;
using MeshSetPlugin.Resources;
using Frosty.Core;
using FrostySdk;

namespace MeshSetExtender.Decimation
{
    public class PostImportLodDecimator
    {
        private readonly ILogger _logger;
        private readonly bool _debug;
        private readonly float _maxError;
        private readonly bool _lockBorders;

        public PostImportLodDecimator(ILogger logger, bool debug = false, float maxError = 0.05f, bool lockBorders = false)
        {
            _logger = logger;
            _debug = debug;
            _maxError = maxError;
            _lockBorders = lockBorders;
            NativeLibraryLoader.EnsureLoaded();
            if (_debug) _logger.Log($"[DEBUG] MeshOptimizer available: {MeshOptimizerInterop.IsAvailable}, maxError={_maxError}, lockBorders={_lockBorders}");
        }

        public void DecimateLods(MeshSet meshSet, ResAssetEntry resEntry, float[] ratios,
            Action<string, double> progressCallback = null)
        {
            if (meshSet.Lods.Count <= 1) return;

            for (int lodIdx = 1; lodIdx < meshSet.Lods.Count; lodIdx++)
            {
                float ratio = (lodIdx - 1 < ratios.Length)
                    ? ratios[lodIdx - 1]
                    : ratios[ratios.Length - 1] * 0.5f;

                MeshSetLod lod = meshSet.Lods[lodIdx];

                progressCallback?.Invoke($"Decimating LOD{lodIdx} ({ratio:P0})...",
                    (double)lodIdx / meshSet.Lods.Count * 100);

                Stream lodStream = GetLodStream(lod);
                if (lodStream == null) { _logger.LogError($"Failed to read LOD{lodIdx} chunk."); continue; }

                var lodSections = ExtractSectionGeometries(lod, lodStream);
                lodStream.Dispose();

                if (lodSections.Count == 0) { _logger.LogError($"No renderable sections in LOD{lodIdx}."); continue; }

                int originalTriCount = lodSections.Sum(s => s.Indices.Length / 3);
                var decimatedSections = new List<SectionGeometry>();

                foreach (var src in lodSections)
                {
                    var decimated = DecimateSectionGeometry(src, ratio);
                    decimatedSections.Add(decimated);
                }

                int newTriCount = decimatedSections.Sum(s => s.Indices.Length / 3);
                _logger.Log($"LOD{lodIdx}: {originalTriCount} -> {newTriCount} tris ({ratio:P0})");

                if (_debug)
                    foreach (var d in decimatedSections)
                        _logger.Log($"[DEBUG]   {d.Name}: {d.Indices.Length / 3} tris");

                RebuildLodChunkData(lod, decimatedSections, resEntry);
            }
        }

        #region Extraction

        private List<SectionGeometry> ExtractSectionGeometries(MeshSetLod lod, Stream chunkStream)
        {
            var result = new List<SectionGeometry>();
            int indexSize = lod.IndexUnitSize / 8;

            using (NativeReader reader = new NativeReader(chunkStream))
            {
                foreach (MeshSetSection section in lod.Sections)
                {
                    // Only decimate named (renderable) sections; skip shadow/depth
                    if (string.IsNullOrEmpty(section.Name)) continue;
                    if (section.VertexCount == 0 || section.PrimitiveCount == 0) continue;

                    var geom = ExtractSection(reader, lod, section, indexSize);
                    if (geom != null) result.Add(geom);
                }
            }
            return result;
        }

        private SectionGeometry ExtractSection(NativeReader reader, MeshSetLod lod, MeshSetSection section, int indexSize)
        {
            var geomDecl = section.GeometryDeclDesc[0];
            int posOffset = -1, posStreamIdx = -1;
            VertexElementFormat posFormat = VertexElementFormat.None;

            for (int e = 0; e < geomDecl.ElementCount; e++)
            {
                var elem = geomDecl.Elements[e];
                if (elem.Usage == VertexElementUsage.Pos)
                {
                    posOffset = elem.Offset;
                    posStreamIdx = elem.StreamIndex;
                    posFormat = elem.Format;
                    break;
                }
            }
            if (posOffset < 0) return null;

            int vertexStride = geomDecl.Streams[posStreamIdx].VertexStride;

            if (_debug)
                _logger.Log($"[DEBUG] Extracting '{section.Name}': format={posFormat}, stride={vertexStride}, verts={section.VertexCount}");

            // Positions
            float[] positions = new float[section.VertexCount * 3];
            for (uint v = 0; v < section.VertexCount; v++)
            {
                reader.Position = section.VertexOffset + (v * vertexStride) + posOffset;
                ReadPosition(reader, posFormat, positions, (int)v * 3);
            }

            // Full vertex data
            byte[] vertexData = new byte[section.VertexCount * vertexStride];
            for (uint v = 0; v < section.VertexCount; v++)
            {
                reader.Position = section.VertexOffset + (v * vertexStride);
                byte[] vdata = reader.ReadBytes(vertexStride);
                Buffer.BlockCopy(vdata, 0, vertexData, (int)v * vertexStride, vertexStride);
            }

            // Indices
            long indexBufferStart = lod.VertexBufferSize;
            reader.Position = indexBufferStart + (section.StartIndex * indexSize);
            uint[] indices = new uint[section.PrimitiveCount * 3];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = indexSize == 4 ? reader.ReadUInt() : reader.ReadUShort();

            return new SectionGeometry
            {
                Name = section.Name,
                Positions = positions,
                VertexCount = (int)section.VertexCount,
                Indices = indices,
                VertexData = vertexData,
                VertexStride = vertexStride
            };
        }

        private void ReadPosition(NativeReader reader, VertexElementFormat format, float[] outPos, int outIdx)
        {
            switch (format)
            {
                case VertexElementFormat.Float3:
                    outPos[outIdx] = reader.ReadFloat();
                    outPos[outIdx + 1] = reader.ReadFloat();
                    outPos[outIdx + 2] = reader.ReadFloat();
                    break;
                case VertexElementFormat.Float4:
                    outPos[outIdx] = reader.ReadFloat();
                    outPos[outIdx + 1] = reader.ReadFloat();
                    outPos[outIdx + 2] = reader.ReadFloat();
                    reader.ReadFloat();
                    break;
                case VertexElementFormat.Half4:
                    outPos[outIdx] = HalfUtils.Unpack(reader.ReadUShort());
                    outPos[outIdx + 1] = HalfUtils.Unpack(reader.ReadUShort());
                    outPos[outIdx + 2] = HalfUtils.Unpack(reader.ReadUShort());
                    reader.ReadUShort();
                    break;
                case VertexElementFormat.Half3:
                    outPos[outIdx] = HalfUtils.Unpack(reader.ReadUShort());
                    outPos[outIdx + 1] = HalfUtils.Unpack(reader.ReadUShort());
                    outPos[outIdx + 2] = HalfUtils.Unpack(reader.ReadUShort());
                    break;
                default:
                    outPos[outIdx] = reader.ReadFloat();
                    outPos[outIdx + 1] = reader.ReadFloat();
                    outPos[outIdx + 2] = reader.ReadFloat();
                    break;
            }
        }

        #endregion

        #region Decimation

        private SectionGeometry DecimateSectionGeometry(SectionGeometry source, float targetRatio)
        {
            uint[] newIndices;

            if (MeshOptimizerInterop.IsAvailable)
            {
                uint options = _lockBorders ? 2u : 0u;
                newIndices = MeshOptimizerInterop.Simplify(
                    source.Indices, source.Positions, source.VertexCount, targetRatio, _maxError, options);
            }
            else
            {
                newIndices = ManagedDecimator.Simplify(
                    source.Indices, source.Positions, source.VertexCount, targetRatio);
            }

            if (newIndices == null || newIndices.Length < 3)
                return source;

            return new SectionGeometry
            {
                Name = source.Name,
                Positions = source.Positions,
                VertexCount = source.VertexCount,
                Indices = newIndices,
                VertexData = source.VertexData,
                VertexStride = source.VertexStride
            };
        }

        #endregion

        #region Chunk Rebuild

        private void RebuildLodChunkData(MeshSetLod lod, List<SectionGeometry> decimatedSections, ResAssetEntry resEntry)
        {
            Stream originalStream = GetLodStream(lod);
            if (originalStream == null) { _logger.LogError("Failed to read LOD chunk for rebuild."); return; }

            byte[] originalBytes;
            using (var ms = new MemoryStream()) { originalStream.CopyTo(ms); originalBytes = ms.ToArray(); }
            originalStream.Dispose();

            bool use32Bit = lod.IndexUnitSize == 32;
            int indexUnitBytes = use32Bit ? 4 : 2;
            long origIndexRegionStart = lod.VertexBufferSize;

            // Pre-read original indices for shadow/depth sections (they are not decimated)
            var originalSectionIndices = new Dictionary<MeshSetSection, uint[]>();
            foreach (MeshSetSection section in lod.Sections)
            {
                if (string.IsNullOrEmpty(section.Name) && section.PrimitiveCount > 0)
                {
                    uint[] indices = new uint[section.PrimitiveCount * 3];
                    long offset = origIndexRegionStart + (section.StartIndex * indexUnitBytes);
                    for (int i = 0; i < indices.Length; i++)
                    {
                        if (use32Bit) { indices[i] = BitConverter.ToUInt32(originalBytes, (int)offset); offset += 4; }
                        else { indices[i] = BitConverter.ToUInt16(originalBytes, (int)offset); offset += 2; }
                    }
                    originalSectionIndices[section] = indices;
                }
            }

            // Original vertex buffer (unchanged)
            int originalVertexBufferSize = (int)lod.VertexBufferSize;
            byte[] originalVertexBuffer = new byte[originalVertexBufferSize];
            Buffer.BlockCopy(originalBytes, 0, originalVertexBuffer, 0, originalVertexBufferSize);

            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {
                writer.Write(originalVertexBuffer);
                long indexRegionStart = writer.Position;
                int decimatedWriteIdx = 0;

                foreach (MeshSetSection section in lod.Sections)
                {
                    if (!string.IsNullOrEmpty(section.Name) && section.VertexCount > 0 &&
                        decimatedWriteIdx < decimatedSections.Count)
                    {
                        // Renderable section — write decimated indices
                        var dec = decimatedSections[decimatedWriteIdx++];
                        section.StartIndex = (uint)((writer.Position - indexRegionStart) / indexUnitBytes);
                        section.PrimitiveCount = (uint)(dec.Indices.Length / 3);

                        foreach (uint index in dec.Indices)
                        {
                            if (use32Bit) writer.Write(index);
                            else writer.Write((ushort)index);
                        }
                    }
                    else if (string.IsNullOrEmpty(section.Name) && originalSectionIndices.ContainsKey(section))
                    {
                        // Shadow/depth section — preserve original indices
                        uint[] origIndices = originalSectionIndices[section];
                        section.StartIndex = (uint)((writer.Position - indexRegionStart) / indexUnitBytes);

                        foreach (uint index in origIndices)
                        {
                            if (use32Bit) writer.Write(index);
                            else writer.Write((ushort)index);
                        }
                    }
                    else
                    {
                        section.StartIndex = (uint)((writer.Position - indexRegionStart) / indexUnitBytes);
                        section.PrimitiveCount = 0;
                    }
                }

                writer.WritePadding(0x10);
                lod.VertexBufferSize = (uint)indexRegionStart;
                lod.IndexBufferSize = (uint)(writer.Position - indexRegionStart);
                lod.SetIndexBufferFormatSize(indexUnitBytes);

                byte[] newChunkData = writer.ToByteArray();

                if (lod.ChunkId != Guid.Empty)
                {
                    App.AssetManager.ModifyChunk(lod.ChunkId, newChunkData);
                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(lod.ChunkId);
                    if (chunkEntry != null) resEntry.LinkAsset(chunkEntry);
                }
                else
                    lod.SetInlineData(newChunkData);
            }
        }

        #endregion

        #region Helpers

        private Stream GetLodStream(MeshSetLod lod)
        {
            if (lod.ChunkId != Guid.Empty)
            {
                ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(lod.ChunkId);
                if (chunkEntry != null) return App.AssetManager.GetChunk(chunkEntry);
            }
            else if (lod.InlineData != null)
                return new MemoryStream(lod.InlineData);
            return null;
        }

        public class SectionGeometry
        {
            public string Name;
            public float[] Positions;
            public int VertexCount;
            public uint[] Indices;
            public byte[] VertexData;
            public int VertexStride;
        }

        #endregion
    }
}
