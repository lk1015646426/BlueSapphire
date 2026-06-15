using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Views.Dialogs
{
    public sealed partial class FormatConvertDialog : ContentDialog
    {
        public FormatConvertOptions Options { get; } = new FormatConvertOptions();

        public FormatConvertDialog()
        {
            this.InitializeComponent();
            PrimaryButtonClick += (s, e) => 
            {
                var selectedTag = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Jpeg";
                Options.TargetFormat = selectedTag switch
                {
                    "Png" => ImageConversionTarget.Png,
                    "Bmp" => ImageConversionTarget.Bmp,
                    _ => ImageConversionTarget.Jpeg
                };
                Options.Quality = QualitySlider.Value / 100.0;
            };
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTag = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Jpeg";
            if (QualityPanel != null)
            {
                QualityPanel.Visibility = selectedTag == "Jpeg" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            }
        }

        private void QualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (QualityText != null)
            {
                QualityText.Text = $"压缩质量: {e.NewValue:F0}%";
            }
        }
    }
}
