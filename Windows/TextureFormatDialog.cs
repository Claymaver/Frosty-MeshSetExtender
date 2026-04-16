using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Frosty.Controls;

namespace MeshSetExtender.Windows
{
    /// <summary>
    /// Simple dialog for selecting texture export format (PNG, DDS, TGA).
    /// Extends FrostyDockableWindow for consistent dark theme styling.
    /// </summary>
    public class TextureFormatDialog : FrostyDockableWindow
    {
        private readonly ComboBox _formatCombo;

        /// <summary>Filter string for TextureExporter.Export (e.g. "*.png").</summary>
        public string SelectedFormat { get; private set; } = "*.png";

        /// <summary>File extension including dot (e.g. ".png").</summary>
        public string SelectedExtension { get; private set; } = ".png";

        public TextureFormatDialog()
        {
            Title = "Texture Export Format";
            Width = 300;
            Height = 160;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;

            var fontBrush = TryFindResource("FontColor") as Brush ?? Brushes.White;
            var controlBackground = TryFindResource("ControlBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45));

            var panel = new StackPanel { Margin = new Thickness(16) };

            panel.Children.Add(new TextBlock
            {
                Text = "Select export format:",
                Foreground = fontBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _formatCombo = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 16),
                Background = controlBackground,
                Foreground = fontBrush
            };
            _formatCombo.Items.Add("PNG (*.png)");
            _formatCombo.Items.Add("DDS (*.dds)");
            _formatCombo.Items.Add("TGA (*.tga)");
            _formatCombo.SelectedIndex = 0;
            panel.Children.Add(_formatCombo);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                ApplySelection();
                DialogResult = true;
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                IsCancel = true
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(buttonPanel);

            Content = panel;
        }

        private void ApplySelection()
        {
            switch (_formatCombo.SelectedIndex)
            {
                case 0:
                    SelectedFormat = "*.png";
                    SelectedExtension = ".png";
                    break;
                case 1:
                    SelectedFormat = "*.dds";
                    SelectedExtension = ".dds";
                    break;
                case 2:
                    SelectedFormat = "*.tga";
                    SelectedExtension = ".tga";
                    break;
            }
        }
    }
}
