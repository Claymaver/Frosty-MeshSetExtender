using System;
using System.Collections.Generic;
using System.IO;

namespace MeshSetExtender.Resources
{
    /// <summary>
    /// ClothWrappingAsset - parsed structure for cloth simulation vertex data
    /// 
    /// Binary layout (from hex analysis of actual .res files):
    /// 
    /// Full .res file on disk:
    /// - @0-3:   ByteSize (uint32, = file_size - 16)
    /// - @4-15:  12 zero bytes (rest of 16-byte file header)
    /// - @16+:   Content (what GetRes() returns)
    /// 
    /// Content layout (what GetRes() returns, starts at BNRY):
    /// - @0-15:  BNRY header block (BNRY + version + LTLE + flags)
    /// - @16-19: UnknownField (uint32, always 1)
    /// - @20-23: LodCount (uint32, e.g. 6)
    /// - @24+:   MeshSection data
    /// 
    /// Each MeshSection (48 byte header + 32 bytes per vertex):
    /// - UnknownId (4 bytes, always 1)
    /// - VertexCount (4 bytes)
    /// - UnmappedBytes (40 bytes)
    /// - Vertices[VertexCount] (32 bytes each):
    ///   - Position: 3x float (12 bytes)
    ///   - Normal: 2x int16 (4 bytes)
    ///   - Tangent: 2x int16 (4 bytes)
    ///   - BoneWeights: 4x byte/255 (4 bytes)
    ///   - BoneIndices: 4x uint16 (8 bytes)
    /// </summary>
    public class ClothWrappingAssetParsed
    {
        public byte[] BnryHeader { get; set; }    // 16 bytes: BNRY + version + LTLE + flags
        public uint UnknownField { get; set; }    // @content offset 16 (always 1)
        public uint LodCount { get; set; }        // @content offset 20
        public MeshSection[] MeshSections { get; set; }

        public ClothWrappingAssetParsed()
        {
        }

        /// <summary>
        /// Read from GetRes() data - starts at BNRY (no file header)
        /// </summary>
        public void Read(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                // Read BNRY header (16 bytes)
                BnryHeader = br.ReadBytes(16);
                
                // Verify BNRY magic
                string magic = System.Text.Encoding.ASCII.GetString(BnryHeader, 0, 4);
                if (magic != "BNRY")
                {
                    throw new InvalidDataException($"Expected BNRY header, got: {magic}");
                }
                
                // Read UnknownField at content offset 16 (always 1)
                UnknownField = br.ReadUInt32();
                
                // Read LodCount at content offset 20
                LodCount = br.ReadUInt32();
                
                if (LodCount == 0 || LodCount > 20)
                {
                    throw new InvalidDataException($"Invalid LOD count: {LodCount}");
                }

                // Read mesh sections (one per LOD)
                var sections = new List<MeshSection>();
                for (uint s = 0; s < LodCount && br.BaseStream.Position < data.Length; s++)
                {
                    var section = new MeshSection();
                    section.Read(br);
                    sections.Add(section);
                }
                
                MeshSections = sections.ToArray();
            }
        }

        /// <summary>
        /// Write content bytes (starts at BNRY, no file header).
        /// Caller must prepend the 16-byte file header before passing to ModifyRes().
        /// </summary>
        public byte[] Write()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // Write BNRY header (16 bytes)
                bw.Write(BnryHeader);
                
                // Write UnknownField at content offset 16
                bw.Write(UnknownField);
                
                // Write LodCount at content offset 20
                bw.Write(LodCount);

                // Write mesh sections
                if (MeshSections != null)
                {
                    foreach (var section in MeshSections)
                    {
                        section.Write(bw);
                    }
                }
                
                return ms.ToArray();
            }
        }

        /// <summary>
        /// MeshSection - per-vertex cloth simulation data
        /// </summary>
        public class MeshSection
        {
            public uint UnknownId { get; set; }
            public uint VertexCount { get; set; }
            public byte[] UnmappedBytes { get; set; } // 40 bytes
            public ClothVertex[] Vertices { get; set; }

            public void Read(BinaryReader br)
            {
                UnknownId = br.ReadUInt32();
                VertexCount = br.ReadUInt32();
                
                if (VertexCount > 100000)
                {
                    throw new InvalidDataException($"Invalid vertex count: {VertexCount}");
                }
                
                UnmappedBytes = br.ReadBytes(40);
                Vertices = new ClothVertex[VertexCount];

                for (int i = 0; i < VertexCount; i++)
                {
                    Vertices[i] = new ClothVertex
                    {
                        Position = new ClothVector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        NormalX = br.ReadInt16(),
                        NormalY = br.ReadInt16(),
                        TangentX = br.ReadInt16(),
                        TangentY = br.ReadInt16(),
                        Weight0 = br.ReadByte() / 255f,
                        Weight1 = br.ReadByte() / 255f,
                        Weight2 = br.ReadByte() / 255f,
                        Weight3 = br.ReadByte() / 255f,
                        Index0 = br.ReadUInt16(),
                        Index1 = br.ReadUInt16(),
                        Index2 = br.ReadUInt16(),
                        Index3 = br.ReadUInt16()
                    };
                }
            }

            public void Write(BinaryWriter bw)
            {
                bw.Write(UnknownId);
                bw.Write(VertexCount);
                bw.Write(UnmappedBytes);

                foreach (var v in Vertices)
                {
                    bw.Write(v.Position.X);
                    bw.Write(v.Position.Y);
                    bw.Write(v.Position.Z);
                    bw.Write(v.NormalX);
                    bw.Write(v.NormalY);
                    bw.Write(v.TangentX);
                    bw.Write(v.TangentY);
                    bw.Write((byte)(v.Weight0 * 255f));
                    bw.Write((byte)(v.Weight1 * 255f));
                    bw.Write((byte)(v.Weight2 * 255f));
                    bw.Write((byte)(v.Weight3 * 255f));
                    bw.Write(v.Index0);
                    bw.Write(v.Index1);
                    bw.Write(v.Index2);
                    bw.Write(v.Index3);
                }
            }
        }
    }

    public class ClothVertex
    {
        public ClothVector3 Position;
        public short NormalX, NormalY;
        public short TangentX, TangentY;
        public float Weight0, Weight1, Weight2, Weight3;
        public ushort Index0, Index1, Index2, Index3;
    }

    public struct ClothVector3
    {
        public float X, Y, Z;

        public ClothVector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public float DistanceSquared(ClothVector3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
