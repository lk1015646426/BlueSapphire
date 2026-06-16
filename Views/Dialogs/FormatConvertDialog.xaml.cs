using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using BlueSapphire.Models;
using BlueSapphire.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Views.Dialogs
{
    public sealed partial class FormatConvertDialog : ContentDialog
    {
        public FormatConvertOptions Options { get; } = new FormatConvertOptions();
        private readonly IReadOnlyList<string> _sourceFiles;
        private readonly ImageProcessingService _imageProcessingService = new ImageProcessingService();
        private CancellationTokenSource? _estimationCts;
        private bool _isUpdatingValue;

        public FormatConvertDialog(IReadOnlyList<string> sourceFiles)
        {
            this.InitializeComponent();
            _sourceFiles = sourceFiles;
            
            PrimaryButtonClick += (s, e) => 
            {
                var selectedTag = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Jpeg";
                Options.TargetFormat = selectedTag switch
                {
                    "Png" => ImageConversionTarget.Png,
                    "Bmp" => ImageConversionTarget.Bmp,
                    "Gif" => ImageConversionTarget.Gif,
                    "Tiff" => ImageConversionTarget.Tiff,
                    _ => ImageConversionTarget.Jpeg
                };
                Options.Quality = QualitySlider.Value / 100.0;
            };
            
            this.Loaded += (s, e) => ScheduleEstimation();
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTag = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Jpeg";
            if (QualityPanel != null)
            {
                QualityPanel.Visibility = selectedTag == "Jpeg" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            }
            ScheduleEstimation();
        }

        private void QualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingValue) return;
            
            if (QualityText != null)
            {
                QualityText.Text = $"压缩质量: {e.NewValue:F0}%";
            }
            if (QualityNumberBox != null && QualityNumberBox.Value != e.NewValue)
            {
                _isUpdatingValue = true;
                QualityNumberBox.Value = e.NewValue;
                _isUpdatingValue = false;
            }
            
            ScheduleEstimation();
        }

        private void QualityNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isUpdatingValue || double.IsNaN(args.NewValue)) return;

            double val = Math.Clamp(args.NewValue, 1, 100);
            
            if (QualityText != null)
            {
                QualityText.Text = $"压缩质量: {val:F0}%";
            }
            if (QualitySlider != null && QualitySlider.Value != val)
            {
                _isUpdatingValue = true;
                QualitySlider.Value = val;
                _isUpdatingValue = false;
            }
            
            ScheduleEstimation();
        }

        private void ScheduleEstimation()
        {
            if (_sourceFiles == null || _sourceFiles.Count == 0 || EstimatedSizeText == null) return;

            _estimationCts?.Cancel();
            _estimationCts?.Dispose();
            _estimationCts = new CancellationTokenSource();
            
            var token = _estimationCts.Token;
            EstimatedSizeText.Text = "正在预估大小...";
            
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token); // Debounce
                    
                    var targetFormat = ImageConversionTarget.Jpeg;
                    DispatcherQueue.TryEnqueue(() => 
                    {
                        var selectedTag = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Jpeg";
                        targetFormat = selectedTag switch
                        {
                            "Png" => ImageConversionTarget.Png,
                            "Bmp" => ImageConversionTarget.Bmp,
                            "Gif" => ImageConversionTarget.Gif,
                            "Tiff" => ImageConversionTarget.Tiff,
                            _ => ImageConversionTarget.Jpeg
                        };
                    });
                    
                    // Wait for UI thread to fetch format
                    await Task.Delay(10, token);
                    
                    double quality = 0.92;
                    DispatcherQueue.TryEnqueue(() => { quality = QualitySlider.Value / 100.0; });
                    await Task.Delay(10, token);

                    var options = new FormatConvertOptions
                    {
                        TargetFormat = targetFormat,
                        Quality = quality
                    };

                    long estimatedSize = await _imageProcessingService.EstimateSizeAsync(_sourceFiles[0], options, token);
                    
                    if (!token.IsCancellationRequested)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (estimatedSize > 0)
                            {
                                string sizeStr = FormatBytes((ulong)estimatedSize);
                                EstimatedSizeText.Text = _sourceFiles.Count > 1 
                                    ? $"预估大小 (以第一张为准): {sizeStr}" 
                                    : $"预估大小: {sizeStr}";
                            }
                            else
                            {
                                EstimatedSizeText.Text = "无法预估大小";
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }
            }, token);
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }
}
