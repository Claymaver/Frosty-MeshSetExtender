using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.Managers;
using FrostySdk.IO;
using MeshSetExtender.Core;
using MeshSetExtender.Helpers;
using MeshSetExtender.Resources;

namespace MeshSetExtender.Windows
{
    /// <summary>
    /// Cloth Data Generator Window - copies cloth data from template mesh to target mesh
    /// 
    /// WORKFLOW:
    /// 1. Target mesh is auto-loaded from selection (the mesh you want to add cloth to)
    /// 2. User selects a template mesh asset that has cloth (e.g., Leia's skirt)
    /// 3. Plugin auto-finds the template's ClothWrapping and EACloth resources
    /// 4. Plugin auto-finds target mesh's existing cloth resources (if any)
    /// 5. Generate copies the template cloth data to the target
    /// 
    /// BINARY FORMAT:
    /// - GetRes() returns data WITHOUT the 16-byte header (starts at BNRY)
    /// - ModifyRes() expects data WITH the 16-byte header
    /// - Header: [size (4 bytes, = content length)] [12 zeros] [BNRY content...]
    /// </summary>
    public partial class ClothDataGeneratorWindow : FrostyDockableWindow
    {
        // Target mesh (the one we want to add cloth to)
        private EbxAssetEntry _targetMeshAssetEntry;
        private MeshData _targetMeshData;
        
        // Template mesh (has existing cloth we want to copy from)
        private EbxAssetEntry _templateMeshAssetEntry;
        
        // Template cloth resource entries (auto-detected from template mesh)
        private ResAssetEntry _templateClothWrappingEntry;
        private ResAssetEntry _templateEAClothEntry;
        
        // Target cloth resource entries (auto-detected from target mesh, to replace)
        private ResAssetEntry _targetClothWrappingEntry;
        private ResAssetEntry _targetEAClothEntry;
        
        // Raw template bytes (from GetRes, WITHOUT 16-byte header)
        private byte[] _templateClothWrappingBytes;
        private byte[] _templateEAClothBytes;
        
        // Parsed cloth data for adaptation
        private ClothWrappingAssetParsed _templateClothWrappingParsed;
        
        // Parsed target cloth data (if target already has cloth, used for normals/tangents)
        private ClothWrappingAssetParsed _targetClothWrappingParsed;
        
        // Target mesh MeshSet for vertex extraction
        private MeshSetPlugin.Resources.MeshSet _targetMeshSet;
        private MeshSetPlugin.Resources.MeshSet _templateMeshSet;
        
        // Bundle IDs for new resources
        private List<int> _meshBundles = new List<int>();

        // Sibling meshes that share the target's EACloth (empty when not shared).
        private List<EbxAssetEntry> _sharedEAClothUsers = new List<EbxAssetEntry>();

        public ClothDataGeneratorWindow() : this(null)
        {
        }

        public ClothDataGeneratorWindow(EbxAssetEntry selectedAsset)
        {
            InitializeComponent();

            if (selectedAsset != null)
            {
                _targetMeshAssetEntry = selectedAsset;
            }
        }

        private void FrostyDockableWindow_FrostyLoaded(object sender, EventArgs e)
        {
            if (_targetMeshAssetEntry != null)
            {
                LoadTargetMesh(_targetMeshAssetEntry);
            }
            else
            {
                // Try to get currently selected asset
                AssetEntry currentSelection = App.EditorWindow?.DataExplorer?.SelectedAsset;
                if (currentSelection != null && currentSelection.Type.Contains("MeshAsset"))
                {
                    EbxAssetEntry ebxEntry = currentSelection as EbxAssetEntry;
                    if (ebxEntry != null)
                    {
                        LoadTargetMesh(ebxEntry);
                    }
                }
            }
        }

        #region Target Mesh Loading

