using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI;

// 注意：IValueConverter 转换器类已移至 Helpers/Converters.cs 文件

namespace BlueSapphire
{
    // ==========================================
    // 1. 数据模型类: ImageItem 
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
        private bool _isImageLoading = false;
        public bool IsImageLoading
        {
            get => _isImageLoading;
            set
            {
                if (_isImageLoading != value)
                {
                    _isImageLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public async Task LoadImageAsync(DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath)) return;
            _isLoaded = true;
            this.IsImageLoading = true;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(this.ImagePath);

                using (var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 200))
                {
                    if (thumb != null)
                    {
                        var memoryStream = new InMemoryRandomAccessStream();
                        await RandomAccessStream.CopyAsync(thumb, memoryStream);
                        memoryStream.Seek(0);

                        dispatcherQueue.TryEnqueue(() =>
                        {
                            var bitmap = new BitmapImage();
                            bitmap.SetSource(memoryStream);
                            bitmap.DecodePixelWidth = 200;
                            this.ImageSource = bitmap;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading thumbnail for {FileName}: {ex.Message}");
                _isLoaded = false;
            }
            finally
            {
                this.IsImageLoading = false;
            }
        }
    }

    // ==========================================
    // 2. 数据模型类: DuplicateItem (去重弹窗专用)
    // ==========================================
    public class DuplicateItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public StorageFile? File { get; }
        public bool IsGroupSeparator { get; }
        public string DisplayName => File?.Name ?? "重复组";
        public bool IsKeepSuggestion { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public SolidColorBrush ForegroundBrush =>
            IsKeepSuggestion ? new SolidColorBrush(Colors.LightGreen) : new SolidColorBrush(Colors.White);

        public double OpacityValue => IsGroupSeparator ? 0.7 : (IsKeepSuggestion ? 0.8 : 1.0);

        public DuplicateItem(StorageFile file, bool isKeepSuggestion)
        {
            File = file;
            IsKeepSuggestion = isKeepSuggestion;
            IsGroupSeparator = false;
            IsChecked = !isKeepSuggestion;
        }

        private DuplicateItem(bool isGroupSeparator)
        {
            File = null;
            IsKeepSuggestion = isGroupSeparator;
            IsGroupSeparator = isGroupSeparator;
            IsChecked = false;
        }

        public static DuplicateItem CreateSeparator()
        {
            return new DuplicateItem(true);
        }
    }


    // ==========================================
    // 3. 页面逻辑类: MediaManagerPage 
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
                if (args.InRecycleQueue) return;
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        // --- UI 状态控制 (保持不变) ---
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

        // --- 排序逻辑 (保持不变) ---
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
                RefreshViewFromCache();
            }
        }

