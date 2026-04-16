using System.Linq;
using System.Text;
using FrostySdk.Interfaces;
using MeshSetPlugin.Resources;

namespace MeshSetExtender.Helpers
{
    /// <summary>
    /// Logs a consolidated summary of all LODs after import/decimation.
    /// </summary>
    internal static class LodSummaryHelper
    {
        public static string GetMeshTypeName(MeshType type)
        {
            switch (type)
            {
                case MeshType.MeshType_Rigid: return "Rigid";
                case MeshType.MeshType_Skinned: return "Skinned";
                case MeshType.MeshType_Composite: return "Composite";
                default: return type.ToString();
            }
        }

        public static void LogSummary(ILogger logger, MeshSet meshSet, string assetName)
        {
            string typeName = GetMeshTypeName(meshSet.Type);
            int lodCount = meshSet.Lods.Count;
            int materialCount = meshSet.Lods[0].Sections.Count(s => !string.IsNullOrEmpty(s.Name) && s.VertexCount > 0);

            var sb = new StringBuilder();
            sb.AppendLine($"Auto LOD complete — {assetName} ({typeName}, {lodCount} LODs, {materialCount} materials)");

            for (int i = 0; i < lodCount; i++)
            {
                var lod = meshSet.Lods[i];
                uint totalTris = 0;
                foreach (var sec in lod.Sections)
                    totalTris += sec.PrimitiveCount;

                if (i == 0)
                    sb.AppendLine($"  LOD{i}: {totalTris:N0} tris (source)");
                else
                {
                    uint lod0Tris = 0;
                    foreach (var sec in meshSet.Lods[0].Sections)
                        lod0Tris += sec.PrimitiveCount;
                    float pct = lod0Tris > 0 ? (float)totalTris / lod0Tris * 100f : 0f;
                    sb.AppendLine($"  LOD{i}: {totalTris:N0} tris ({pct:F1}%)");
                }
            }

            logger.Log(sb.ToString().TrimEnd());
        }
    }
}