        private void LoadTargetMesh(EbxAssetEntry entry)
        {
            try
            {
                _targetMeshAssetEntry = entry;
                AssetPathText.Text = entry.Name;
                
                string baseName = System.IO.Path.GetFileName(entry.Name).ToLower();
                NewResourceNameText.Text = baseName;

                FrostyTaskWindow.Show("Loading Target Mesh", "", (task) =>
                {
                    task.Update("Loading mesh data...");
                    LoadTargetMeshData();
                    
                    task.Update("Finding existing cloth resources...");
                    AutoDetectTargetClothResources(entry.Name);
                    
                    // If target already has cloth wrapping, parse it for normals/tangents
                    if (_targetClothWrappingEntry != null)
                    {
                        task.Update("Loading target cloth data...");
                        LoadTargetClothWrapping();
                    }
                });

                if (_targetMeshData != null)
                {
                    MeshInfoText.Text = $"Verts: {_targetMeshData.VertexCount}, Tris: {_targetMeshData.TriangleCount}";
                }

                UpdateTargetResourcesUI();
                UpdateSharedClothWarning();

                StatusText.Text = _targetClothWrappingEntry != null || _targetEAClothEntry != null
                    ? "Target loaded with existing cloth. Select template mesh."
                    : "Target loaded (no cloth found). Select template mesh.";

                UpdateGenerateButton();
            }
            catch (Exception ex)
            {
                FrostyMessageBox.Show($"Error loading target: {ex.Message}", "Load Error", MessageBoxButton.OK);
                StatusText.Text = "Error loading target";
            }
        }

        private void LoadTargetMeshData()
        {
            var asset = App.AssetManager.GetEbx(_targetMeshAssetEntry);
            dynamic meshAsset = asset.RootObject;

            _meshBundles.Clear();
            _meshBundles.AddRange(_targetMeshAssetEntry.Bundles);
            if (_targetMeshAssetEntry.AddedBundles != null)
                _meshBundles.AddRange(_targetMeshAssetEntry.AddedBundles);

            ulong resRid = meshAsset.MeshSetResource;
            var resEntry = App.AssetManager.GetResEntry(resRid);

            if (resEntry == null)
                throw new Exception("Could not find mesh set resource");

            _targetMeshSet = App.AssetManager.GetResAs<MeshSetPlugin.Resources.MeshSet>(resEntry);
            _targetMeshData = ExtractMeshData(_targetMeshSet);
        }

        private MeshData ExtractMeshData(MeshSetPlugin.Resources.MeshSet meshSet)
        {
            var meshData = new MeshData();

            if (meshSet.Lods == null || meshSet.Lods.Count == 0)
                throw new Exception("No LODs found");

            var lod = meshSet.Lods[0];
            int vertexCount = 0;
            int indexCount = 0;

            foreach (var section in lod.Sections)
            {
                vertexCount += (int)section.VertexCount;
                // PrimitiveCount is number of triangles, multiply by 3 for indices
                indexCount += (int)section.PrimitiveCount * 3;
            }

            meshData.Vertices = new Math.Vector3d[vertexCount];
            meshData.Indices = new int[indexCount]; // Initialize indices array for proper TriangleCount
            return meshData;
        }

        /// <summary>
        /// Auto-detect cloth resources for the TARGET mesh
        /// Uses strict matching: must end with "_clothwrappingasset" or "_eacloth"
        /// </summary>
        private void AutoDetectTargetClothResources(string meshAssetPath)
        {
            _targetClothWrappingEntry = null;
            _targetEAClothEntry = null;
            
            // Extract the mesh name pattern (e.g., "obiwan_03_skirt" from the path)
            string meshName = System.IO.Path.GetFileName(meshAssetPath).ToLower();
            
            // Get parent path for search scope
            int lastSlash = meshAssetPath.LastIndexOf('/');
            string parentPath = lastSlash > 0 ? meshAssetPath.Substring(0, lastSlash).ToLower() : "";
            
            foreach (var resEntry in App.AssetManager.EnumerateRes())
            {
                string resName = resEntry.Name.ToLower();
                
                // Must be in same directory tree
                if (!resName.StartsWith(parentPath)) continue;
                
                // Strict matching for ClothWrappingAsset (must end with it)
                if (ClothAssetNaming.IsClothWrappingAsset(resName))
                {
                    if (_targetClothWrappingEntry == null)
                    {
                        _targetClothWrappingEntry = resEntry;
                        ClothLogger.LogDebug($"Auto-detected target ClothWrapping: {resEntry.Name}");
                    }
                }
                // Strict matching for EACloth / Cloth (must end with "_eacloth" or "_cloth")
                else if (ClothAssetNaming.IsClothAsset(resName))
                {
                    if (_targetEAClothEntry == null)
                    {
                        _targetEAClothEntry = resEntry;
                        ClothLogger.LogDebug($"Auto-detected target EACloth: {resEntry.Name}");
                    }
                }
            }
        }

        private void UpdateTargetResourcesUI()
        {
            TargetClothWrappingText.Text = _targetClothWrappingEntry?.Name ?? "(Will create new)";
            TargetEAClothText.Text = _targetEAClothEntry?.Name ?? "(Will create new)";
        }

