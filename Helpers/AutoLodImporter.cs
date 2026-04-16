using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin;
using MeshSetPlugin.Resources;

using MeshSetExtender.Decimation;
using MeshSetExtender.Settings;

namespace MeshSetExtender.Helpers
{
    /// <summary>
    /// Orchestrates Auto LOD import: LOD0-only FBX import, reflection-based clone into lower
    /// LODs, decimation, and shader block depot updates. Callable from both toolbar and
    /// context menu.
    /// </summary>
    internal static class AutoLodImporter
    {
        /// <summary>
        /// Runs the full Auto LOD import flow on the given mesh entry.
        /// If <paramref name="editor"/> is supplied, the mesh editor's viewport is refreshed
        /// using the same sequence the vanilla Import button uses.
        /// </summary>
        public static void Run(EbxAssetEntry entry, FrostyAssetEditor editor = null)
        {
            if (entry == null) return;

            EbxAsset asset = App.AssetManager.GetEbx(entry);
            dynamic meshAsset = asset.RootObject;

            ulong resRid = meshAsset.MeshSetResource;
            ResAssetEntry resEntry = App.AssetManager.GetResEntry(resRid);
            MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(resEntry);

            int originalLodCount = meshSet.Lods.Count;
            if (originalLodCount <= 1)
            {
                FrostyMessageBox.Show(
                    "This mesh only has 1 LOD — Auto LOD requires at least 2 LODs.",
                    "Auto LOD");
                return;
            }

            FrostyOpenFileDialog ofd = new FrostyOpenFileDialog(
                "Import FBX (Auto LOD)", "*.fbx (FBX Files)|*.fbx", "Mesh");
            if (!ofd.ShowDialog()) return;
            string inputPath = ofd.FileName;

            AutoLodImportSettings settings = new AutoLodImportSettings();

            var config = new AutoLodConfig();
            config.Load();
            settings.Preset = config.Preset;
            settings.MaxError = config.MaxError;
            settings.LockBorders = config.LockBorders;
            settings.Lod1Ratio = config.Lod1Ratio;
            settings.Lod2Ratio = config.Lod2Ratio;
            settings.Lod3Ratio = config.Lod3Ratio;
            settings.Lod4Ratio = config.Lod4Ratio;
            settings.Lod5Ratio = config.Lod5Ratio;
            settings.DebugLogging = config.DebugLogging;

            if (meshSet.Type == MeshType.MeshType_Skinned)
                settings.SkeletonAsset = Config.Get<string>("MeshSetImportSkeleton", "", ConfigScope.Game);

            ResizeNextImportDialog(450);

            if (FrostyImportExportBox.Show<AutoLodImportSettings>(
                    $"Import Mesh — Auto LOD ({originalLodCount} LODs)",
                    FrostyImportExportType.Import,
                    settings) != MessageBoxResult.OK)
                return;

            if (meshSet.Type == MeshType.MeshType_Skinned && !string.IsNullOrEmpty(settings.SkeletonAsset))
                Config.Add("MeshSetImportSkeleton", settings.SkeletonAsset, ConfigScope.Game);

            bool debug = settings.DebugLogging;

            App.Logger.Log(settings.GetRatiosSummary(originalLodCount));

            // Pause the viewport while we mutate the mesh (mirrors vanilla Import button).
            if (editor != null) MeshEditorRefreshHelper.SetPaused(editor, true);

            try
            {
                FrostyTaskWindow.Show("Importing with Auto LOD", "", (task) =>
                {
                    try
                    {
                        ExecutePipeline(entry, asset, resEntry, meshSet, resRid, inputPath, settings, task, debug);
                        settings.SaveAsDefaults();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"Auto LOD import failed: {ex.Message}");
                        if (debug) App.Logger.LogError(ex.ToString());
                    }
                });

                // Refresh using the same pattern as vanilla ImportButton_Click.
                if (editor != null)
                    MeshEditorRefreshHelper.RefreshAfterImport(editor, meshSet, asset);
            }
            finally
            {
                if (editor != null) MeshEditorRefreshHelper.SetPaused(editor, false);
            }

