using System;
using System.Runtime.InteropServices;

namespace MeshSetExtender.Windows
{
    /// <summary>
    /// Modern Vista-style folder picker using IFileOpenDialog COM interop.
    /// Replaces the ancient WinForms FolderBrowserDialog.
    /// </summary>
    public class VistaFolderBrowserDialog
    {
        public string SelectedPath { get; private set; }
        public string Title { get; set; } = "Select Folder";

        public bool ShowDialog()
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();

            try
            {
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_NOCHANGEDIR);
                dialog.SetTitle(Title);

                uint hr = dialog.Show(GetActiveWindow());
                if (hr != 0) // user cancelled or error
                    return false;

                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
                SelectedPath = path;
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        // COM interop for IFileOpenDialog
        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] uint Show(IntPtr hwndOwner);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(FOS fos);
            void GetOptions();
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem ppsi);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
            void GetResults();
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes();
            void Compare();
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_NOCHANGEDIR = 0x00000008,
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }
    }
}
