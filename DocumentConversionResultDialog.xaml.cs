using BlueSapphire.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using Windows.Storage;

namespace BlueSapphire
{
    public sealed partial class DocumentConversionResultDialog : ContentDialog
    {
        public DocumentConversionBatchReport Report { get; }

        public DocumentConversionResultDialog(DocumentConversionBatchReport report)
        {
            this.InitializeComponent();
            Report = report;
            Title = report.DialogTitle;
            OperationTextBlock.Text = report.OperationName;
            CreatedAtTextBlock.Text = report.CreatedAtText;
            SummaryTextBlock.Text = $"本次共处理 {report.TotalCount} 个文件，{report.SummaryText}。";
            ResultList.ItemsSource = report.Items;
        }

        private async void ResultList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is not DocumentConversionBatchItem item)
            {
                return;
            }

            string? launchPath = item.CanOpenResult ? item.OutputPath : item.SourcePath;
            if (string.IsNullOrWhiteSpace(launchPath))
            {
                return;
            }

            try
            {
                if (item.ResultTargetKind == DocumentOperationResultTargetKind.Folder)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{launchPath}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                var file = await StorageFile.GetFileFromPathAsync(launchPath);
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
            catch
            {
            }
        }
    }
}