        /// <summary>
        /// Finds other mesh assets in the same parent folder that resolve to the
        /// same EACloth res entry as the target. Returns empty list if not shared.
        ///
        /// A sibling is only counted as a sharer if:
        ///   1. It has its OWN ClothWrappingAsset that stem-matches its mesh name
        ///      (CW is strictly per-mesh — no matching CW = the sibling has no cloth).
        ///   2. That sibling's CW pairs with the target's EACloth by name-stem, OR
        ///      the folder contains exactly one EACloth (the shared one), so any
        ///      sibling with a CW implicitly references it.
        /// </summary>
        private List<EbxAssetEntry> DetectSharedEAClothUsers(ResAssetEntry sharedEACloth, EbxAssetEntry targetMesh)
        {
            var sharers = new List<EbxAssetEntry>();
            if (sharedEACloth == null || targetMesh == null) return sharers;

            string targetPath = targetMesh.Name.ToLower();
            int lastSlash = targetPath.LastIndexOf('/');
            string parentPath = lastSlash > 0 ? targetPath.Substring(0, lastSlash) : "";

            // Collect all cloth res entries in the parent folder once.
            var folderClothWrappings = new List<ResAssetEntry>();
            var folderEACloths = new List<ResAssetEntry>();
            foreach (var resEntry in App.AssetManager.EnumerateRes())
            {
                string resName = resEntry.Name.ToLower();
                if (!resName.StartsWith(parentPath)) continue;
                if (ClothAssetNaming.IsClothWrappingAsset(resName))
                    folderClothWrappings.Add(resEntry);
                else if (ClothAssetNaming.IsClothAsset(resName))
                    folderEACloths.Add(resEntry);
            }

            string sharedFileStem = FileStem(ClothAssetNaming.StripClothSuffix(sharedEACloth.Name.ToLower()));

            foreach (var ebx in App.AssetManager.EnumerateEbx())
            {
                if (ebx == targetMesh) continue;
                if (ebx.Type == null || !ebx.Type.Contains("MeshAsset")) continue;

                string siblingPath = ebx.Name.ToLower();
                int siblingSlash = siblingPath.LastIndexOf('/');
                string siblingParent = siblingSlash > 0 ? siblingPath.Substring(0, siblingSlash) : "";
                if (!string.Equals(siblingParent, parentPath, StringComparison.OrdinalIgnoreCase)) continue;

                string siblingFile = siblingPath.Substring(siblingSlash + 1);
                string siblingStem = siblingFile.EndsWith("_mesh")
                    ? siblingFile.Substring(0, siblingFile.Length - "_mesh".Length)
                    : siblingFile;

                // Step 1: sibling must own a ClothWrappingAsset that stem-matches its mesh.
                ResAssetEntry siblingCW = null;
                foreach (var cw in folderClothWrappings)
                {
                    string cwFile = FileStem(cw.Name.ToLower().Replace("_clothwrappingasset", ""));
                    if (cwFile == siblingStem || cwFile.EndsWith(siblingStem) || siblingStem.EndsWith(cwFile))
                    {
                        siblingCW = cw;
                        break;
                    }
                }
                if (siblingCW == null) continue; // No CW → no cloth at all.

                // Step 2: does this sibling's CW pair with the target's EACloth?
                string siblingCwFile = FileStem(siblingCW.Name.ToLower().Replace("_clothwrappingasset", ""));

                bool paired = false;
                // Exact stem-pair: sibling CW stem matches shared EACloth stem.
                if (siblingCwFile == sharedFileStem
                    || siblingCwFile.EndsWith(sharedFileStem)
                    || sharedFileStem.EndsWith(siblingCwFile))
                {
                    paired = true;
                }
                // Fallback: if the folder has exactly one EACloth and it's the shared one,
                // every CW in the folder implicitly references it (ARC kama pattern).
                else if (folderEACloths.Count == 1 && folderEACloths[0] == sharedEACloth)
                {
                    paired = true;
                }

                if (paired) sharers.Add(ebx);
            }
            return sharers;
        }

        private static string FileStem(string pathLower)
        {
            int s = pathLower.LastIndexOf('/');
            return s >= 0 ? pathLower.Substring(s + 1) : pathLower;
        }

