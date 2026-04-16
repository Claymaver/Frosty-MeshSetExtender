using System.Collections.Generic;
using FrostySdk;
using FrostySdk.Interfaces;
using MeshSetPlugin.Resources;

namespace MeshSetExtender.Helpers
{
    /// <summary>
    /// Temporarily patches vertex element usages that the FBXImporter doesn't support
    /// (e.g. SubMaterialIndex in composite meshes) so the import can proceed without errors.
    /// After import, the original usages are restored. The patched elements will contain
    /// zeroed data in the vertex buffer, which is acceptable for Auto LOD workflows.
    /// </summary>
    internal static class GeometryDeclPatcher
    {
        public struct ElementPatch
        {
            public GeometryDeclarationDesc.Element[] Elements;
            public int ElementIndex;
            public VertexElementUsage OriginalUsage;
        }

        /// <summary>
        /// Scans all LOD sections in the mesh and patches unsupported vertex element usages
        /// to RadiosityTexCoord. The importer skips this usage during read (no FBX source needed)
        /// and writes 4 zero bytes during write (matching the expected 4-byte element size).
        /// Returns a list of patches that must be restored via <see cref="Restore"/> after import.
        /// </summary>
        public static List<ElementPatch> PatchForImport(MeshSet meshSet, ILogger logger, bool debug)
        {
            var patches = new List<ElementPatch>();

            foreach (var lod in meshSet.Lods)
            {
                foreach (var section in lod.Sections)
                {
                    for (int d = 0; d < section.GeometryDeclDesc.Length; d++)
                    {
                        var decl = section.GeometryDeclDesc[d];
                        for (int e = 0; e < decl.ElementCount; e++)
                        {
                            if (!IsUnsupported(decl.Elements[e].Usage))
                                continue;

                            int size = decl.Elements[e].Size;
                            if (size != 4)
                            {
                                logger.LogWarning(
                                    $"Vertex element '{decl.Elements[e].Usage}' has size {size} " +
                                    $"(expected 4) in section '{section.Name}'. Import may produce incorrect vertex data.");
                            }

                            var originalUsage = decl.Elements[e].Usage;

                            // Elements is an array (reference type) so this modifies the original in-place
                            decl.Elements[e].Usage = VertexElementUsage.RadiosityTexCoord;

                            patches.Add(new ElementPatch
                            {
                                Elements = decl.Elements,
                                ElementIndex = e,
                                OriginalUsage = originalUsage
                            });

                            if (debug)
                                logger.Log($"[DEBUG] Patched {originalUsage} -> RadiosityTexCoord in '{section.Name}' (decl {d}, elem {e}, size {size})");
                        }
                    }
                }
            }

            if (patches.Count > 0)
                logger.Log($"Patched {patches.Count} unsupported vertex element(s) for import.");

            return patches;
        }

        /// <summary>
        /// Restores the original vertex element usages after import completes.
        /// </summary>
        public static void Restore(List<ElementPatch> patches)
        {
            foreach (var patch in patches)
            {
                patch.Elements[patch.ElementIndex].Usage = patch.OriginalUsage;
            }
        }

        private static bool IsUnsupported(VertexElementUsage usage)
        {
            switch (usage)
            {
                case VertexElementUsage.SubMaterialIndex:
                    return true;
                default:
                    return false;
            }
        }
    }
}
