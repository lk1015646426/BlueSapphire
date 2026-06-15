using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using BlueSapphire.Models;

namespace BlueSapphire.Views.Dialogs
{
    public sealed partial class EnhanceImageDialog : ContentDialog
    {
        private readonly string? _previewImagePath;
        public EnhanceOptions Options { get; } = new EnhanceOptions();

        public EnhanceImageDialog(string? previewImagePath)
        {
            this.InitializeComponent();
            _previewImagePath = previewImagePath;
            PrimaryButtonClick += (s, e) => 
            {
                Options.Brightness = BrightnessSlider.Value;
                Options.Contrast = ContrastSlider.Value;
                Options.Saturation = SaturationSlider.Value;
                Options.Sharpness = SharpnessSlider.Value;
            };
        }

        private void ContentDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_previewImagePath))
            {
                PreviewBorder.Visibility = Visibility.Visible;
                PreviewImage.Source = new BitmapImage(new Uri(_previewImagePath));
            }
        }

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (BrightnessText != null) BrightnessText.Text = $"亮度: {BrightnessSlider.Value:F2}";
            if (ContrastText != null) ContrastText.Text = $"对比度: {ContrastSlider.Value:F2}";
            if (SaturationText != null) SaturationText.Text = $"饱和度: {SaturationSlider.Value:F2}";
            if (SharpnessText != null) SharpnessText.Text = $"锐化: {SharpnessSlider.Value:F2}";
        }
    }
}