        private void UpdateSharedClothWarning()
        {
            _sharedEAClothUsers.Clear();
            if (_targetEAClothEntry == null || _targetMeshAssetEntry == null)
            {
                SharedClothWarningPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _sharedEAClothUsers = DetectSharedEAClothUsers(_targetEAClothEntry, _targetMeshAssetEntry);
            if (_sharedEAClothUsers.Count == 0)
            {
                SharedClothWarningPanel.Visibility = Visibility.Collapsed;
                return;
            }

            string firstFew = string.Join(", ", _sharedEAClothUsers.Take(3).Select(m => m.Filename));
            string suffix = _sharedEAClothUsers.Count > 3
                ? $" (+{_sharedEAClothUsers.Count - 3} more)"
                : "";
            SharedClothWarningText.Text =
                $"Shared with {_sharedEAClothUsers.Count} other mesh(es): {firstFew}{suffix}. " +
                "Replacing it will affect every mesh that references it.";
            SharedClothWarningPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Loads and parses the target's existing ClothWrappingAsset.
        /// This provides normals/tangents already in the correct cloth encoding,
        /// which avoids shadow artifacts from encoding mismatches.
        /// </summary>
        private void LoadTargetClothWrapping()
        {
            _targetClothWrappingParsed = null;
            
            try
            {
                byte[] targetClothBytes;
                using (var stream = App.AssetManager.GetRes(_targetClothWrappingEntry))
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        targetClothBytes = ms.ToArray();
                    }
                }
                
                _targetClothWrappingParsed = new ClothWrappingAssetParsed();
                _targetClothWrappingParsed.Read(targetClothBytes);
                ClothLogger.LogDebug($"Parsed target ClothWrapping: {_targetClothWrappingParsed.LodCount} LODs, {_targetClothWrappingParsed.MeshSections[0].VertexCount} vertices in LOD0 (will use for normals/tangents)");
            }
            catch (Exception ex)
            {
                ClothLogger.LogWarning($"Could not parse target ClothWrapping: {ex.Message} (will fall back to mesh vertex normals)");
                _targetClothWrappingParsed = null;
            }
        }

        #endregion

        #region Template Selection

        /// <summary>
        /// Browse for a mesh asset to use as template
        /// </summary>
        private void BrowseTemplateMesh_Click(object sender, RoutedEventArgs e)
        {
            List<EbxAssetEntry> meshAssets = null;
            
            FrostyTaskWindow.Show("Finding Meshes with Cloth", "", (task) =>
            {
                task.Update("Scanning cloth resources...");
                
                // First pass: collect all parent paths that have cloth resources
                var pathsWithCloth = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var resEntry in App.AssetManager.EnumerateRes())
                {
                    string resName = resEntry.Name.ToLower();
                    if (ClothAssetNaming.IsClothWrappingAsset(resName) || ClothAssetNaming.IsClothAsset(resName))
                    {
                        // Extract parent path
                        int lastSlash = resEntry.Name.LastIndexOf('/');
                        if (lastSlash > 0)
                        {
                            // Go up one more level for cloth resources in subdirs like /cloth/
                            string parentPath = resEntry.Name.Substring(0, lastSlash);
                            pathsWithCloth.Add(parentPath.ToLower());
                            
                            // Also add grandparent for cases like mesh/cloth/eacloth
                            int secondLastSlash = parentPath.LastIndexOf('/');
                            if (secondLastSlash > 0)
                            {
                                pathsWithCloth.Add(parentPath.Substring(0, secondLastSlash).ToLower());
                            }
                        }
                    }
                }
                
                task.Update($"Found {pathsWithCloth.Count} paths with cloth. Scanning meshes...");
                
                // Second pass: find mesh assets in those paths
                meshAssets = new List<EbxAssetEntry>();
                
                foreach (var ebxEntry in App.AssetManager.EnumerateEbx())
                {
                    if (ebxEntry.Type != null && ebxEntry.Type.Contains("SkinnedMeshAsset"))
                    {
                        string meshPath = ebxEntry.Name.ToLower();
                        int lastSlash = meshPath.LastIndexOf('/');
                        string parentPath = lastSlash > 0 ? meshPath.Substring(0, lastSlash) : meshPath;
                        
                        if (pathsWithCloth.Contains(parentPath))
                        {
                            meshAssets.Add(ebxEntry);
                        }
                    }
                }
                
                meshAssets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            });
            
            if (meshAssets == null || meshAssets.Count == 0)
            {
                FrostyMessageBox.Show("No mesh assets with cloth data found.", "No Templates", MessageBoxButton.OK);
                return;
            }
            
