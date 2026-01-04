using BlueSapphire.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace BlueSapphire
{
    public sealed partial class RenamePreviewDialog : ContentDialog
    {
        public RenamePreviewDialog(List<RenamePreviewItem> items, int skippedCount)
        {
            this.InitializeComponent();
            PreviewList.ItemsSource = items;

            if (skippedCount > 0)
            {
                WarningText.Text = $"⚠ 注意：有 {skippedCount} 个文件因缺失拍摄日期信息将被跳过。";
                WarningText.Visibility = Visibility.Visible;
            }
        }
    }
}