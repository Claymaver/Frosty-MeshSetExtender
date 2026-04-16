using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.Managers;
using HarmonyLib;
using MeshSetPlugin;

using MeshSetExtender.Helpers;
using MeshSetExtender.Windows;

namespace MeshSetExtender.Patches
{
    /// <summary>
    /// Adds two toolbar buttons to the mesh editor:
    /// 1. "Auto LOD Import" — runs Auto LOD import flow on the current mesh asset.
    /// 2. "Export Res/Chunk" — exports all res and chunk files associated with the mesh.
    ///
    /// Registered under Harmony category "autolod" so it's enabled/disabled alongside
    /// the rest of the Auto LOD integration.
    /// </summary>
    [HarmonyPatch(typeof(FrostyMeshSetEditor), "RegisterToolbarItems")]
    [HarmonyPatchCategory("autolod")]
    public class MeshEditorToolbarPatch
    {
        // Priority.Normal so this postfix runs BEFORE the texture export patch (Priority.Low),
        // giving final order: [Export, Import, Auto LOD Import, Export Res/Chunk, Export Textures]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Normal)]
        static void Postfix(FrostyAssetEditor __instance, ref List<ToolbarItem> __result)
        {
            if (__result == null) return;

            if (!__result.Any(t => t.Text == "Auto LOD Import"))
            {
                __result.Add(new ToolbarItem(
                    "Auto LOD Import",
                    "Import an FBX as LOD0 and automatically generate decimated lower LODs",
                    "Images/Import.png",
                    new RelayCommand(
                        (o) => RunAutoLodImport(__instance),
                        (o) => true
                    )
                ));
            }

            if (!__result.Any(t => t.Text == "Export Res/Chunk"))
            {
                __result.Add(new ToolbarItem(
                    "Export Res/Chunk",
                    "Export this mesh's res and chunk files (MeshSet, shader block depots, cloth, etc)",
                    "Images/Export.png",
                    new RelayCommand(
                        (o) => RunExportResChunk(__instance),
                        (o) => true
                    )
                ));
            }
        }

        private static void RunAutoLodImport(FrostyAssetEditor editor)
        {
            EbxAssetEntry entry = editor?.AssetEntry as EbxAssetEntry;
            if (entry == null)
            {
                FrostyMessageBox.Show("No mesh asset is loaded.", "Auto LOD Import");
                return;
            }

            try
            {
                AutoLodImporter.Run(entry, editor);
            }
            catch (Exception ex)
            {
                App.Logger.LogError($"Auto LOD Import failed: {ex.Message}");
                FrostyMessageBox.Show($"Auto LOD Import failed:\n{ex.Message}", "Auto LOD Import");
            }
        }

        private static void RunExportResChunk(FrostyAssetEditor editor)
        {
            EbxAssetEntry entry = editor?.AssetEntry as EbxAssetEntry;
            if (entry == null)
            {
                FrostyMessageBox.Show("No mesh asset is loaded.", "Export Res/Chunk");
                return;
            }

            var resFiles = MeshResExportHelper.CollectResEntries(entry);
            if (resFiles.Count == 0)
            {
                FrostyMessageBox.Show("No res files found for this mesh asset.", "Export Res/Chunk");
                return;
            }

            var folderDialog = new VistaFolderBrowserDialog { Title = "Select export destination" };
            if (!folderDialog.ShowDialog())
            {
                App.Logger.Log("Canceled mesh res file export.");
                return;
            }

            string exportDir = Path.Combine(folderDialog.SelectedPath, entry.Filename);
            Directory.CreateDirectory(exportDir);

            MeshResExportResult result = default;

            FrostyTaskWindow.Show("Exporting Mesh Res Files", "", (task) =>
            {
                result = MeshResExportHelper.Export(entry, resFiles, exportDir, (msg, pct) => task.Update(msg, pct));
            });

            string summary = MeshResExportHelper.FormatSummary(result, exportDir);
            App.Logger.Log(summary);
            FrostyMessageBox.Show(summary, "Export Complete", MessageBoxButton.OK);
        }
    }
}
