using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.DependencyInjection; // ✅ 新增：引入依赖注入扩展

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, IMediaViewInteraction
    {
        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            this.InitializeComponent();

            // ✅ 1. 从全局容器中获取 ViewModel 实例，替代原来的 new
            ViewModel = App.Current.Services.GetRequiredService<MediaManagerViewModel>();

            // ✅ 2. 调用 Initialize 方法，注入当前页面接口实例与 DispatcherQueue
            ViewModel.Initialize(this, this.DispatcherQueue);
        }

        // --- 接口实现 ---

        // 1. 文件夹选择 (修复了返回值 ? 和句柄问题)
        public async Task<StorageFolder?> PickFolderAsync()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            folderPicker.FileTypeFilter.Add("*");

            // 修复：使用 App.MainWindowHandle
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, App.MainWindowHandle);

            return await folderPicker.PickSingleFolderAsync();
        }

        // 2. 显示重命名预览 (调用你新建的 XAML Dialog)
        public async Task<bool> ShowRenamePreviewAsync(List<RenamePreviewItem> items, int skippedCount)
        {
            var dialog = new RenamePreviewDialog(items, skippedCount)
            {
                XamlRoot = this.XamlRoot // 必须设置
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        // 3. 显示重复结果 (调用你新建的 XAML Dialog)
        public async Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> duplicates)
        {
            var dialog = new DuplicateResultDialog(duplicates, this.DispatcherQueue)
            {
                XamlRoot = this.XamlRoot // 必须设置
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                return dialog.GetSelectedFiles();
            }
            return new List<StorageFile>(); // 返回空列表表示取消
        }

        // 4. 显示简单提示
        public async Task ShowTipAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        // 5. 删除确认
        public async Task<bool> ShowDeleteConfirmationAsync(int count)
        {
            var dialog = new ContentDialog
            {
                Title = "删除确认",
                Content = $"确定要将这 {count} 个文件移至回收站吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        // GridView 事件处理 (保持 UI 响应)
        // MediaManagerPage.xaml.cs

        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            // ✅ 当网格滑出屏幕被回收时，立刻掐断后台的 I/O 加载！
            if (args.InRecycleQueue)
            {
                // 把前面的 // 删掉了，现在它是真正起作用的代码了！
                (args.Item as ImageItem)?.CancelLoad();
                return;
            }

            if (args.Item is ImageItem item)
            {
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        private async void ImageGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is ImageItem item)
            {
                // 打开图片查看
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                    await Windows.System.Launcher.LaunchFileAsync(file);
                }
                catch { }
            }
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            // 转发给 ViewModel 的命令
            ViewModel.DeleteSelectedCommand.Execute(ImageGrid.SelectedItems);
        }
    }
}