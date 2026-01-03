using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using System; // 必须引用，解决 Task 和 GetAwaiter 问题
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // 必须引用
using Windows.Storage; // 必须引用，解决 StorageFolder 问题
using Windows.Foundation; // 必须引用，解决 IAsyncOperation await 问题

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, ITool, IMediaViewInteraction
    {
        // --- ITool 接口实现 (之前报错是因为缺少了这些) ---
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        public void Initialize() { }

        // 公开 ViewModel 供 x:Bind 使用
        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            this.InitializeComponent();
            ViewModel = new MediaManagerViewModel(this, this.DispatcherQueue);
        }

        // --- 事件处理 ---
        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is ImageItem item && !args.InRecycleQueue)
            {
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        private void ImageGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ImageGrid.SelectedItem is ImageItem item)
            {
                _ = ShowTipAsync($"文件: {item.FileName}\n路径: {item.ImagePath}\n大小: {item.FileSizeString}");
            }
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            var items = ImageGrid.SelectedItems;
            ViewModel.DeleteSelectedCommand.Execute(items);
        }

        // --- IMediaViewInteraction 接口实现 ---

        public async Task ShowTipAsync(string message)
        {
            try
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
            catch { }
        }

        // [新增] 实现重命名预览弹窗
        public async Task<bool> ShowRenamePreviewAsync(List<RenamePreviewItem> items, int skippedCount)
        {
            // 1. 创建列表视图
            var listView = new ListView
            {
                ItemsSource = items,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 400,
                ItemTemplate = (DataTemplate)this.Resources["RenamePreviewTemplate"]
            };

            // 2. 构建提示信息
            var stackPanel = new StackPanel { Spacing = 10 };

            // 头部提示
            stackPanel.Children.Add(new TextBlock
            {
                Text = "请确认以下更改：",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            // 列表
            stackPanel.Children.Add(listView);

            // 底部警告 (如果有跳过的文件)
            if (skippedCount > 0)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = $"⚠ 注意：有 {skippedCount} 个文件因缺失拍摄日期信息将被跳过。",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0)), // Orange
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            // 格式说明
            stackPanel.Children.Add(new TextBlock
            {
                Text = "命名格式：yyyyMMdd_HHmmss (如遇冲突自动添加序号)",
                Opacity = 0.6,
                FontSize = 12
            });

            var dialog = new ContentDialog
            {
                Title = "批量重命名预览",
                Content = stackPanel,
                PrimaryButtonText = "确认修改",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<bool> ShowDeleteConfirmationAsync(int count)
        {
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除选中的 {count} 个文件吗？\n此操作不可撤销。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<StorageFolder?> PickFolderAsync()
        {
            var openPicker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            openPicker.FileTypeFilter.Add("*");

            // [优化] 使用 App.CurrentWindow 替代 MainWindow.Instance
            if (App.CurrentWindow != null)
            {
                // 获取窗口句柄
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);
            }

            // 需要 using System; 和 using Windows.Foundation; 才能 await 这个调用
            return await openPicker.PickSingleFolderAsync();
        }

        // [重构] 使用 DataTemplate 和 ListView 替代 XamlReader
        public async Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> dupes)
        {
            // 1. 准备扁平化数据源
            var flatList = new System.Collections.ObjectModel.ObservableCollection<DuplicateItem>();
            foreach (var g in dupes)
            {
                flatList.Add(DuplicateItem.CreateSeparator());
                // 按日期降序排列（最新的在最前）
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    // 逻辑：每组第一个（最新的）标记为“推荐保留”
                    flatList.Add(new DuplicateItem(sorted[i], i == 0));
                }
            }
            // 移除首个多余的分隔符
            if (flatList.Any()) flatList.RemoveAt(0);

            // 2. 编程式创建 ListView
            var listView = new ListView
            {
                ItemsSource = flatList,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 500, // 限制高度，防止弹窗溢出屏幕
                Width = 450,
                // [关键] 引用 XAML 中定义的 DataTemplate
                ItemTemplate = (DataTemplate)this.Resources["DuplicateItemTemplate"]
            };

            // 3. 注册事件：懒加载缩略图
            listView.ContainerContentChanging += (s, args) =>
            {
                if (args.Item is DuplicateItem item && !item.IsSeparator && !args.InRecycleQueue)
                {
                    // 调用 Model 中的异步加载方法
                    _ = item.LoadThumbnailAsync(this.DispatcherQueue);
                }
            };

            // 4. 注册事件：双击预览大图
            listView.DoubleTapped += async (s, e) =>
            {
                // 获取被点击的数据项
                if ((e.OriginalSource as FrameworkElement)?.DataContext is DuplicateItem item
                    && item.File != null)
                {
                    try
                    {
                        // [交互优化] 调用系统默认查看器打开文件
                        // 这是查看原始画质最准确、最兼容的方式
                        await Windows.System.Launcher.LaunchFileAsync(item.File);
                    }
                    catch { }
                }
            };

            // 5. 显示对话框
            var dialog = new ContentDialog
            {
                Title = $"发现 {dupes.Count} 组重复文件",
                Content = listView,
                PrimaryButtonText = "删除选中项",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                // 返回所有被勾选的文件（即待删除的文件）
                return flatList.Where(x => x.File != null && x.IsChecked).Select(x => x.File!).ToList();
            }
            return new List<StorageFile>();
        }
    }
}

