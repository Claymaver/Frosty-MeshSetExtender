using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Frosty.Core;
using FrostySdk.Managers;
using MeshSetPlugin.Resources;

namespace MeshSetExtender.Helpers
{
    /// <summary>
    /// Reads a LOD's chunk data (or inline data) as a Stream.
    /// </summary>
    internal static class LodStreamHelper
    {
        public static Stream GetLodStream(MeshSetLod lod)
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
    }

    /// <summary>
    /// Copies LOD-level bounding box from source to target so the game's frustum culler
    /// doesn't cull the converted LOD at distance using stale bounds.
    /// </summary>
    internal static class LodMetadataHelper
    {
        public static void CloneLodBounds(MeshSetLod source, MeshSetLod target)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var bboxField = typeof(MeshSetLod).GetField("m_boundingBox", flags);
            if (bboxField != null) bboxField.SetValue(target, bboxField.GetValue(source));
        }
    }

    /// <summary>
    /// Updates ShaderBlockDepot mesh params (primitive count, start index, vertex stream offset)
    /// to match the post-import LOD section layout. Iterates from LOD0 because our native
    /// re-import flow runs FBXImporter multiple times and can clobber the depot's LOD0 entries
    /// with values from later LODs.
    /// </summary>
    internal static class ShaderBlockDepotHelper
    {
        public static void Update(MeshSet meshSet, ResAssetEntry resEntry, List<ShaderBlockDepot> shaderBlockDepots, bool debug = false)
        {
            if (FrostySdk.ProfilesLibrary.DataVersion != (int)FrostySdk.ProfileVersion.StarWarsBattlefrontII || shaderBlockDepots.Count == 0)
                return;

            for (int lodIdx = 0; lodIdx < meshSet.Lods.Count; lodIdx++)
            {
                MeshSetLod lod = meshSet.Lods[lodIdx];

                foreach (var depot in shaderBlockDepots)
                {
                    var sbe = depot.GetSectionEntry(lodIdx);
                    if (sbe == null) continue;

                    for (int i = 0; i < lod.Sections.Count; i++)
                    {
                        try
                        {
                            MeshParamDbBlock meshParams = sbe.GetMeshParams(i);
                            if (meshParams != null)
                            {
                                meshParams.SetParameterValue("!primitiveCount", lod.Sections[i].PrimitiveCount);
                                meshParams.SetParameterValue("!startIndex", lod.Sections[i].StartIndex);
                                meshParams.SetParameterValue("!vertexStreamOffsets0", lod.Sections[i].VertexOffset);
                                meshParams.IsModified = true;
                            }
                        }
                        catch (ArgumentOutOfRangeException) { break; }
                    }

                    App.AssetManager.ModifyRes(depot.ResourceId, depot);
                    resEntry.LinkAsset(App.AssetManager.GetResEntry(depot.ResourceId));
                }
            }
        }
    }
}