            App.EditorWindow.DataExplorer.RefreshAll();
        }

        /// <summary>
        /// Runs the Auto LOD pipeline on a mesh that already has settings + FBX path resolved.
        /// Used by both the single-mesh interactive flow and the batch importer.
        /// </summary>
        public static void RunBatch(EbxAssetEntry entry, string fbxPath, AutoLodImportSettings settings,
            FrostyTaskWindow taskWindow, bool debug)
        {
            EbxAsset asset = App.AssetManager.GetEbx(entry);
            dynamic meshAsset = asset.RootObject;
            ulong resRid = meshAsset.MeshSetResource;
            ResAssetEntry resEntry = App.AssetManager.GetResEntry(resRid);
            MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(resEntry);

            if (meshSet.Lods.Count <= 1)
            {
                App.Logger.LogWarning($"{entry.Filename}: only 1 LOD — skipped.");
                return;
            }

            ExecutePipeline(entry, asset, resEntry, meshSet, resRid, fbxPath, settings, taskWindow, debug);
        }

        /// <summary>
        /// Core Auto LOD pipeline: import LOD0, native re-import for stride-mismatched LODs,
        /// verbatim clone for same-stride LODs, decimate, update shader block depots, commit.
        /// </summary>
        private static void ExecutePipeline(EbxAssetEntry entry, EbxAsset asset, ResAssetEntry resEntry,
            MeshSet meshSet, ulong resRid, string inputPath, AutoLodImportSettings settings,
            FrostyTaskWindow task, bool debug)
        {
            var originalLods = meshSet.Lods.ToList();
            uint lod0Stride = originalLods[0].Sections.Count > 0 ? originalLods[0].Sections[0].VertexStride : 0u;

            // Identify LODs whose native stride differs from LOD0's. For these we re-import
            // the FBX directly into their native structure, because format-
            // converting LOD0 bytes into their layout doesn't match their pre-compiled
            // shader bytecode. Same-stride LODs are still handled via verbatim clone.
            var lodsNeedingReimport = new List<int>();
            for (int i = 1; i < originalLods.Count; i++)
            {
                uint s = originalLods[i].Sections.Count > 0 ? originalLods[i].Sections[0].VertexStride : 0u;
                if (s != lod0Stride && s > 0) lodsNeedingReimport.Add(i);
            }

            // Import LOD0 first (standard path)
            task.Update("Importing LODs...", 10);
            while (meshSet.Lods.Count > 1)
                meshSet.Lods.RemoveAt(meshSet.Lods.Count - 1);

            var geomPatches = GeometryDeclPatcher.PatchForImport(meshSet, App.Logger, debug);
            new FBXImporter(App.Logger).ImportFBX(inputPath, meshSet, asset, entry, settings);
            GeometryDeclPatcher.Restore(geomPatches);

            MeshSetLod importedLod0 = meshSet.Lods[0];

            if (debug)
            {
                App.Logger.Log("[DEBUG] LOD0 after import:");
                foreach (var sec in importedLod0.Sections)
                    App.Logger.Log($"[DEBUG]   '{sec.Name}': V={sec.VertexCount}, P={sec.PrimitiveCount}, Stride={sec.VertexStride}");
            }

            // Re-import the FBX natively into each stride-mismatched LOD.
            var nativeImportedLods = new Dictionary<int, MeshSetLod>();
            for (int idx = 0; idx < lodsNeedingReimport.Count; idx++)
            {
                int lodIndex = lodsNeedingReimport[idx];
                meshSet.Lods.Clear();
                meshSet.Lods.Add(originalLods[lodIndex]);

                var patches = GeometryDeclPatcher.PatchForImport(meshSet, App.Logger, debug);
                new FBXImporter(App.Logger).ImportFBX(inputPath, meshSet, asset, entry, settings);
                GeometryDeclPatcher.Restore(patches);

                nativeImportedLods[lodIndex] = meshSet.Lods[0];

                if (debug)
                {
                    App.Logger.Log($"[DEBUG] LOD{lodIndex} after native re-import:");
                    foreach (var sec in meshSet.Lods[0].Sections)
                        App.Logger.Log($"[DEBUG]   '{sec.Name}': V={sec.VertexCount}, P={sec.PrimitiveCount}, Stride={sec.VertexStride}");
                }
            }

            // Reassemble full LOD structure
            meshSet.Lods.Clear();
            meshSet.Lods.Add(importedLod0);
            for (int i = 1; i < originalLods.Count; i++)
            {
                if (nativeImportedLods.TryGetValue(i, out var lod))
                    meshSet.Lods.Add(lod);
                else
                    meshSet.Lods.Add(originalLods[i]);
            }

            // Verbatim clone LOD0 into any remaining same-stride LODs
            DeepCloneLod0(meshSet, resEntry, debug);

            // Decimate all lower LODs (single static message — no per-LOD spam)
            task.Update("Decimating LODs...", 60);
            var decimator = new PostImportLodDecimator(App.Logger, debug, settings.MaxError, settings.LockBorders);
            decimator.DecimateLods(
                meshSet, resEntry, settings.GetRatios(),
                (message, progress) => task.Update("Decimating LODs...", 60 + progress * 0.3)
            );

            // Update shader block depots
            task.Update("Updating shader block depots...", 92);
            var shaderBlockDepots = new List<ShaderBlockDepot>();
            foreach (var linkedEntry in resEntry.LinkedAssets)
            {
                if (linkedEntry is ResAssetEntry linkedRes && linkedRes.Type == "ShaderBlockDepot")
                {
                    var depot = App.AssetManager.GetResAs<ShaderBlockDepot>(linkedRes);
                    if (depot != null) shaderBlockDepots.Add(depot);
                }
            }
            ShaderBlockDepotHelper.Update(meshSet, resEntry, shaderBlockDepots, debug);

            // Commit
            task.Update("Finalizing...", 98);
            App.AssetManager.ModifyRes(resRid, meshSet);
            entry.LinkAsset(resEntry);

            LodSummaryHelper.LogSummary(App.Logger, meshSet, entry.Filename);
        }

