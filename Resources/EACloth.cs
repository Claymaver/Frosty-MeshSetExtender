using System;
using System.IO;

namespace MeshSetExtender.Resources
{
    /// <summary>
    /// EACloth resource - stores EA cloth simulation data.
    /// Uses raw byte storage.
    /// The binary format is:
    ///   - Bytes 0-3: Data size (equals total_size - 16)
    ///   - Bytes 4-15: Padding (zeros)
    ///   - Bytes 16+: BNRY header and actual cloth simulation data
    /// </summary>
    public class EACloth
    {
        /// <summary>
        /// Raw bytes of the entire EACloth resource (including the 16-byte header)
        /// </summary>
        public byte[] Bytes { get; set; }

        public EACloth()
        {
        }

        /// <summary>
        /// Copy constructor - creates a deep copy by reading from the original's bytes.
        /// </summary>
        public EACloth(EACloth other)
        {
            if (other.Bytes != null)
            {
                // Create a copy by re-reading from the original bytes
                using (var ms = new MemoryStream(other.Bytes))
                using (var br = new BinaryReader(ms))
                {
                    Read(br);
                }
            }
        }

        /// <summary>
        /// Read the EACloth resource from a stream
        /// Format: First 4 bytes contain size of data after 16-byte header
        /// </summary>
        public void Read(BinaryReader br)
        {
            // Read size from first 4 bytes, then read entire resource
            uint dataSize = br.ReadUInt32();
            uint totalSize = dataSize + 16U;
            
            // Reset position and read all bytes
            br.BaseStream.Position = 0L;
            Bytes = br.ReadBytes((int)totalSize);
        }

        /// <summary>
        /// Write the EACloth resource to a stream
        /// </summary>
        public void Write(BinaryWriter bw)
        {
            if (Bytes != null)
            {
                bw.Write(Bytes);
            }
        }

        /// <summary>
        /// Convert to byte array for saving
        /// </summary>
        public byte[] ToBytes()
        {
            return Bytes;
        }
    }
}
