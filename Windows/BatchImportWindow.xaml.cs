using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.Managers;
using MeshSetExtender.Helpers;
using MeshSetExtender.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MeshSetExtender.Windows
{
    public partial class BatchImportWindow : FrostyDockableWindow
    {
        public ObservableCollection<BatchMeshEntry> Entries { get; } = new ObservableCollection<BatchMeshEntry>();

        public BatchImportWindow()
        {
            InitializeComponent();
            MeshGrid.ItemsSource = Entries;
        }

        public BatchImportWindow(List<EbxAssetEntry> meshEntries) : this()
        {
            foreach (var entry in meshEntries)
            {
                Entries.Add(new BatchMeshEntry
                {
                    MeshEntry = entry,
                    AssetName = entry.Filename,
                    FbxFullPath = "",
                    FbxFileName = "(not selected)",
                    Enabled = false
                });
            }
            UpdateStatus();
        }

        private void BrowseFbx_Click(object sender, RoutedEventArgs e)
        {
            var entry = (sender as Button)?.Tag as BatchMeshEntry;
            if (entry == null) return;

            var ofd = new FrostyOpenFileDialog(
                $"Select FBX for {entry.AssetName}",
                "*.fbx (FBX Files)|*.fbx",
                "AutoLodBatch");

            if (ofd.ShowDialog())
            {
                entry.FbxFullPath = ofd.FileName;
                entry.FbxFileName = Path.GetFileName(ofd.FileName);
                entry.Enabled = true;
                UpdateStatus();
            }
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var entry in Entries.Where(x => !string.IsNullOrEmpty(x.FbxFullPath)))
                entry.Enabled = true;
            UpdateStatus();
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var entry in Entries) entry.Enabled = false;
            UpdateStatus();
        }

        private void AutoMatch_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog { Title = "Select folder containing FBX files" };
            if (!dialog.ShowDialog()) return;

            var fbxFiles = Directory.GetFiles(dialog.SelectedPath, "*.fbx", SearchOption.AllDirectories);

            foreach (var entry in Entries)
            {
                string assetNameLower = Path.GetFileNameWithoutExtension(entry.AssetName).ToLower();
                foreach (var fbx in fbxFiles)
                {
                    string fbxNameLower = Path.GetFileNameWithoutExtension(fbx).ToLower();
                    if (fbxNameLower.Contains(assetNameLower) || assetNameLower.Contains(fbxNameLower))
                    {
                        entry.FbxFullPath = fbx;
                        entry.FbxFileName = Path.GetFileName(fbx);
                        entry.Enabled = true;
                        break;
                    }
                }
            }

            UpdateStatus();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var selected = Entries.Where(x => x.Enabled && !string.IsNullOrEmpty(x.FbxFullPath)).ToList();
            if (selected.Count == 0)
            {
                FrostyMessageBox.Show("No meshes selected (or no FBX files matched).", "Batch Auto LOD Import");
                return;
            }

            // Show shared settings dialog once.
            var settings = new AutoLodImportSettings();
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

            // Resize the next Import/Export dialog to match this window's height (use ActualHeight if available)
            double dialogHeight = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
            AutoLodImporter.ResizeNextImportDialog(dialogHeight);

            if (FrostyImportExportBox.Show<AutoLodImportSettings>(
                    $"Import {selected.Count} Meshes — Auto LOD",
                    FrostyImportExportType.Import,
                    settings) != MessageBoxResult.OK)
                return;

            bool debug = settings.DebugLogging;
            int succeeded = 0, failed = 0;

            FrostyTaskWindow.Show($"Batch Auto LOD Import ({selected.Count} meshes)", "", (task) =>
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    var item = selected[i];
                    task.Update($"[{i + 1}/{selected.Count}] {item.AssetName}", (i * 100.0) / selected.Count);
                    try
                    {
                        AutoLodImporter.RunBatch(item.MeshEntry, item.FbxFullPath, settings, task, debug);
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        App.Logger.LogError($"{item.AssetName}: {ex.Message}");
                        if (debug) App.Logger.LogError(ex.ToString());
                    }
                }
            });

            settings.SaveAsDefaults();
            App.EditorWindow.DataExplorer.RefreshAll();

            FrostyMessageBox.Show(
                $"Batch import complete.\n\nSucceeded: {succeeded}\nFailed: {failed}",
                "Batch Auto LOD Import");

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateStatus()
        {
            int total = Entries.Count;
            int selected = Entries.Count(x => x.Enabled);
            int matched = Entries.Count(x => !string.IsNullOrEmpty(x.FbxFullPath));
            StatusText.Text = $"{selected} selected, {matched} FBX files matched out of {total} meshes";
        }
    }

    public class BatchMeshEntry : INotifyPropertyChanged
    {
        private bool _enabled;
        private string _fbxFullPath = "";
        private string _fbxFileName = "(not selected)";

        public EbxAssetEntry MeshEntry { get; set; }
        public string AssetName { get; set; }
        public int LodCount { get; set; } = 6;

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
        }

        public string FbxFullPath
        {
            get => _fbxFullPath;
            set { _fbxFullPath = value; OnPropertyChanged(nameof(FbxFullPath)); }
        }

        public string FbxFileName
        {
            get => _fbxFileName;
            set { _fbxFileName = value; OnPropertyChanged(nameof(FbxFileName)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}