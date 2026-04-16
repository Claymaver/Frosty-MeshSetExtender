using System;
using System.Collections.Generic;
using System.Linq;

namespace MeshSetExtender.Decimation
{
    /// <summary>
    /// Managed mesh decimation using quadric error metrics (Garland & Heckbert).
    /// Used as a fallback when the native meshoptimizer DLL is not available.
    /// 
    /// This operates on index/vertex data and produces a simplified index buffer
    /// that references a subset of the original vertices.
    /// </summary>
    public static class ManagedDecimator
    {
        private struct Vec3
        {
            public double X, Y, Z;

            public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

            public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

            public double Length => System.Math.Sqrt(X * X + Y * Y + Z * Z);

            public Vec3 Normalized()
            {
                double len = Length;
                return len > 1e-12 ? new Vec3(X / len, Y / len, Z / len) : new Vec3(0, 0, 0);
            }

            public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );

            public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>
        /// Symmetric 4x4 matrix for quadric error computation.
        /// Stored as 10 unique values: a11,a12,a13,a14,a22,a23,a24,a33,a34,a44
        /// </summary>
        private struct SymMat4
        {
            public double A11, A12, A13, A14;
            public double A22, A23, A24;
            public double A33, A34;
            public double A44;

            /// <summary>
            /// Construct quadric from plane equation ax + by + cz + d = 0
            /// </summary>
            public static SymMat4 FromPlane(double a, double b, double c, double d)
            {
                return new SymMat4
                {
                    A11 = a * a, A12 = a * b, A13 = a * c, A14 = a * d,
                    A22 = b * b, A23 = b * c, A24 = b * d,
                    A33 = c * c, A34 = c * d,
                    A44 = d * d
                };
            }

            public static SymMat4 operator +(SymMat4 a, SymMat4 b) => new SymMat4
            {
                A11 = a.A11 + b.A11, A12 = a.A12 + b.A12, A13 = a.A13 + b.A13, A14 = a.A14 + b.A14,
                A22 = a.A22 + b.A22, A23 = a.A23 + b.A23, A24 = a.A24 + b.A24,
                A33 = a.A33 + b.A33, A34 = a.A34 + b.A34,
                A44 = a.A44 + b.A44
            };

            /// <summary>
            /// Evaluate v^T * Q * v for a point v = (x,y,z)
            /// </summary>
            public double Evaluate(Vec3 v)
            {
                return A11 * v.X * v.X + 2 * A12 * v.X * v.Y + 2 * A13 * v.X * v.Z + 2 * A14 * v.X
                     + A22 * v.Y * v.Y + 2 * A23 * v.Y * v.Z + 2 * A24 * v.Y
                     + A33 * v.Z * v.Z + 2 * A34 * v.Z
                     + A44;
            }
        }

        private class Edge : IComparable<Edge>
        {
            public int V0, V1;
            public double Cost;
            public Vec3 OptimalPos;
            public bool Removed = false;

            public int CompareTo(Edge other) => Cost.CompareTo(other.Cost);
        }

        /// <summary>
        /// Simplify a mesh by collapsing edges using quadric error metrics.
        /// </summary>
        /// <param name="indices">Triangle indices into the vertex array</param>
        /// <param name="positions">Flat float array: x,y,z,x,y,z,... (only positions needed)</param>
        /// <param name="vertexCount">Number of vertices</param>
        /// <param name="targetRatio">Fraction of triangles to retain (0.0 - 1.0)</param>
        /// <returns>New index buffer referencing original vertices, or null if decimation failed</returns>
        public static uint[] Simplify(uint[] indices, float[] positions, int vertexCount, float targetRatio)
        {
            int triCount = indices.Length / 3;
            int targetTriCount = System.Math.Max(1, (int)(triCount * targetRatio));

            // Build vertex positions
            Vec3[] verts = new Vec3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                verts[i] = new Vec3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            }

            // Compute per-vertex quadrics from face planes
            SymMat4[] quadrics = new SymMat4[vertexCount];

            for (int t = 0; t < triCount; t++)
            {
                uint i0 = indices[t * 3], i1 = indices[t * 3 + 1], i2 = indices[t * 3 + 2];
                Vec3 n = Vec3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).Normalized();
                double d = -Vec3.Dot(n, verts[i0]);
                SymMat4 faceQ = SymMat4.FromPlane(n.X, n.Y, n.Z, d);

