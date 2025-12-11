using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI;

namespace BlueSapphire
{
    // ==========================================
    // 1. 数据模型类: ImageItem (性能优化版)
    // ==========================================
    public class ImageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string? FileName { get; set; }
        public string? ImagePath { get; set; }
        public DateTimeOffset DateCreated { get; set; }
        public ulong FileSize { get; set; }

        public string DateCreatedString => DateCreated.ToString("yyyy-MM-dd");
        public string FileSizeString => FormatBytes(FileSize);

        private static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private BitmapImage? _imageSource;
        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set
            {
                if (_imageSource != value)
                {
                    _imageSource = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isLoaded = false;

        // 【优化点 1】使用系统缩略图代替读取原图，极大降低内存占用并提升速度
        public async Task LoadImageAsync(DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath)) return;
            _isLoaded = true;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(this.ImagePath);

                // 请求 200px 的缩略图，系统会自动处理缓存和缩放
                using (var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 200))
                {
                    if (thumb != null)
                    {
                        // 将缩略图流复制到内存流中 (因为 thumb 在 using 结束时会关闭)
                        // 缩略图非常小，这里复制几乎不耗内存
                        var memoryStream = new InMemoryRandomAccessStream();
                        await RandomAccessStream.CopyAsync(thumb, memoryStream);
                        memoryStream.Seek(0);

                        dispatcherQueue.TryEnqueue(() =>
                        {
                            var bitmap = new BitmapImage();
                            bitmap.SetSource(memoryStream); // 使用 SetSource 而不是 SetSourceAsync 以便立即绑定
                            bitmap.DecodePixelWidth = 200;  // 双重保险
                            this.ImageSource = bitmap;
                        });
                    }
                }
            }
            catch
            {
                _isLoaded = false; // 加载失败允许重试
            }
        }
    }

    // ==========================================
    // 2. 页面逻辑类: MediaManagerPage (缓存与并发优化版)
    // ==========================================
    public sealed partial class MediaManagerPage : Page, ITool, INotifyPropertyChanged
    {
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // 绑定到 UI 的增量加载集合
        private IncrementalLoadingCollection<ImageItem>? _images;
        public IncrementalLoadingCollection<ImageItem>? Images
        {
            get => _images;
            set
            {
                if (_images != value)
                {
                    _images = value;
                    OnPropertyChanged();
                }
            }
        }

        // 【优化点 2】内存缓存：保存当前文件夹的所有文件元数据，避免排序时重新扫描硬盘
        private List<ImageItem> _cachedAllItems = new List<ImageItem>();

        private StorageFolder? _currentFolder;
        private bool _isDialogActive = false;
        private string CurrentSortTag { get; set; } = "Name";

        public MediaManagerPage()
        {
            this.InitializeComponent();
            UpdateSortDirectionIcon();
        }

        public void Initialize() { }

        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is ImageItem item)
            {
                // 仅当 Item 进入视口时触发加载
                if (args.InRecycleQueue) return;
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        // --- UI 状态控制 ---

        private void SetBusyState(string statusText)
        {
            ScanningProgressBar.Visibility = Visibility.Collapsed;
            LoadingStatusText.Text = statusText;
            StatusOverlay.Visibility = Visibility.Visible;
        }

        private void StartProgressState(string statusText, int totalItems)
        {
            ScanningProgressBar.Visibility = Visibility.Visible;
            ScanningProgressBar.IsIndeterminate = false;
            ScanningProgressBar.Minimum = 0;
            ScanningProgressBar.Maximum = totalItems;
            ScanningProgressBar.Value = 0;

            LoadingStatusText.Text = statusText;
            StatusOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateProgressValue(int currentValue)
        {
            ScanningProgressBar.Value = currentValue;
        }

        private void RestoreUI(bool hasContent = true)
        {
            ScanningProgressBar.Visibility = Visibility.Collapsed;
            StatusOverlay.Visibility = Visibility.Collapsed;
            StatusBlock.Text = "STATUS: READY";
            if (hasContent)
            {
                ImageGrid.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ImageGrid.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }

        private async void ShowTipDialog(string message)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "提示",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        private void HandleError(string message)
        {
            RestoreUI(Images?.Any() == true);
            _isDialogActive = false;
            ShowTipDialog(message);
        }

        // --- 排序逻辑 (优化版) ---

        private void UpdateSortDirectionIcon()
        {
            bool isDescending = SortDirectionToggle.IsChecked ?? false;
            if (!isDescending)
            {
                SortDirectionIcon.Glyph = "\uE74B"; // 升序图标
                ToolTipService.SetToolTip(SortDirectionToggle, "当前: 升序");
            }
            else
            {
                SortDirectionIcon.Glyph = "\uE74A"; // 降序图标
                ToolTipService.SetToolTip(SortDirectionToggle, "当前: 降序");
            }
        }

        private void OnSortFieldMenuItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string tag)
            {
                CurrentSortTag = tag;
                SortFieldButton.Content = item.Text;
                // 【优化】不重新加载文件夹，直接使用内存缓存重排
                RefreshViewFromCache();
            }
        }

        private void OnSortDirectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateSortDirectionIcon();
            // 【优化】不重新加载文件夹，直接使用内存缓存重排
            RefreshViewFromCache();
        }

        // --- 核心：从缓存刷新视图 (新增) ---
        private void RefreshViewFromCache()
        {
            if (_cachedAllItems == null || _cachedAllItems.Count == 0) return;

            SetBusyState("正在排序...");

            bool isDescending = SortDirectionToggle.IsChecked ?? false;
            IEnumerable<ImageItem> sortedEnumerable;

            switch (CurrentSortTag)
            {
                case "Date":
                    sortedEnumerable = isDescending
                        ? _cachedAllItems.OrderByDescending(x => x.DateCreated)
                        : _cachedAllItems.OrderBy(x => x.DateCreated);
                    break;
                case "Size":
                    sortedEnumerable = isDescending
                        ? _cachedAllItems.OrderByDescending(x => x.FileSize)
                        : _cachedAllItems.OrderBy(x => x.FileSize);
                    break;
                case "Name":
                default:
                    sortedEnumerable = isDescending
                        ? _cachedAllItems.OrderByDescending(x => x.FileName)
                        : _cachedAllItems.OrderBy(x => x.FileName);
                    break;
            }

            var sortedList = sortedEnumerable.ToList();

            // 重建 IncrementalLoadingCollection
            Images = new IncrementalLoadingCollection<ImageItem>(async (token, count) =>
            {
                return await Task.Run(() =>
                {
                    var currentLen = Images?.Count ?? 0;
                    return sortedList.Skip(currentLen).Take((int)count);
                });
            });

            RestoreUI(true);
        }

        // --- 打开文件夹 ---

        public async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                var openPicker = new FolderPicker();
                openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                openPicker.FileTypeFilter.Add("*");

                if (MainWindow.Instance != null)
                {
                    WinRT.Interop.InitializeWithWindow.Initialize(openPicker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));
                }

                StorageFolder folder = await openPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    _currentFolder = folder;
                    await LoadFolderContent(folder);
                }
            }
            catch (Exception ex)
            {
                HandleError(ex.Message);
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        // --- 加载逻辑 (优化版) ---

        private async Task LoadFolderContent(StorageFolder folder)
        {
            if (folder == null) return;
            try
            {
                SetBusyState("正在索引元数据...");
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                ImageGrid.Visibility = Visibility.Collapsed;
                Images = null;
                _cachedAllItems.Clear(); // 清空缓存
                PathBlock.Text = $"PATH: {folder.Path}";

                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp" })
                {
                    FolderDepth = FolderDepth.Deep
                };
                var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (files.Count == 0)
                {
                    CountBlock.Text = "ITEMS: 0";
                    RestoreUI(false);
                    return;
                }

                CountBlock.Text = $"ITEMS: {files.Count}";
                LoadingStatusText.Text = $"正在处理 {files.Count} 个文件...";

                // 构建缓存列表
                // 注意：这里我们尽量只读取 BasicProperties，避免昂贵的操作
                foreach (var file in files)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    _cachedAllItems.Add(new ImageItem
                    {
                        FileName = file.Name,
                        ImagePath = file.Path,
                        DateCreated = file.DateCreated,
                        FileSize = props.Size
                    });
                }

                // 首次填充完毕，调用统一的刷新方法进行排序和显示
                RefreshViewFromCache();
            }
            catch (Exception ex)
            {
                HandleError($"加载失败: {ex.Message}");
            }
        }

        // --- 删除逻辑 ---

        private async void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;

            var selectedItems = ImageGrid.SelectedItems.Cast<ImageItem>().ToList();
            if (selectedItems.Count == 0)
            {
                _isDialogActive = false;
                ShowTipDialog("请先选择图片");
                return;
            }

            try
            {
                ContentDialog deleteDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要彻底删除这 {selectedItems.Count} 个文件吗？此操作不可恢复。",
                    PrimaryButtonText = "执行删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                if (await deleteDialog.ShowAsync() != ContentDialogResult.Primary) return;
                StartProgressState($"正在删除... (0/{selectedItems.Count})", selectedItems.Count);

                int deletedCount = 0;
                int processedCount = 0;
                foreach (var item in selectedItems)
                {
                    processedCount++;
                    UpdateProgressValue(processedCount);
                    LoadingStatusText.Text = $"正在删除... ({processedCount}/{selectedItems.Count})";

                    try
                    {
                        if (item.ImagePath != null)
                        {
                            var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                            await file.DeleteAsync(StorageDeleteOption.Default);

                            // 【同步更新】同时从 UI 集合和缓存中移除
                            Images?.Remove(item);
                            _cachedAllItems.Remove(item);
                            deletedCount++;
                        }
                    }
                    catch { }
                }

                if (Images != null) CountBlock.Text = $"ITEMS: {_cachedAllItems.Count}";
                RestoreUI(true);

                _isDialogActive = false;
                ShowTipDialog($"操作完成。成功删除 {deletedCount} 个文件。");
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        // --- 去重逻辑 (并发优化版) ---

        private async void OnScanDuplicatesClicked(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) { ShowTipDialog("请先导入文件夹"); return; }
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                SetBusyState("正在初筛 (按大小)...");
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp" });
                queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new[] { "System.Size" });
                var files = await _currentFolder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (files.Count < 2)
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("文件太少，无需扫描");
                    return;
                }

                // 1. 初筛：按文件大小分组
                var fileInfos = new List<(StorageFile f, ulong s)>();
                foreach (var f in files)
                {
                    var p = await f.GetBasicPropertiesAsync();
                    fileInfos.Add((f, p.Size));
                }

                var groups = fileInfos.GroupBy(x => x.s).Where(g => g.Count() > 1).ToList();
                if (!groups.Any())
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("恭喜，当前文件夹没有发现重复文件！");
                    return;
                }

                SetBusyState($"正在进行深度校验 (MD5)... 共 {groups.Count} 组疑似重复");

                // 2. 复筛：【优化点 3】并发计算 MD5
                var dupes = new ConcurrentBag<List<StorageFile>>();

                await Task.Run(async () =>
                {
                    // 使用 Parallel.ForEachAsync 提升并发效率 (需要 .NET 6+)
                    await Parallel.ForEachAsync(groups, async (g, ct) =>
                    {
                        var hashes = new Dictionary<string, List<StorageFile>>();
                        foreach (var item in g)
                        {
                            var h = await FileHelper.ComputeMD5Async(item.f);
                            if (!string.IsNullOrEmpty(h))
                            {
                                if (!hashes.ContainsKey(h)) hashes[h] = new List<StorageFile>();
                                hashes[h].Add(item.f);
                            }
                        }

                        // 只有同一个哈希值对应多个文件才算重复
                        foreach (var hg in hashes.Values)
                        {
                            if (hg.Count > 1) dupes.Add(hg);
                        }
                    });
                });

                RestoreUI(true);
                _isDialogActive = false;

                var finalDupes = dupes.ToList();
                if (finalDupes.Any())
                {
                    await ShowDuplicateResultsDialog(finalDupes);
                }
                else
                {
                    ShowTipDialog("恭喜，当前文件夹没有发现重复文件！");
                }
            }
            catch (Exception ex)
            {
                HandleError(ex.Message);
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        private async Task ShowDuplicateResultsDialog(List<List<StorageFile>> dupes)
        {
            var sp = new StackPanel { Spacing = 10, Padding = new Thickness(0, 0, 10, 0) };
            var toDel = new List<StorageFile>();
            int idx = 1;

            foreach (var g in dupes)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"重复组 #{idx++} ({g.Count}个)",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 0, 255, 255))
                });
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var f = sorted[i];
                    bool keep = (i == 0);
                    var cb = new CheckBox
                    {
                        Content = $"{f.Name}",
                        IsChecked = !keep,
                        Tag = f,
                        Opacity = keep ? 0.5 : 1
                    };
                    if (keep)
                    {
                        cb.Content += " [建议保留]";
                        cb.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.LightGreen);
                    }

                    cb.Checked += (s, e) => toDel.Add(f);
                    cb.Unchecked += (s, e) => toDel.Remove(f);
                    if (!keep) toDel.Add(f);
                    sp.Children.Add(cb);
                }
            }

            var scroll = new ScrollViewer { Content = sp, MaxHeight = 400 };
            var dlg = new ContentDialog
            {
                Title = $"发现 {dupes.Count} 组重复文件",
                Content = scroll,
                PrimaryButtonText = "删除选中项",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary && toDel.Any())
            {
                await PerformDeleteLoop(toDel);
            }
        }

        private async Task PerformDeleteLoop(List<StorageFile> files)
        {
            StartProgressState($"正在清理... (0/{files.Count})", files.Count);
            int c = 0;
            int processed = 0;

            foreach (var f in files)
            {
                processed++;
                UpdateProgressValue(processed);
                LoadingStatusText.Text = $"正在清理... ({processed}/{files.Count})";

                try
                {
                    await f.DeleteAsync(StorageDeleteOption.Default);

                    // 【同步更新】查找并移除缓存中的项
                    var itemToRemove = _cachedAllItems.FirstOrDefault(x => x.ImagePath == f.Path);
                    if (itemToRemove != null)
                    {
                        _cachedAllItems.Remove(itemToRemove);
                        Images?.Remove(itemToRemove);
                    }
                    c++;
                }
                catch { }
            }

            if (Images != null) CountBlock.Text = $"ITEMS: {_cachedAllItems.Count}";
            RestoreUI(true);

            _isDialogActive = false;
            ShowTipDialog($"成功清理 {c} 个重复文件");
        }

        private void ImageGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ImageGrid.SelectedItem is ImageItem item)
            {
                ShowTipDialog($"已选择: {item.FileName}");
            }
        }
    }
}