        /// <summary>
        /// Clones LOD0 into all lower LODs that share LOD0's vertex format. Different-stride
        /// LODs were natively re-imported earlier in ExecutePipeline, so their vertex data
        /// already matches their pre-compiled shader bytecode and we skip them here.
        /// </summary>
        private static void DeepCloneLod0(MeshSet meshSet, ResAssetEntry resEntry, bool debug)
        {
            if (meshSet.Lods.Count < 2) return;

            MeshSetLod lod0 = meshSet.Lods[0];

            byte[] lod0Bytes;
            using (Stream s = LodStreamHelper.GetLodStream(lod0))
            {
                if (s == null) { App.Logger.LogError("Failed to read LOD0 chunk."); return; }
                using (MemoryStream ms = new MemoryStream()) { s.CopyTo(ms); lod0Bytes = ms.ToArray(); }
            }

            uint lod0Stride = lod0.Sections.Count > 0 ? lod0.Sections[0].VertexStride : 0u;

            for (int i = 1; i < meshSet.Lods.Count; i++)
            {
                MeshSetLod targetLod = meshSet.Lods[i];
                uint targetStride = targetLod.Sections.Count > 0 ? targetLod.Sections[0].VertexStride : 0u;

                if (targetStride != lod0Stride && targetStride > 0)
                {
                    // Different-stride LODs were natively re-imported earlier in Run(), so
                    // their vertex data already matches their pre-compiled shader bytecode.
                    // Skip cloning — the decimator will trim their triangle count next.
                    if (debug)
                        App.Logger.Log($"[DEBUG] LOD{i} skipped in clone stage (stride={targetStride}, native-reimported)");
                    continue;
                }

                // Same stride — clone verbatim
                if (targetLod.ChunkId != Guid.Empty)
                {
                    App.AssetManager.ModifyChunk(targetLod.ChunkId, lod0Bytes);
                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(targetLod.ChunkId);
                    if (chunkEntry != null) resEntry.LinkAsset(chunkEntry);
                }
                else
                    targetLod.SetInlineData(lod0Bytes);

                targetLod.VertexBufferSize = lod0.VertexBufferSize;
                targetLod.IndexBufferSize = lod0.IndexBufferSize;
                targetLod.SetIndexBufferFormatSize(lod0.IndexUnitSize == 32 ? 4 : 2);

                var lod0Renderables = lod0.Sections.Where(s => !string.IsNullOrEmpty(s.Name)).ToList();
                var lod0DepthShadow = lod0.Sections.Where(s => string.IsNullOrEmpty(s.Name)).ToList();
                var targetRenderables = targetLod.Sections.Where(s => !string.IsNullOrEmpty(s.Name)).ToList();
                var targetDepthShadow = targetLod.Sections.Where(s => string.IsNullOrEmpty(s.Name)).ToList();

                for (int s = 0; s < targetRenderables.Count && s < lod0Renderables.Count; s++)
                    CloneSectionMetadata(lod0Renderables[s], targetRenderables[s]);

                for (int s = 0; s < targetDepthShadow.Count; s++)
                {
                    if (s < lod0DepthShadow.Count)
                        CloneSectionMetadata(lod0DepthShadow[s], targetDepthShadow[s]);
                    else
                    {
                        targetDepthShadow[s].VertexCount = 0;
                        targetDepthShadow[s].PrimitiveCount = 0;
                        targetDepthShadow[s].VertexOffset = 0;
                        targetDepthShadow[s].StartIndex = 0;
                    }
                }

                targetLod.ClearBones();
                if (lod0.BoneCount > 0)
                {
                    targetLod.BoneIndexArray.Clear();
                    foreach (uint b in lod0.BoneIndexArray) targetLod.BoneIndexArray.Add(b);
                    targetLod.BoneShortNameArray.Clear();
                    foreach (uint h in lod0.BoneShortNameArray) targetLod.BoneShortNameArray.Add(h);
                }

                LodMetadataHelper.CloneLodBounds(lod0, targetLod);

                if (debug) App.Logger.Log($"[DEBUG] LOD{i} cloned ({targetLod.Sections.Count} sections, stride={targetLod.Sections[0].VertexStride})");
            }
        }

        /// <summary>
        /// Clones section metadata including private fields (stride, geometry declaration,
        /// bones per vertex, primitive type) via reflection. Critical because stride-40 LODs
        /// need to be promoted to stride-52 to match cloned LOD0 data.
        /// </summary>
        private static void CloneSectionMetadata(MeshSetSection source, MeshSetSection target)
        {
            target.VertexOffset = source.VertexOffset;
            target.StartIndex = source.StartIndex;
            target.VertexCount = source.VertexCount;
            target.PrimitiveCount = source.PrimitiveCount;
            target.SetBones(source.BoneList);

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var t = typeof(MeshSetSection);

            foreach (string fieldName in new[] { "m_vertexStride", "m_geometryDeclarationDesc", "m_bonesPerVertex", "m_primitiveType" })
            {
                var field = t.GetField(fieldName, flags);
                if (field != null) field.SetValue(target, field.GetValue(source));
            }
        }

        public static void ResizeNextImportDialog(double height)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        if (win.GetType().Name.Contains("ImportExportBox") && win.IsVisible)
                        {
                            win.Height = height;
                            win.MinHeight = height;
                            break;
                        }
                    }
                }
                catch { }
            };
            timer.Start();
        }
    }
}