                quadrics[i0] = quadrics[i0] + faceQ;
                quadrics[i1] = quadrics[i1] + faceQ;
                quadrics[i2] = quadrics[i2] + faceQ;
            }

            // Build triangles as mutable list
            int[] tris = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                tris[i] = (int)indices[i];
            bool[] triRemoved = new bool[triCount];

            // Vertex remap: tracks which vertex each vertex has been collapsed into
            int[] remap = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                remap[i] = i;

            // Find the canonical vertex after chain of collapses
            int FindRoot(int v)
            {
                while (remap[v] != v) v = remap[v];
                return v;
            }

            // Build edge set and priority queue
            var edgeSet = new HashSet<long>();
            var edges = new List<Edge>();

            long EdgeKey(int a, int b)
            {
                if (a > b) { int tmp = a; a = b; b = tmp; }
                return ((long)a << 32) | (uint)b;
            }

            for (int t = 0; t < triCount; t++)
            {
                int v0 = tris[t * 3], v1 = tris[t * 3 + 1], v2 = tris[t * 3 + 2];
                int[][] pairs = { new[] { v0, v1 }, new[] { v1, v2 }, new[] { v2, v0 } };

                foreach (var pair in pairs)
                {
                    long key = EdgeKey(pair[0], pair[1]);
                    if (edgeSet.Add(key))
                    {
                        edges.Add(new Edge { V0 = pair[0], V1 = pair[1] });
                    }
                }
            }

            // Compute initial edge costs
            void ComputeEdgeCost(Edge e)
            {
                SymMat4 q = quadrics[e.V0] + quadrics[e.V1];
                // Use midpoint as optimal position (avoids matrix inversion issues)
                Vec3 mid = (verts[e.V0] + verts[e.V1]) * 0.5;
                e.Cost = q.Evaluate(mid);
                e.OptimalPos = mid;
            }

            foreach (var e in edges)
                ComputeEdgeCost(e);

            // Sort — we'll use a simple sorted approach and rebuild when needed
            // For production use, a proper priority queue would be better
            edges.Sort();

            int currentTriCount = triCount;

            // Collapse edges until we hit target
            int edgeIdx = 0;
            while (currentTriCount > targetTriCount && edgeIdx < edges.Count)
            {
                Edge e = edges[edgeIdx++];
                if (e.Removed)
                    continue;

                int v0 = FindRoot(e.V0);
                int v1 = FindRoot(e.V1);

                if (v0 == v1)
                    continue; // already collapsed

                // Collapse v1 into v0
                verts[v0] = e.OptimalPos;
                quadrics[v0] = quadrics[v0] + quadrics[v1];
                remap[v1] = v0;

                // Update triangles: replace v1 with v0, remove degenerates
                for (int t = 0; t < triCount; t++)
                {
                    if (triRemoved[t])
                        continue;

                    bool modified = false;
                    for (int j = 0; j < 3; j++)
                    {
                        int root = FindRoot(tris[t * 3 + j]);
                        if (root != tris[t * 3 + j])
                        {
                            tris[t * 3 + j] = root;
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                        if (a == b || b == c || a == c)
                        {
                            triRemoved[t] = true;
                            currentTriCount--;
                        }
                    }
                }

                // If we're far into the edge list, re-sort remaining edges
                if (edgeIdx > edges.Count / 2)
                {
                    var remaining = new List<Edge>();
                    for (int i = edgeIdx; i < edges.Count; i++)
                    {
                        var re = edges[i];
                        if (re.Removed) continue;

                        re.V0 = FindRoot(re.V0);
                        re.V1 = FindRoot(re.V1);
                        if (re.V0 == re.V1) continue;

                        ComputeEdgeCost(re);
                        remaining.Add(re);
                    }
                    remaining.Sort();
                    edges = remaining;
                    edgeIdx = 0;
                }
            }

            // Build output indices
            var result = new List<uint>();
            for (int t = 0; t < triCount; t++)
            {
                if (triRemoved[t])
                    continue;

                int a = FindRoot(tris[t * 3]);
                int b = FindRoot(tris[t * 3 + 1]);
                int c = FindRoot(tris[t * 3 + 2]);

                // Skip degenerate
                if (a == b || b == c || a == c)
                    continue;

                result.Add((uint)a);
                result.Add((uint)b);
                result.Add((uint)c);
            }

            return result.Count >= 3 ? result.ToArray() : null;
        }
    }
}