            var dialog = new MeshBrowserDialog(meshAssets, "Select Template Mesh (with cloth)");
            if (dialog.ShowDialog() == true && dialog.SelectedMesh != null)
            {
                LoadTemplateMesh(dialog.SelectedMesh);
            }
        }

        private void LoadTemplateMesh(EbxAssetEntry meshEntry)
        {
            _templateMeshAssetEntry = meshEntry;
            _templateClothWrappingEntry = null;
            _templateEAClothEntry = null;
            _templateClothWrappingBytes = null;
            _templateEAClothBytes = null;
            _templateClothWrappingParsed = null;
            _templateMeshSet = null;
            
            try
            {
                FrostyTaskWindow.Show("Loading Template", "", (task) =>
                {
                    task.Update("Loading template mesh...");
                    LoadTemplateMeshSet(meshEntry);
                    
                    task.Update("Finding cloth resources...");
                    AutoDetectTemplateClothResources(meshEntry.Name);
                    
                    if (_templateClothWrappingEntry != null)
                    {
                        task.Update("Loading ClothWrapping...");
                        LoadTemplateClothWrapping();
                    }
                    
                    if (_templateEAClothEntry != null)
                    {
                        task.Update("Loading EACloth...");
                        LoadTemplateEACloth();
                    }
                });
                
                // Update UI
                TemplateClothWrappingText.Text = _templateClothWrappingEntry?.Name ?? "(Not found)";
                TemplateEAClothText.Text = _templateEAClothEntry?.Name ?? "(Not found)";
                
                if (_templateClothWrappingBytes != null && _templateEAClothBytes != null)
                {
                    StatusText.Text = $"Template loaded: {meshEntry.Name}";
                }
                else
                {
                    StatusText.Text = "Warning: Could not load all template cloth data";
                }
                
                UpdateGenerateButton();
            }
            catch (Exception ex)
            {
                FrostyMessageBox.Show($"Error loading template: {ex.Message}", "Error", MessageBoxButton.OK);
                StatusText.Text = "Error loading template";
            }
        }

        private void LoadTemplateMeshSet(EbxAssetEntry meshEntry)
        {
            var asset = App.AssetManager.GetEbx(meshEntry);
            dynamic meshAsset = asset.RootObject;

            ulong resRid = meshAsset.MeshSetResource;
            var resEntry = App.AssetManager.GetResEntry(resRid);

            if (resEntry != null)
            {
                _templateMeshSet = App.AssetManager.GetResAs<MeshSetPlugin.Resources.MeshSet>(resEntry);
                ClothLogger.LogDebug($"Loaded template MeshSet: {meshEntry.Name}");
            }
        }

        /// <summary>
        /// Auto-detect cloth resources for the TEMPLATE mesh
        /// Finds matching pairs (ClothWrapping and EACloth with same base name)
        /// </summary>
        private void AutoDetectTemplateClothResources(string meshAssetPath)
        {
            string meshPathLower = meshAssetPath.ToLower();
            int lastSlash = meshPathLower.LastIndexOf('/');
            string parentPath = lastSlash > 0 ? meshPathLower.Substring(0, lastSlash) : "";
            
            // Collect all cloth resources for this mesh
            var clothWrappings = new List<ResAssetEntry>();
            var eaCloths = new List<ResAssetEntry>();
            
            foreach (var resEntry in App.AssetManager.EnumerateRes())
            {
                string resName = resEntry.Name.ToLower();
                
                if (!resName.StartsWith(parentPath)) continue;
                
                if (ClothAssetNaming.IsClothWrappingAsset(resName))
                {
                    clothWrappings.Add(resEntry);
                }
                else if (ClothAssetNaming.IsClothAsset(resName))
                {
                    eaCloths.Add(resEntry);
                }
            }
            
            // Try to find matching pairs by base name
            // e.g., "leia_princess_01_skirt" should match both "_skirt_clothwrappingasset" and "_skirt_eacloth"
            foreach (var cw in clothWrappings)
            {
                // Extract base name from clothwrapping (remove suffix)
                string cwName = cw.Name.ToLower();
                string baseName = cwName.Replace("_clothwrappingasset", "").Replace("clothwrappingasset", "");
                
                // Extract the identifying part (e.g., "skirt" from "leia_princess_01_skirt")
                int cwLastSlash = baseName.LastIndexOf('/');
                string cwFileName = cwLastSlash >= 0 ? baseName.Substring(cwLastSlash + 1) : baseName;
                
                // Find matching EACloth - look for one that contains the same identifier
                foreach (var ec in eaCloths)
                {
                    string ecName = ec.Name.ToLower();
                    string ecBaseName = ClothAssetNaming.StripClothSuffix(ecName);
                    int ecLastSlash = ecBaseName.LastIndexOf('/');
                    string ecFileName = ecLastSlash >= 0 ? ecBaseName.Substring(ecLastSlash + 1) : ecBaseName;
                    
                    // Check if they share the same base identifier
                    // e.g., both contain "skirt" or both end with same pattern
                    if (cwFileName == ecFileName || cwFileName.EndsWith(ecFileName) || ecFileName.EndsWith(cwFileName))
                    {
                        _templateClothWrappingEntry = cw;
                        _templateEAClothEntry = ec;
                        ClothLogger.LogDebug($"Found matching template pair:");
                        ClothLogger.LogDebug($"  ClothWrapping: {cw.Name}");
                        ClothLogger.LogDebug($"  EACloth: {ec.Name}");
                        return;
                    }
                }
            }
            
            // Fallback: just use first of each if no matching pair found
            if (clothWrappings.Count > 0)
            {
                _templateClothWrappingEntry = clothWrappings[0];
                ClothLogger.LogDebug($"Found template ClothWrapping (no match): {clothWrappings[0].Name}");
            }
            if (eaCloths.Count > 0)
            {
                _templateEAClothEntry = eaCloths[0];
                ClothLogger.LogDebug($"Found template EACloth (no match): {eaCloths[0].Name}");
            }
        }

