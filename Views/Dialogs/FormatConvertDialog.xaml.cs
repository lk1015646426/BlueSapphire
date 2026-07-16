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
        private readonly object _estimationSync = new();
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
            this.Closed += (s, e) =>
            {
                lock (_estimationSync)
                {
                    _estimationCts?.Cancel();
                    _estimationCts = null;
                }
            };
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

            CancellationTokenSource cts = new();
            lock (_estimationSync)
            {
                _estimationCts?.Cancel();
                _estimationCts = cts;
            }

            EstimatedSizeText.Text = "正在预估大小...";
            
            var selectedTag = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Jpeg";
            var targetFormat = selectedTag switch
            {
                "Png" => ImageConversionTarget.Png,
                "Bmp" => ImageConversionTarget.Bmp,
                "Gif" => ImageConversionTarget.Gif,
                "Tiff" => ImageConversionTarget.Tiff,
                _ => ImageConversionTarget.Jpeg
            };
            double quality = (QualitySlider?.Value ?? 92.0) / 100.0;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, cts.Token); // Debounce
                    
                    var options = new FormatConvertOptions
                    {
                        TargetFormat = targetFormat,
                        Quality = quality
                    };

                    long estimatedSize = await _imageProcessingService.EstimateSizeAsync(_sourceFiles[0], options, cts.Token);
                    
                    if (!cts.IsCancellationRequested)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (cts.IsCancellationRequested) return;
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
                    // 新选项或关闭对话框会取消旧预估。
                }
                catch (Exception)
                {
                    if (!cts.IsCancellationRequested)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!cts.IsCancellationRequested)
                            {
                                EstimatedSizeText.Text = "无法预估大小";
                            }
                        });
                    }
                }
                finally
                {
                    lock (_estimationSync)
                    {
                        if (ReferenceEquals(_estimationCts, cts))
                        {
                            _estimationCts = null;
                        }
                    }
                    cts.Dispose();
                }
            });
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
