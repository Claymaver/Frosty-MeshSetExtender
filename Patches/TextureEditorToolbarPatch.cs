using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Viewport;
using Frosty.Core.Windows;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using HarmonyLib;
using MeshSetPlugin;
using TexturePlugin;

using MeshSetExtender.Windows;

namespace MeshSetExtender.Patches
{
    // Adds "Export Textures" button to mesh editor toolbar — bulk exports all textures
    // referenced by the mesh's materials via MeshMaterialCollection.
    [HarmonyPatch(typeof(FrostyMeshSetEditor), "RegisterToolbarItems")]
    [HarmonyPatchCategory("meshsetexporter")]
    public class MeshTextureExportToolbarPatch
    {
        private static readonly HashSet<FrostyAssetEditor> PatchedInstances = new HashSet<FrostyAssetEditor>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        static void Postfix(FrostyAssetEditor __instance, ref List<ToolbarItem> __result)
        {
            // Ensure the texture export button persists across asset switches for the same editor instance
            // by only adding if it isn't already present in the current toolbar list.
            if (__result != null && __result.Any(t => t.Text == "Export Textures"))
                return;

            // Some builds may not include the Texture Plugin at runtime.
            // Only add the "Export Textures" button if the TextureExporter type exists.
            if (!TextureExporterExists())
                return;

            __result.Add(new ToolbarItem(
                "Export Textures",
                "Bulk export all textures referenced by this mesh's materials",
                "Images/Export.png",
                new RelayCommand(
                    (o) => ExportMeshTextures(__instance),
                    (o) => true
                )
            ));
        }

        private static bool TextureExporterExists()
        {
            // Look up TextureExporter type via loaded assemblies to avoid hard dependency at patch load time
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("TexturePlugin.TextureExporter");
                if (t != null)
                    return true;
            }
            return false;
        }

        private static void ExportMeshTextures(FrostyAssetEditor editor)
        {
            EbxAssetEntry entry = editor.AssetEntry as EbxAssetEntry;
            if (entry == null) return;

            // Discover all textures via MeshMaterialCollection
            HashSet<Guid> textureGuids = new HashSet<Guid>();
            List<EbxAssetEntry> textureEntries = new List<EbxAssetEntry>();

            try
            {
                EbxAsset asset = App.AssetManager.GetEbx(entry);
                MeshMaterialCollection materials = new MeshMaterialCollection(asset, new PointerRef());

                foreach (MeshMaterial material in materials)
                {
                    foreach (dynamic texParam in material.TextureParameters)
                    {
                        try
                        {
                            PointerRef texRef = texParam.Value;
                            if (texRef.Type == PointerRefType.External)
                            {
                                Guid fileGuid = texRef.External.FileGuid;
                                if (fileGuid != Guid.Empty && !textureGuids.Contains(fileGuid))
                                {
                                    EbxAssetEntry texEntry = App.AssetManager.GetEbxEntry(fileGuid);
                                    if (texEntry != null)
                                    {
                                        textureGuids.Add(fileGuid);
                                        textureEntries.Add(texEntry);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.Log($"Failed to read mesh materials: {ex.Message}");
                FrostyMessageBox.Show($"Failed to read mesh materials:\n{ex.Message}", "Export Textures", MessageBoxButton.OK);
                return;
            }

            if (textureEntries.Count == 0)
            {
                FrostyMessageBox.Show("No textures found in this mesh's materials.", "Export Textures", MessageBoxButton.OK);
                return;
            }

            // Pick format
            var formatDialog = new TextureFormatDialog();
            if (formatDialog.ShowDialog() != true)
            {
                App.Logger.Log("Canceled texture export.");
                return;
            }
            string formatFilter = formatDialog.SelectedFormat;
            string formatExtension = formatDialog.SelectedExtension;

            // Pick output folder
            var folderDialog = new VistaFolderBrowserDialog
            {
                Title = $"Export {textureEntries.Count} texture(s) — select destination folder"
            };
            if (!folderDialog.ShowDialog())
            {
                App.Logger.Log("Canceled texture export.");
                return;
            }

            string exportDir = Path.Combine(folderDialog.SelectedPath, entry.Filename + "_textures");
            Directory.CreateDirectory(exportDir);

            int exported = 0;
            int failed = 0;

            FrostyTaskWindow.Show("Exporting Mesh Textures", "", (task) =>
            {
                for (int i = 0; i < textureEntries.Count; i++)
                {
                    EbxAssetEntry texEntry = textureEntries[i];
                    task.Update($"[{i + 1}/{textureEntries.Count}] {texEntry.Filename}...",
                        (double)i / textureEntries.Count * 100.0);

                    try
                    {
                        EbxAsset texAsset = App.AssetManager.GetEbx(texEntry);
                        dynamic textureAsset = (dynamic)texAsset.RootObject;

                        ResAssetEntry resEntry = App.AssetManager.GetResEntry(textureAsset.Resource);
                        Texture texture = App.AssetManager.GetResAs<Texture>(resEntry);

                        string outPath = Path.Combine(exportDir, texEntry.Filename + formatExtension);

                        string outDir = Path.GetDirectoryName(outPath);
                        if (!Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        TextureExporter exporter = new TextureExporter();
                        exporter.Export(texture, outPath, formatFilter);

                        exported++;
                        App.Logger.Log($"Exported texture: {texEntry.Name}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        App.Logger.Log($"Failed to export texture {texEntry.Name}: {ex.Message}");
                    }
                }
            });

            string summary = $"Exported {exported} texture(s) as {formatExtension.TrimStart('.')}";
            if (failed > 0)
                summary += $" ({failed} failed)";
            summary += $" to:\n{exportDir}";

            App.Logger.Log(summary);
            FrostyMessageBox.Show(summary, "Export Complete", MessageBoxButton.OK);
        }
    }
}