        private void LoadTemplateClothWrapping()
        {
            using (var stream = App.AssetManager.GetRes(_templateClothWrappingEntry))
            {
                _templateClothWrappingBytes = ReadAllBytes(stream);
            }
            ClothLogger.LogDebug($"Loaded ClothWrapping: {_templateClothWrappingBytes.Length} bytes");
            
            // Parse the cloth wrapping data for adaptation
            try
            {
                _templateClothWrappingParsed = new ClothWrappingAssetParsed();
                _templateClothWrappingParsed.Read(_templateClothWrappingBytes);
                ClothLogger.LogDebug($"Parsed ClothWrapping: {_templateClothWrappingParsed.LodCount} LODs, {_templateClothWrappingParsed.MeshSections.Length} sections, {_templateClothWrappingParsed.MeshSections[0].VertexCount} vertices in LOD0");
            }
            catch (Exception ex)
            {
                ClothLogger.LogWarning($"Could not parse ClothWrapping: {ex.Message}");
                _templateClothWrappingParsed = null;
            }
        }

        private void LoadTemplateEACloth()
        {
            using (var stream = App.AssetManager.GetRes(_templateEAClothEntry))
            {
                _templateEAClothBytes = ReadAllBytes(stream);
            }
            ClothLogger.LogDebug($"Loaded EACloth: {_templateEAClothBytes.Length} bytes");
        }

