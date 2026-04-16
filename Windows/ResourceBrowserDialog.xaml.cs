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
    /// View model for resource display in the list
    /// </summary>
    public class ResourceItemViewModel
    {
        public string Name { get; set; }
        public string TypeDisplay { get; set; }
        public ResAssetEntry Entry { get; set; }
    }

    /// <summary>
    /// Resource browser dialog using native Frosty styling
    /// </summary>
    public partial class ResourceBrowserDialog : FrostyDockableWindow
    {
        public ResAssetEntry SelectedResource { get; private set; }

        private List<ResAssetEntry> _allResources;
        private ObservableCollection<ResourceItemViewModel> _displayedResources;

        public ResourceBrowserDialog(List<ResAssetEntry> resources, string title)
        {
            InitializeComponent();

            Title = title;
            _allResources = resources;
            _displayedResources = new ObservableCollection<ResourceItemViewModel>();
            ResourceListBox.ItemsSource = _displayedResources;
        }

        private void FrostyDockableWindow_FrostyLoaded(object sender, EventArgs e)
        {
            PopulateList(_allResources);
        }

        private void PopulateList(IEnumerable<ResAssetEntry> resources)
        {
            _displayedResources.Clear();
            foreach (var res in resources)
            {
                _displayedResources.Add(new ResourceItemViewModel
                {
                    Name = res.Name,
                    TypeDisplay = $"Type: 0x{res.ResType:X8}",
                    Entry = res
                });
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Null check in case called before FrostyLoaded
            if (_allResources == null) return;
            
            string filter = SearchTextBox.Text?.ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(filter))
            {
                PopulateList(_allResources);
            }
            else
            {
                var filtered = _allResources.Where(r => r.Name.ToLower().Contains(filter));
                PopulateList(filtered);
            }
        }

        private void ResourceListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
            var selectedItem = ResourceListBox.SelectedItem as ResourceItemViewModel;
            if (selectedItem != null)
            {
                SelectedResource = selectedItem.Entry;
                DialogResult = true;
                Close();
            }
        }
    }
}