        private void OnSortDirectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateSortDirectionIcon();
            RefreshViewFromCache();
        }

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

        // --- 打开文件夹 (保持不变) ---

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

        // --- 核心：加载逻辑 (I/O 鲁棒性修复) ---

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

                // 扩展文件类型列表，加入常见视频格式
                var fileExtensions = new List<string> {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp",
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp" // 视频扩展名
                };

                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions)
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

                int failedCount = 0;

                // 构建缓存列表
                foreach (var file in files)
                {
                    // 增加 I/O 鲁棒性：尝试安全地获取文件属性
                    try
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
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load properties for {file.Name}: {ex.Message}");
                        failedCount++;
                    }
                }

                int successfulCount = _cachedAllItems.Count;
                CountBlock.Text = $"ITEMS: {successfulCount}";

                if (failedCount > 0)
                {
                    ShowTipDialog($"警告: 成功加载 {successfulCount} 个文件，有 {failedCount} 个文件因权限或 I/O 错误被跳过。");
                }

                // 首次填充完毕，调用统一的刷新方法进行排序和显示
                RefreshViewFromCache();
            }
            catch (Exception ex)
            {
                HandleError($"加载失败: {ex.Message}");
            }
        }

        // --- 删除逻辑 (保持不变) ---

        private async void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;

            var selectedItems = ImageGrid.SelectedItems.Cast<ImageItem>().ToList();
            if (selectedItems.Count == 0)
            {
                _isDialogActive = false;
                ShowTipDialog("请先选择文件");
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

                // 批量删除，并显示进度
                await PerformDeleteLoop(selectedItems);
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        // 批量删除核心逻辑，接受 ImageItem 列表
        private async Task PerformDeleteLoop(List<ImageItem> itemsToDelete)
        {
            StartProgressState($"正在删除... (0/{itemsToDelete.Count})", itemsToDelete.Count);

            int deletedCount = 0;
            int processedCount = 0;
            const int batchSize = 50; // 每处理 50 个文件更新一次进度条

            // 1. 在后台执行删除
            await Task.Run(async () =>
            {
                foreach (var item in itemsToDelete)
                {
                    processedCount++;

                    try
                    {
                        if (item.ImagePath != null)
                        {
                            var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                            await file.DeleteAsync(StorageDeleteOption.Default);

                            // 仅从后台缓存中移除，不操作 UI 集合
                            var itemToRemove = _cachedAllItems.FirstOrDefault(x => x.ImagePath == file.Path);
                            if (itemToRemove != null)
                            {
                                _cachedAllItems.Remove(itemToRemove);
                            }

                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Deletion failed for {item.FileName}: {ex.Message}");
                    }

                    // 批量更新 UI 进度条
                    if (processedCount % batchSize == 0 || processedCount == itemsToDelete.Count)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            UpdateProgressValue(processedCount);
                            LoadingStatusText.Text = $"正在删除... ({processedCount}/{itemsToDelete.Count})";
                        });
                    }
                }
            });

            // 2. 删除完成后，一次性刷新 UI 集合
            RefreshViewFromCache();

            CountBlock.Text = $"ITEMS: {_cachedAllItems.Count}";
            RestoreUI(true);

            ShowTipDialog($"操作完成。成功删除 {deletedCount} 个文件。");
        }


        // --- 去重逻辑 (保持不变) ---

        private async void OnScanDuplicatesClicked(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) { ShowTipDialog("请先导入文件夹"); return; }
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                SetBusyState("正在初筛 (按大小)...");
                var fileExtensions = new List<string> {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp",
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp" // 视频扩展名
                };
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions);
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
                    try
                    {
                        var p = await f.GetBasicPropertiesAsync();
                        fileInfos.Add((f, p.Size));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Skipping file due to I/O error: {f.Name}. Error: {ex.Message}");
                    }
                }

                var groups = fileInfos.GroupBy(x => x.s).Where(g => g.Count() > 1).ToList();
                if (!groups.Any())
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("恭喜，当前文件夹没有发现重复文件！");
                    return;
                }

                StartProgressState($"正在进行深度校验 (MD5)... 共 {groups.Count} 组疑似重复", groups.Count);

                // 2. 复筛：并发计算 MD5
                var dupes = new ConcurrentBag<List<StorageFile>>();
                int groupsProcessed = 0;

                await Task.Run(async () =>
                {
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

                        foreach (var hg in hashes.Values)
                        {
                            if (hg.Count > 1) dupes.Add(hg);
                        }

                        // 实时更新进度条
                        groupsProcessed++;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            UpdateProgressValue(groupsProcessed);
                            LoadingStatusText.Text = $"正在进行深度校验... ({groupsProcessed}/{groups.Count})";
                        });
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

        // --- 核心：去重结果对话框 (使用预编译资源) ---
        private async Task ShowDuplicateResultsDialog(List<List<StorageFile>> dupes)
        {
            var flatList = new ObservableCollection<DuplicateItem>();

            // 1. 构造扁平化数据源
            foreach (var g in dupes)
            {
                flatList.Add(DuplicateItem.CreateSeparator());

                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var f = sorted[i];
                    bool keep = (i == 0); // 建议保留最新创建的文件
                    var item = new DuplicateItem(f, keep);
                    flatList.Add(item);
                }
            }

            // 移除第一个不必要的组分隔符 (如果存在)
            if (flatList.Any() && flatList.First().IsGroupSeparator) flatList.RemoveAt(0);

            // 2. 构造 ListView 和 DataTemplate (实现虚拟化)
            var listView = new ListView
            {
                ItemsSource = flatList,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 400,
                Margin = new Thickness(0, 0, 10, 0)
            };

            // XAML 字符串引用 Page.Resources 中已预编译的 StaticResource
            string xaml = @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                    <Grid x:Name='ItemGrid' Margin='0,0,0,0' Padding='0'>
                        
                        <TextBlock Text='重复组' Margin='0,10,0,0' 
                                   FontWeight='SemiBold' FontSize='16'
                                   Foreground='#00FFFF' Opacity='0.7'
                                   VerticalAlignment='Top'
                                   Visibility='{Binding IsGroupSeparator, Converter={StaticResource BoolToVisibleConverter}}'/>

                        <CheckBox x:Name='FileCheckBox'
                                  IsChecked='{Binding IsChecked, Mode=TwoWay}'
                                  Foreground='{Binding ForegroundBrush}'
                                  Opacity='{Binding OpacityValue}'
                                  Margin='0,5,0,5'
                                  Visibility='{Binding IsGroupSeparator, Converter={StaticResource InverseBoolToVisibleConverter}}'>
                            <TextBlock>
                                <Run Text='{Binding DisplayName}' FontWeight='SemiBold'/>
                                <Run Text=' ({Binding File.DateCreated, Converter={StaticResource DateConverter}})' FontSize='12' Foreground='#80FFFFFF'/>
                                <Run Text='{Binding IsKeepSuggestion, Converter={StaticResource BoolToKeepTextConverter}}' Foreground='LightGreen' FontSize='12'/>
                            </TextBlock>
                        </CheckBox>
                    </Grid>
                </DataTemplate>";

            // 注入 XAML 并解析为 DataTemplate
            listView.ItemTemplate = XamlReader.Load(xaml) as DataTemplate;

            var dlg = new ContentDialog
            {
                Title = $"发现 {dupes.Count} 组重复文件",
                Content = listView,
                PrimaryButtonText = "删除选中项",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                var toDelFilePaths = flatList
                    .Where(i => i.File != null && i.IsChecked)
                    .Select(i => i.File!.Path)
                    .ToList();

                if (toDelFilePaths.Any())
                {
                    var itemsToDelete = _cachedAllItems
                        .Where(item => item.ImagePath != null && toDelFilePaths.Contains(item.ImagePath))
                        .ToList();

                    await PerformDeleteLoop(itemsToDelete);
                }
                else
                {
                    ShowTipDialog("未选择任何文件进行删除。");
                }
            }
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