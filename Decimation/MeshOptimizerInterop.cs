using System;
using System.Runtime.InteropServices;

namespace MeshSetExtender.Decimation
{
    /// <summary>
    /// P/Invoke wrapper for meshoptimizer (https://github.com/zeux/meshoptimizer).
    /// Build the native library for x64 and place meshoptimizer.dll in the output directory.
    /// 
    /// If meshoptimizer is not available, the plugin falls back to a managed quadric-error
    /// decimation implementation (see ManagedDecimator).
    /// </summary>
    public static class MeshOptimizerInterop
    {
        private const string DllName = "meshoptimizer.dll";

        /// <summary>
        /// Returns true if the native meshoptimizer DLL is loaded and callable.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_checkedAvailability)
                    return _isAvailable;

                _checkedAvailability = true;
                try
                {
                    // Attempt a trivial call to verify the DLL is present
                    meshopt_buildMeshletsBound((UIntPtr)0, (UIntPtr)0);
                    _isAvailable = true;
                }
                catch (DllNotFoundException)
                {
                    _isAvailable = false;
                }
                catch (EntryPointNotFoundException)
                {
                    // DLL is present but might be a different version — still usable for simplify
                    _isAvailable = true;
                }
                catch
                {
                    _isAvailable = false;
                }

                return _isAvailable;
            }
        }

        private static bool _isAvailable;
        private static bool _checkedAvailability;

        // ──────────────────────────────────────────────
        // meshopt_simplify
        // ──────────────────────────────────────────────
        // size_t meshopt_simplify(
        //     unsigned int* destination,
        //     const unsigned int* indices, size_t index_count,
        //     const float* vertex_positions, size_t vertex_count, size_t vertex_positions_stride,
        //     size_t target_index_count, float target_error,
        //     unsigned int options, float* result_error);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr meshopt_simplify(
            [Out] uint[] destination,
            [In] uint[] indices,
            UIntPtr index_count,
            [In] float[] vertex_positions,
            UIntPtr vertex_count,
            UIntPtr vertex_positions_stride,
            UIntPtr target_index_count,
            float target_error,
            uint options,
            [Out] float[] result_error
        );

        // ──────────────────────────────────────────────
        // meshopt_simplifySloppy (faster, less precise)
        // ──────────────────────────────────────────────
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr meshopt_simplifySloppy(
            [Out] uint[] destination,
            [In] uint[] indices,
            UIntPtr index_count,
            [In] float[] vertex_positions,
            UIntPtr vertex_count,
            UIntPtr vertex_positions_stride,
            UIntPtr target_index_count,
            float target_error,
            [Out] float[] result_error
        );

        // ──────────────────────────────────────────────
        // meshopt_optimizeVertexCache (post-simplification optimization)
        // ──────────────────────────────────────────────
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void meshopt_optimizeVertexCache(
            [Out] uint[] destination,
            [In] uint[] indices,
            UIntPtr index_count,
            UIntPtr vertex_count
        );

        // ──────────────────────────────────────────────
        // meshopt_optimizeVertexFetch (reorder vertices for cache)
        // ──────────────────────────────────────────────
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr meshopt_optimizeVertexFetch(
            IntPtr destination,
            [In, Out] uint[] indices,
            UIntPtr index_count,
            IntPtr vertices,
            UIntPtr vertex_count,
            UIntPtr vertex_size
        );

        // Used for availability check only
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr meshopt_buildMeshletsBound(UIntPtr index_count, UIntPtr max_vertices);

        // ──────────────────────────────────────────────
        // High-level wrapper
        // ──────────────────────────────────────────────

        /// <summary>
        /// Simplify a triangle mesh using meshoptimizer.
        /// </summary>
        /// <param name="indices">Source triangle indices</param>
        /// <param name="vertexPositions">Flat array of vertex positions (x,y,z,x,y,z,...)</param>
        /// <param name="targetRatio">Target ratio (0.0 - 1.0) of triangles to keep</param>
        /// <param name="maxError">Maximum allowed error (0.01 = 1% of mesh bounding box)</param>
        /// <returns>Simplified index buffer, or null on failure</returns>
        public static uint[] Simplify(uint[] indices, float[] vertexPositions, int vertexCount, float targetRatio, float maxError = 0.05f, uint options = 0u)
        {
            int targetIndexCount = (int)(indices.Length * targetRatio);
            // Round down to multiple of 3
            targetIndexCount = (targetIndexCount / 3) * 3;
            if (targetIndexCount < 3)
                targetIndexCount = 3;

            uint[] destination = new uint[indices.Length];
            float[] resultError = new float[1];

            UIntPtr resultCount = meshopt_simplify(
                destination,
                indices,
                (UIntPtr)indices.Length,
                vertexPositions,
                (UIntPtr)vertexCount,
                (UIntPtr)12, // stride = 3 floats * 4 bytes
                (UIntPtr)targetIndexCount,
                maxError,
                options,
                resultError
            );

            int count = (int)resultCount;
            if (count == 0)
                return null;

            uint[] result = new uint[count];
            Array.Copy(destination, result, count);

            // Optimize for vertex cache after simplification
            uint[] optimized = new uint[count];
            meshopt_optimizeVertexCache(
                optimized,
                result,
                (UIntPtr)count,
                (UIntPtr)vertexCount
            );

            return optimized;
        }
    }
}
