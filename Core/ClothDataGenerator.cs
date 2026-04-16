using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeshSetExtender.Math;
using MeshSetExtender.Resources;

namespace MeshSetExtender.Core
{
    /// <summary>
    /// Internal mesh data structure for cloth processing
    /// </summary>
    public class MeshData
    {
        public string Name { get; set; }
        public Vector3d[] Vertices { get; set; }
        public int[] Indices { get; set; }
        public Vector3[] Normals { get; set; }
        public Vector4[] Tangents { get; set; }
        public Vector2[] UV1 { get; set; }
        public Vector2[] UV2 { get; set; }
        public Vector4[] Colors1 { get; set; }
        public Vector4[] Colors2 { get; set; }
        public BoneWeight[] BoneWeights { get; set; }

        public int VertexCount => Vertices?.Length ?? 0;
        public int TriangleCount => (Indices?.Length ?? 0) / 3;
    }

    /// <summary>
    /// Settings for cloth data generation
    /// </summary>
    public class ClothGeneratorSettings
    {
        public int Precision { get; set; } = 4;
        public float DefaultStiffness { get; set; } = 0.8f;
        public float DefaultInverseMass { get; set; } = 1.0f;
        public float MaxLinkDistance { get; set; } = 0.1f;
        public float PinThreshold { get; set; } = 0.9f;

        private string _hashFormat;
        public string HashFormat
        {
            get
            {
                if (_hashFormat == null)
                {
                    _hashFormat = "0." + new string('0', Precision);
                }
                return _hashFormat;
            }
        }
    }

    /// <summary>
    /// For cloth data, we simply copy templates.
    /// Inside Frosty, we copy existing cloth resources directly.
    /// </summary>
    public class ClothDataGenerator
    {
        private ClothGeneratorSettings _settings;
        private MeshData _sourceMesh;
        private ClothWrappingAsset _templateClothWrapping;
        private EACloth _templateEACloth;

        public ClothDataGenerator(ClothGeneratorSettings settings = null)
        {
            _settings = settings ?? new ClothGeneratorSettings();
        }

        public void SetSourceMesh(MeshData mesh)
        {
            _sourceMesh = mesh;
        }

        public void SetTemplateClothData(ClothWrappingAsset clothWrapping, EACloth eaCloth)
        {
            _templateClothWrapping = clothWrapping;
            _templateEACloth = eaCloth;
        }

        public void SetExistingClothData(ClothWrappingAsset clothWrapping, EACloth eaCloth)
        {
            SetTemplateClothData(clothWrapping, eaCloth);
        }

        /// <summary>
        /// Returns a copy of the template ClothWrappingAsset
        /// </summary>
        public ClothWrappingAsset GenerateClothWrappingAsset()
        {
            if (_templateClothWrapping == null)
                throw new InvalidOperationException("Template ClothWrappingAsset not set");

            // Simply copy the template
            return new ClothWrappingAsset(_templateClothWrapping);
        }

        /// <summary>
        /// Returns a copy of the template EACloth
        /// </summary>
        public EACloth GenerateEACloth()
        {
            if (_templateEACloth == null)
                throw new InvalidOperationException("Template EACloth not set");

            // Simply copy the template
            return new EACloth(_templateEACloth);
        }
    }
}