        private byte[] ReadAllBytes(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        #endregion

        #region Manual Target Browsing

        private void BrowseTargetClothWrapping_Click(object sender, RoutedEventArgs e)
        {
            var entry = BrowseForClothResource("Select ClothWrapping to Replace", ClothAssetNaming.IsClothWrappingAsset, "ClothWrapping");
            if (entry != null)
            {
                _targetClothWrappingEntry = entry;
                TargetClothWrappingText.Text = entry.Name;
                StatusText.Text = $"Will replace: {entry.Name}";
            }
        }

        private void BrowseTargetEACloth_Click(object sender, RoutedEventArgs e)
        {
            var entry = BrowseForClothResource("Select EACloth / Cloth to Replace", ClothAssetNaming.IsClothAsset, "EACloth/Cloth");
            if (entry != null)
            {
                _targetEAClothEntry = entry;
                TargetEAClothText.Text = entry.Name;
                StatusText.Text = $"Will replace: {entry.Name}";
                UpdateSharedClothWarning();
            }
        }

        private void ClearTargetClothWrapping_Click(object sender, RoutedEventArgs e)
        {
            _targetClothWrappingEntry = null;
            _targetClothWrappingParsed = null;
            TargetClothWrappingText.Text = "(Will create new)";
            StatusText.Text = "Cleared target ClothWrapping";
        }

        private void ClearTargetEACloth_Click(object sender, RoutedEventArgs e)
        {
            _targetEAClothEntry = null;
            TargetEAClothText.Text = "(Will create new)";
            StatusText.Text = "Cleared target EACloth";
            UpdateSharedClothWarning();
        }

        private ResAssetEntry BrowseForClothResource(string title, Func<string, bool> predicate, string kindLabel)
        {
            var resources = new List<ResAssetEntry>();

            foreach (var resEntry in App.AssetManager.EnumerateRes())
            {
                if (predicate(resEntry.Name.ToLower()))
                {
                    resources.Add(resEntry);
                }
            }

            if (resources.Count == 0)
            {
                FrostyMessageBox.Show($"No {kindLabel} resources found.", "No Resources", MessageBoxButton.OK);
                return null;
            }

            resources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var dialog = new ResourceBrowserDialog(resources, title);
            if (dialog.ShowDialog() == true)
            {
                return dialog.SelectedResource;
            }

            return null;
        }

        #endregion

        private void UpdateGenerateButton()
        {
            generateButton.IsEnabled = _templateClothWrappingBytes != null && _templateEAClothBytes != null;
        }

        /// <summary>
        /// Prepends the 16-byte header to content bytes
        /// Header: [size (4 bytes)] [12 zeros]
        /// This is required because GetRes() strips the header but ModifyRes() expects it
        /// </summary>
        private byte[] PrependHeader(byte[] contentBytes)
        {
            byte[] result = new byte[contentBytes.Length + 16];
            
            // Size field = content length (little-endian)
            uint size = (uint)contentBytes.Length;
            result[0] = (byte)(size & 0xFF);
            result[1] = (byte)((size >> 8) & 0xFF);
            result[2] = (byte)((size >> 16) & 0xFF);
            result[3] = (byte)((size >> 24) & 0xFF);
            
            // Bytes 4-15 are zeros (already initialized)
            
            // Copy content at offset 16
            Array.Copy(contentBytes, 0, result, 16, contentBytes.Length);
            
            return result;
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_templateClothWrappingBytes == null || _templateEAClothBytes == null)
            {
                FrostyMessageBox.Show("Please select a template mesh first.", "Template Required", MessageBoxButton.OK);
                return;
            }

            if (_targetMeshSet == null)
            {
                FrostyMessageBox.Show("Target mesh not loaded.", "Error", MessageBoxButton.OK);
                return;
            }

            if (_targetEAClothEntry != null && _sharedEAClothUsers != null && _sharedEAClothUsers.Count > 0)
            {
                string list = string.Join("\n  • ", _sharedEAClothUsers.Select(m => m.Name));
                var result = FrostyMessageBox.Show(
                    $"The EACloth resource you're about to replace is shared with {_sharedEAClothUsers.Count} other mesh(es):\n\n  • {list}\n\n" +
                    "These meshes will all receive the new cloth data. Continue?",
                    "Shared Cloth Asset",
                    MessageBoxButton.YesNo);
                if (result != MessageBoxResult.Yes) return;
            }

            string newResourceName = NewResourceNameText.Text.Trim().ToLower();
            int precision = (int)PrecisionSlider.Value;
            ClothLogger.DebugMode = DebugCheckBox.IsChecked == true;
            var targetClothWrappingEntry = _targetClothWrappingEntry;
            var targetEAClothEntry = _targetEAClothEntry;
            var meshBundles = _meshBundles.ToArray();
            
            byte[] adaptedClothWrappingBytes = null;
            byte[] eaClothBytes = null;

            try
            {
                FrostyTaskWindow.Show("Generating Cloth Data", "", (task) =>
                {
                    // Step 1: Extract target mesh vertices for all LODs
                    task.Update("Extracting target mesh vertices...");
                    int targetLodCount = _targetMeshSet.Lods.Count;
                    int templateLodCount = _templateClothWrappingParsed.MeshSections.Length;
                    int lodCount = System.Math.Min(targetLodCount, templateLodCount);
                    var targetLodVertexData = new ExtractedVertexData[lodCount][];
                    for (int lod = 0; lod < lodCount; lod++)
                    {
                        try
                        {
                            targetLodVertexData[lod] = ClothDataAdapter.ExtractMeshVertices(_targetMeshSet, lod);
                            ClothLogger.LogDebug($"Target mesh LOD{lod}: {targetLodVertexData[lod].Length} vertices");
                        }
                        catch (Exception ex)
                        {
                            ClothLogger.LogWarning($"Could not extract target LOD{lod}: {ex.Message}");
                            targetLodVertexData[lod] = null;
                        }
                    }

                    // Step 2: Extract template mesh vertices (positions + normals for matching)
                    // If template and target are the same asset, the mesh has been replaced by import
                    // so mesh vertices no longer match the cloth wrapping — skip extraction and
                    // let the adapter match directly against cloth wrapping positions instead
                    task.Update("Extracting template mesh vertices...");
                    ExtractedVertexData[] templateVertexData = null;
                    bool isSameAsset = _templateMeshAssetEntry != null && _targetMeshAssetEntry != null
                        && _templateMeshAssetEntry.Name == _targetMeshAssetEntry.Name;

                    if (isSameAsset)
                    {
                        ClothLogger.Log("Template and target are the same asset — matching against cloth wrapping positions directly");
                    }
                    else if (_templateMeshSet != null)
                    {
                        templateVertexData = ClothDataAdapter.ExtractMeshVertices(_templateMeshSet);
                        ClothLogger.LogDebug($"Template mesh: {templateVertexData.Length} vertices");
                    }

                    // Step 3: Adapt cloth wrapping data for all LODs
                    task.Update("Adapting cloth data to target mesh...");
                    var adapterSettings = new ClothAdapterSettings { Precision = precision };
                    var adaptedClothWrapping = ClothDataAdapter.AdaptClothWrapping(targetLodVertexData, templateVertexData, _templateClothWrappingParsed, adapterSettings);
                    
                    // Step 3: Write adapted cloth wrapping to bytes (content only, starts at BNRY)
                    task.Update("Writing ClothWrappingAsset...");
                    adaptedClothWrappingBytes = adaptedClothWrapping.Write();
                    ClothLogger.LogDebug($"Generated ClothWrapping: {adaptedClothWrappingBytes.Length} bytes");
                    
                    // Step 4: EACloth copied from template
                    eaClothBytes = _templateEAClothBytes;
                    ClothLogger.LogDebug($"EACloth: {eaClothBytes.Length} bytes (copied from template)");
                    
                    // Step 5: Write to Frosty project
                    task.Update("Saving to project...");
                    
                    if (targetClothWrappingEntry != null)
                    {
                        // Update ResMeta with correct content size (first 4 bytes = ByteSize)
                        byte[] cwMeta = new byte[16];
                        BitConverter.GetBytes((uint)adaptedClothWrappingBytes.Length).CopyTo(cwMeta, 0);
                        App.AssetManager.ModifyRes(targetClothWrappingEntry.ResRid, adaptedClothWrappingBytes, cwMeta);
                        ClothLogger.Log($"Replaced ClothWrapping: {targetClothWrappingEntry.Name}");
                    }
                    else
                    {
                        string resourceName = newResourceName + "_clothwrappingasset";
                        App.AssetManager.AddRes(resourceName, ResourceType.EAClothAssetData, null, adaptedClothWrappingBytes, meshBundles);
                        ClothLogger.Log($"Created ClothWrapping: {resourceName}");
                    }
                    
                    if (targetEAClothEntry != null)
                    {
                        // Update ResMeta with correct content size (first 4 bytes = ByteSize)
                        byte[] eaMeta = new byte[16];
                        BitConverter.GetBytes((uint)eaClothBytes.Length).CopyTo(eaMeta, 0);
                        App.AssetManager.ModifyRes(targetEAClothEntry.ResRid, eaClothBytes, eaMeta);
                        ClothLogger.Log($"Replaced EACloth: {targetEAClothEntry.Name}");
                    }
                    else
                    {
                        string resourceName = newResourceName + "_eacloth";
                        App.AssetManager.AddRes(resourceName, ResourceType.EAClothData, null, eaClothBytes, meshBundles);
                        ClothLogger.Log($"Created EACloth: {resourceName}");
                    }

                    task.Update("Complete!");
                });

                StatusText.Text = "Complete! Save your project.";
                FrostyMessageBox.Show(
                    "Cloth data generated!\n\n" +
                    $"ClothWrapping: {adaptedClothWrappingBytes?.Length ?? 0} bytes\n" +
                    $"EACloth: {eaClothBytes?.Length ?? 0} bytes\n\n" +
                    "Save your project to apply changes.",
                    "Complete", MessageBoxButton.OK);
                
                // Close the generator window after successful generation
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                FrostyMessageBox.Show($"Error: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK);
                StatusText.Text = "Error during generation";
                ClothLogger.LogError($"Generation error: {ex}");
            }
        }

        private void PrecisionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PrecisionValueText != null)
                PrecisionValueText.Text = ((int)e.NewValue).ToString();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AssetPathText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}
