using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshSetExtender.Resources
{
    /// <summary>
    /// Icons for cloth-related assets and tools.
    /// Loads ClothIcon.png from the embedded Images folder.
    /// </summary>
    public static class ClothIcons
    {
        private static ImageSource _clothIcon;

        /// <summary>
        /// Shared cloth icon used for menu items, context menus, and asset types.
        /// Loaded from Images/ClothIcon.png embedded resource.
        /// </summary>
        public static ImageSource ClothIcon
        {
            get
            {
                if (_clothIcon == null)
                {
                    _clothIcon = new BitmapImage(new Uri(
                        "pack://application:,,,/MeshSetExtender;component/Images/ClothIcon.png",
                        UriKind.Absolute));
                }
                return _clothIcon;
            }
        }
    }
}
