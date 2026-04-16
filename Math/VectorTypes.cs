namespace MeshSetExtender.Math
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Vector3d
    {
        public double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct BoneWeight
    {
        public int boneIndex0, boneIndex1, boneIndex2, boneIndex3;
        public float weight0, weight1, weight2, weight3;
        public float boneWeight0 { get => weight0; set => weight0 = value; }
        public float boneWeight1 { get => weight1; set => weight1 = value; }
        public float boneWeight2 { get => weight2; set => weight2 = value; }
        public float boneWeight3 { get => weight3; set => weight3 = value; }
    }
}
