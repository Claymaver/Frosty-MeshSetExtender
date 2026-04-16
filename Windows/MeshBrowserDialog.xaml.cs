using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Frosty.Controls;
using FrostySdk.Managers;

namespace MeshSetExtender.Windows
{
    /// <summary>
    /// View model for mesh asset display
    /// </summary>
    public class MeshItemViewModel
    {
        public string DisplayName { get; set; }
        public string FullPath { get; set; }
        public EbxAssetEntry Entry { get; set; }
    }

    /// <summary>
    /// Dialog for browsing and selecting mesh assets
    /// </summary>
    public partial class MeshBrowserDialog : FrostyDockableWindow
    {
        public EbxAssetEntry SelectedMesh { get; private set; }

        private List<EbxAssetEntry> _allMeshes;
        private ObservableCollection<MeshItemViewModel> _displayedMeshes;

        public MeshBrowserDialog(List<EbxAssetEntry> meshes, string title)
        {
            InitializeComponent();

            Title = title;
            _allMeshes = meshes;
            _displayedMeshes = new ObservableCollection<MeshItemViewModel>();
            MeshListBox.ItemsSource = _displayedMeshes;
        }

        private void FrostyDockableWindow_FrostyLoaded(object sender, EventArgs e)
        {
            PopulateList(_allMeshes);
        }

        private void PopulateList(IEnumerable<EbxAssetEntry> meshes)
        {
            _displayedMeshes.Clear();
            foreach (var mesh in meshes)
            {
                string displayName = System.IO.Path.GetFileName(mesh.Name);
                _displayedMeshes.Add(new MeshItemViewModel
                {
                    DisplayName = displayName,
                    FullPath = mesh.Name,
                    Entry = mesh
                });
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allMeshes == null) return;
            
            string filter = SearchTextBox.Text?.ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(filter))
            {
                PopulateList(_allMeshes);
            }
            else
            {
                var filtered = _allMeshes.Where(m => m.Name.ToLower().Contains(filter));
                PopulateList(filtered);
            }
        }

        private void MeshListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectAndClose();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAndClose();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectAndClose()
        {
            var selectedItem = MeshListBox.SelectedItem as MeshItemViewModel;
            if (selectedItem != null)
            {
                SelectedMesh = selectedItem.Entry;
                DialogResult = true;
                Close();
            }
        }
    }
}
