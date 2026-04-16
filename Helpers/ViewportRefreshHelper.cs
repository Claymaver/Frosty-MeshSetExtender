using System;
using System.Reflection;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Viewport;
using FrostySdk.IO;
using MeshSetPlugin.Resources;

namespace MeshSetExtender.Helpers
{
    /// <summary>
    /// Refreshes the FrostyMeshSetEditor viewport and UI after a mutation, mirroring the
    /// exact sequence the vanilla Import button uses. This avoids the UI thread hang that
    /// our previous broad reflection-based refresh was causing.
    ///
    /// Vanilla ImportButton_Click (FrostyMeshSetEditor.cs ~L4435):
    ///   viewport.SetPaused(true)
    ///   FBXImporter.ImportFBX(...)
    ///   screen.ClearMeshes(clearAll: true)
    ///   screen.AddMesh(meshSet, GetVariation(selectedPreviewIndex), Matrix.Identity, LoadPose(...))
    ///   UpdateMeshSettings(); UpdateControls(); InvokeOnAssetModified()
    ///   viewport.SetPaused(false)
    /// </summary>
    internal static class MeshEditorRefreshHelper
    {
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags InstanceAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Toggles viewport pause on the given editor (no-ops if unavailable).</summary>
        public static void SetPaused(FrostyAssetEditor editor, bool paused)
        {
            try
            {
                FrostyViewport viewport = GetViewport(editor);
                viewport?.SetPaused(paused);
            }
            catch (Exception ex)
            {
                App.Logger.Log($"[MeshSet Extender] viewport pause failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs the vanilla post-import refresh sequence on the given editor instance.
        /// Must be called on the UI thread. Caller owns viewport.SetPaused(true/false).
        /// </summary>
        public static void RefreshAfterImport(FrostyAssetEditor editor, MeshSet meshSet, EbxAsset asset)
        {
            if (editor == null) return;

            // All UI-touching work must happen on the dispatcher thread.
            editor.Dispatcher.Invoke(() =>
            {
                try
                {
                    Type editorType = editor.GetType();

                    object screen = GetField(editor, editorType, "screen");
                    if (screen == null)
                    {
                        App.Logger.Log("[MeshSet Extender] Could not access editor.screen — skipping viewport refresh.");
                        return;
                    }

                    int selectedPreviewIndex = 0;
                    object previewIdxObj = GetField(editor, editorType, "selectedPreviewIndex");
                    if (previewIdxObj is int idx) selectedPreviewIndex = idx;

                    // variation = GetVariation(selectedPreviewIndex)
                    object variation = InvokeMethod(editor, editorType, "GetVariation",
                        new[] { typeof(int) }, new object[] { selectedPreviewIndex });

                    // pose = LoadPose(AssetEntry.Filename, asset)
                    string filename = editor.AssetEntry?.Filename ?? string.Empty;
                    object pose = InvokeMethod(editor, editorType, "LoadPose",
                        new[] { typeof(string), typeof(EbxAsset) }, new object[] { filename, asset });

                    // screen.ClearMeshes(clearAll: true)
                    Type screenType = screen.GetType();
                    MethodInfo clearMeshes = screenType.GetMethod("ClearMeshes", InstanceAny);
                    clearMeshes?.Invoke(screen, new object[] { true });

                    // screen.AddMesh(meshSet, variation, Matrix.Identity, pose)
                    MethodInfo addMesh = screenType.GetMethod("AddMesh", InstanceAny);
                    addMesh?.Invoke(screen, new object[] { meshSet, variation, SharpDX.Matrix.Identity, pose });

                    InvokeMethod(editor, editorType, "UpdateMeshSettings", Type.EmptyTypes, new object[0]);
                    InvokeMethod(editor, editorType, "UpdateControls", Type.EmptyTypes, new object[0]);
                    InvokeMethod(editor, editorType, "InvokeOnAssetModified", Type.EmptyTypes, new object[0]);
                }
                catch (Exception ex)
                {
                    App.Logger.Log($"[MeshSet Extender] viewport refresh failed: {ex.Message}");
                }
            });
        }

        private static FrostyViewport GetViewport(FrostyAssetEditor editor)
        {
            if (editor == null) return null;
            return GetField(editor, editor.GetType(), "viewport") as FrostyViewport;
        }

        private static object GetField(object instance, Type t, string name)
        {
            while (t != null)
            {
                FieldInfo f = t.GetField(name, InstanceAny);
                if (f != null) return f.GetValue(instance);
                t = t.BaseType;
            }
            return null;
        }

        private static object InvokeMethod(object instance, Type t, string name, Type[] sig, object[] args)
        {
            while (t != null)
            {
                MethodInfo m = sig.Length == 0
                    ? t.GetMethod(name, InstanceAny, null, Type.EmptyTypes, null)
                    : t.GetMethod(name, InstanceAny, null, sig, null);
                if (m != null) return m.Invoke(instance, args);
                t = t.BaseType;
            }
            return null;
        }
    }
}
