using System.Windows;
using System.Windows.Media;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.Managers;

using MeshSetExtender.Helpers;
using MeshSetExtender.Windows;

namespace MeshSetExtender.ContextMenus
{
    public class ExportMeshResFilesContextMenu : DataExplorerContextMenuExtension
    {
        public override string ContextItemName => "Export Mesh Res/Chunk Files";

        public override ImageSource Icon => new ImageSourceConverter().ConvertFromString(
            "pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override RelayCommand ContextItemClicked => new RelayCommand(
            (o) => Execute(),
            (o) => CanExecute()
        );

        private bool CanExecute()
        {
            EbxAssetEntry entry = App.SelectedAsset;
            return entry != null && TypeLibrary.IsSubClassOf(entry.Type, "MeshAsset");
        }

        private void Execute()
        {
            EbxAssetEntry entry = App.SelectedAsset;
            if (entry == null) return;

            var resFiles = MeshResExportHelper.CollectResEntries(entry);
            if (resFiles.Count == 0)
            {
                FrostyMessageBox.Show("No res files found for this mesh asset.", "Export Mesh Res/Chunk Files", MessageBoxButton.OK);
                return;
            }

            var folderDialog = new VistaFolderBrowserDialog { Title = "Select export destination" };
            if (!folderDialog.ShowDialog())
            {
                App.Logger.Log("Canceled mesh res file export.");
                return;
            }

            string exportDir = System.IO.Path.Combine(folderDialog.SelectedPath, entry.Filename);
            System.IO.Directory.CreateDirectory(exportDir);

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
