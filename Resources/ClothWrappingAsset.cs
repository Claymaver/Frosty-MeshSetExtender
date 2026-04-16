using System;
using System.IO;

namespace MeshSetExtender.Resources
{
    /// <summary>
    /// ClothWrappingAsset resource - stores cloth wrapping data
    /// Uses raw byte storage since parsing the complex format has proven unreliable.
    /// The binary format is:
    ///   - Bytes 0-3: Data size (total_size - 16)
    ///   - Bytes 4-15: Padding (zeros)  
    ///   - Bytes 16+: BNRY header and actual cloth data
    /// </summary>
    public class ClothWrappingAsset
    {
        /// <summary>
        /// Raw bytes of the entire resource (including the 16-byte header)
        /// </summary>
        public byte[] Bytes { get; set; }

        public ClothWrappingAsset()
        {
        }

        /// <summary>
        /// Copy constructor - creates a deep copy
        /// </summary>
        public ClothWrappingAsset(ClothWrappingAsset other)
        {
            if (other.Bytes != null)
            {
                Bytes = new byte[other.Bytes.Length];
                Array.Copy(other.Bytes, Bytes, other.Bytes.Length);
            }
        }

        /// <summary>
        /// Read the resource from a stream
        /// </summary>
        public void Read(BinaryReader br)
        {
            // Read the entire stream as raw bytes
            br.BaseStream.Position = 0L;
            Bytes = br.ReadBytes((int)br.BaseStream.Length);
        }

        /// <summary>
        /// Write the resource to a stream
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
