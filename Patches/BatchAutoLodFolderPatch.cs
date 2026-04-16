using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using App = Frosty.Core.App;
using Frosty.Controls;
using Frosty.Core.Controls;
using FrostySdk.Managers;
using HarmonyLib;

using MeshSetExtender.Windows;

namespace MeshSetExtender.Patches
{
    /// <summary>
    /// Adds "Batch Auto LOD Import" entries to the Data Explorer's folder context menu.
    /// Right-click any folder in the asset tree to launch the batch importer scoped to all
    /// mesh assets within (and optionally subfolders).
    ///
    /// Coexists with FrostyFlurryPlugin's RevertFolderPatch: if Flurry's patch already created
    /// a ContextMenu via the TreeView's ItemContainerStyle, we append our items to it instead
    /// of replacing the style. Runs at HarmonyPriority.Low so we postfix after Flurry's normal-
    /// priority patch and can detect its menu.
    /// </summary>
    [HarmonyPatch(typeof(FrostyDataExplorer), "OnApplyTemplate")]
    [HarmonyPatchCategory("autolod")]
    public class BatchAutoLodFolderPatch
    {
        private static readonly FieldInfo assetTreeViewField
            = AccessTools.Field(typeof(FrostyDataExplorer), "assetTreeView");

        private static readonly Type assetPathType
            = typeof(FrostyDataExplorer).Assembly.GetType("Frosty.Core.Controls.AssetPath");
        private static readonly PropertyInfo fullPathProp
            = assetPathType?.GetProperty("FullPath");

        // Tracks explorer instances we've already attached items to, so re-templating or other
        // plugins triggering OnApplyTemplate twice don't add duplicate menu items. Weak refs
        // so we don't pin explorer instances after they're disposed.
        private static readonly ConditionalWeakTable<FrostyDataExplorer, object> _patchedInstances
            = new ConditionalWeakTable<FrostyDataExplorer, object>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(FrostyDataExplorer __instance)
        {
            if (__instance == null) return;

            // Skip if we've already attached our items to this explorer instance.
            if (_patchedInstances.TryGetValue(__instance, out object _))
                return;
            _patchedInstances.Add(__instance, null);

            var treeView = assetTreeViewField?.GetValue(__instance) as TreeView;
            if (treeView == null) return;

            var batchItem = new MenuItem { Header = "Batch Auto LOD Import" };
            batchItem.Click += (s, e) => RunBatchOnFolder(treeView, includeSubfolders: false);

            var batchSubItem = new MenuItem { Header = "Batch Auto LOD Import + Subfolders" };
            batchSubItem.Click += (s, e) => RunBatchOnFolder(treeView, includeSubfolders: true);

            // Optional import icon (mirrors Flurry's icon-loading pattern).
            try
            {
                var icon = new ImageSourceConverter().ConvertFromString(
                    "pack://application:,,,/FrostyEditor;component/Images/Import.png") as ImageSource;
                if (icon != null)
                {
                    foreach (var item in new[] { batchItem, batchSubItem })
                    {
                        item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
                        RenderOptions.SetBitmapScalingMode(item.Icon as Image, BitmapScalingMode.Fant);
                    }
                }
            }
            catch { /* icon optional */ }

            void EnableOnFolderHandler(object _, RoutedEventArgs __)
            {
                bool hasFolder = treeView.SelectedItem != null
                    && assetPathType != null
                    && assetPathType.IsInstanceOfType(treeView.SelectedItem);
                batchItem.IsEnabled = hasFolder;
                batchSubItem.IsEnabled = hasFolder;
            }

            // If another plugin (e.g. Flurry's RevertFolderPatch) already attached a
            // ContextMenu, append to it instead of replacing the style — both menus then
            // appear in one combined right-click menu.
            ContextMenu existingMenu = FindExistingFolderMenu(treeView.ItemContainerStyle);
            if (existingMenu != null)
            {
                existingMenu.Items.Add(new Separator());
                existingMenu.Items.Add(batchItem);
                existingMenu.Items.Add(batchSubItem);
                existingMenu.Opened += EnableOnFolderHandler;
                return;
            }

            // No existing folder menu — create our own and apply via style (mirroring Flurry's pattern).
            var folderContextMenu = new ContextMenu();
            folderContextMenu.Items.Add(batchItem);
            folderContextMenu.Items.Add(batchSubItem);
            folderContextMenu.Opened += EnableOnFolderHandler;

            var existingStyle = treeView.ItemContainerStyle;
            var itemStyle = new Style(typeof(TreeViewItem));
            if (existingStyle != null) itemStyle.BasedOn = existingStyle;
            itemStyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, folderContextMenu));
            treeView.ItemContainerStyle = itemStyle;
        }

        private static ContextMenu FindExistingFolderMenu(Style style)
        {
            while (style != null)
            {
                foreach (var setterBase in style.Setters)
                {
                    if (setterBase is Setter s
                        && s.Property == FrameworkElement.ContextMenuProperty
                        && s.Value is ContextMenu cm)
                        return cm;
                }
                style = style.BasedOn;
            }
            return null;
        }

        private static void RunBatchOnFolder(TreeView treeView, bool includeSubfolders)
        {
            var selectedItem = treeView.SelectedItem;
            if (selectedItem == null || fullPathProp == null) return;

            string folderPath = (fullPathProp.GetValue(selectedItem) as string)?.Trim('/') ?? "";
            if (string.IsNullOrEmpty(folderPath))
            {
                FrostyMessageBox.Show("Right-click directly on a folder.", "Batch Auto LOD Import");
                return;
            }

            var meshEntries = new List<EbxAssetEntry>();
            foreach (var ebx in App.AssetManager.EnumerateEbx())
            {
                if (ebx.Type == null || !ebx.Type.Contains("MeshAsset")) continue;
                if (MatchesFolder(ebx.Path ?? "", folderPath, includeSubfolders))
                    meshEntries.Add(ebx);
            }

            if (meshEntries.Count == 0)
            {
                string scope = includeSubfolders ? "this folder or its subfolders" : "this folder";
                FrostyMessageBox.Show($"No mesh assets found in {scope}.\n\nFolder: {folderPath}",
                    "Batch Auto LOD Import");
                return;
            }

            var window = new BatchImportWindow(meshEntries) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
        }

        private static bool MatchesFolder(string assetPath, string folderPath, bool includeSubfolders)
        {
            if (includeSubfolders)
                return assetPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)
                    || assetPath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase);
            return assetPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
