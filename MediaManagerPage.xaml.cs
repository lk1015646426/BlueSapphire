using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
using System.Threading;
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
    // 1. 数据模型: ImageItem (主界面图片)
    // ==========================================
    public class ImageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
            set { if (_imageSource != value) { _imageSource = value; OnPropertyChanged(); } }
        }

        private bool _isImageLoading = false;
        public bool IsImageLoading
        {
            get => _isImageLoading;
            set { if (_isImageLoading != value) { _isImageLoading = value; OnPropertyChanged(); } }
        }

        private bool _isLoaded = false;

        public async Task LoadImageAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath)) return;
            _isLoaded = true;
            IsImageLoading = true;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(ImagePath);
                using var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.PicturesView, 200);
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
                        ImageSource = bitmap;
                    });
                }
            }
            catch { _isLoaded = false; }
            finally { IsImageLoading = false; }
        }
    }

    // ==========================================
    // 2. 数据模型: DuplicateItem (去重弹窗专用)
    // ==========================================
    public class DuplicateItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public StorageFile? File { get; }
        public bool IsGroupSeparator { get; }
        public string DisplayName => File?.Name ?? "重复组";
        public bool IsKeepSuggestion { get; }

        // --- UI 绑定属性 (自包含逻辑，无需依赖外部转换器) ---
        public Visibility SeparatorVisibility => IsGroupSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CheckBoxVisibility => !IsGroupSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SuggestionVisibility => IsKeepSuggestion ? Visibility.Visible : Visibility.Collapsed;
        public string DateString => File?.DateCreated.ToString("yyyy-MM-dd HH:mm") ?? "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); } }
        }

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

        public static DuplicateItem CreateSeparator() => new DuplicateItem(true);
    }

    // ==========================================
    // 3. 页面逻辑: MediaManagerPage
    // ==========================================
    public sealed partial class MediaManagerPage : Page, ITool, INotifyPropertyChanged
    {
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private IncrementalLoadingCollection<ImageItem>? _images;
        public IncrementalLoadingCollection<ImageItem>? Images
        {
            get => _images;
            set { if (_images != value) { _images = value; OnPropertyChanged(); } }
        }

        private List<ImageItem> _cachedAllItems = new List<ImageItem>();
        private StorageFolder? _currentFolder;
        private bool _isDialogActive = false;
        private string CurrentSortTag { get; set; } = "Name";
        private CancellationTokenSource? _globalCts;

        public MediaManagerPage()
        {
            this.InitializeComponent();
            UpdateSortDirectionIcon();
        }

        public void Initialize() { }

        // --- UI 状态控制 ---
        private void SetBusyState(string statusText, bool isProgress = false, int max = 100)
        {
            StatusOverlay.Visibility = Visibility.Visible;
            LoadingStatusText.Text = statusText;

            if (isProgress)
            {
                ScanningProgressBar.Visibility = Visibility.Visible;
                ScanningProgressBar.IsIndeterminate = false;
                ScanningProgressBar.Minimum = 0;
                ScanningProgressBar.Maximum = max;
                ScanningProgressBar.Value = 0;
            }
            else
            {
                ScanningProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void RestoreUI(bool hasContent = true)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            StatusBlock.Text = "STATUS: READY";
            ScanningProgressBar.Visibility = Visibility.Collapsed;

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
            finally { _isDialogActive = false; }
        }

        // --- 加载与显示 ---
        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is ImageItem item && !args.InRecycleQueue)
            {
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        public async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                var openPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };
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
                ShowTipDialog($"无法打开文件夹: {ex.Message}");
            }
            finally { _isDialogActive = false; }
        }

        private async Task LoadFolderContent(StorageFolder folder)
        {
            SetBusyState("正在扫描文件...");
            Images = null;
            _cachedAllItems.Clear();
            PathBlock.Text = $"PATH: {folder.Path}";

            try
            {
                var fileExtensions = new List<string> {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp",
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp"
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

                foreach (var file in files)
                {
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
                    catch { }
                }

                CountBlock.Text = $"ITEMS: {_cachedAllItems.Count}";
                RefreshViewFromCache();
            }
            catch (Exception ex)
            {
                ShowTipDialog($"读取失败: {ex.Message}");
                RestoreUI(false);
            }
        }

        // --- 排序逻辑 ---
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

        private void UpdateSortDirectionIcon()
        {
            bool isDescending = SortDirectionToggle.IsChecked ?? false;
            SortDirectionIcon.Glyph = isDescending ? "\uE74A" : "\uE74B";
        }

        private void RefreshViewFromCache()
        {
            if (_cachedAllItems.Count == 0) return;
            SetBusyState("正在排序...");

            bool isDescending = SortDirectionToggle.IsChecked ?? false;
            IEnumerable<ImageItem> query = _cachedAllItems;

            query = CurrentSortTag switch
            {
                "Date" => isDescending ? query.OrderByDescending(x => x.DateCreated) : query.OrderBy(x => x.DateCreated),
                "Size" => isDescending ? query.OrderByDescending(x => x.FileSize) : query.OrderBy(x => x.FileSize),
                _ => isDescending ? query.OrderByDescending(x => x.FileName) : query.OrderBy(x => x.FileName),
            };

            var sortedList = query.ToList();

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

        // =========================================================
        // 核心修复: 去重功能 (单线程安全版)
        // =========================================================

        private async void OnScanDuplicatesClicked(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) { ShowTipDialog("请先导入文件夹"); return; }
            if (_isDialogActive) return;
            _isDialogActive = true;

            _globalCts?.Cancel();
            _globalCts = new CancellationTokenSource();
            var token = _globalCts.Token;

            try
            {
                SetBusyState("正在初筛 (按大小)...");

                var fileExtensions = new List<string> {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp",
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp"
                };
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions);
                queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new[] { "System.Size" });

                var files = await _currentFolder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (files.Count < 2)
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("文件不足，无需扫描");
                    return;
                }

                var fileInfos = new List<(StorageFile f, ulong s)>();
                foreach (var f in files)
                {
                    try { var p = await f.GetBasicPropertiesAsync(); fileInfos.Add((f, p.Size)); } catch { }
                }

                var groups = fileInfos.GroupBy(x => x.s).Where(g => g.Count() > 1).ToList();
                if (!groups.Any())
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("未发现重复文件 (按大小)");
                    return;
                }

                // 2. 深度扫描 (MD5) - 单线程安全模式
                SetBusyState($"正在校验 MD5... (0/{groups.Count})", true, groups.Count);

                var dupes = new List<List<StorageFile>>();
                int processedCount = 0;
                int totalGroups = groups.Count;

                // 启动 UI 监控线程 (避免高频刷新)
                var uiMonitorTask = Task.Run(async () =>
                {
                    while (processedCount < totalGroups && !token.IsCancellationRequested)
                    {
                        await Task.Delay(200);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ScanningProgressBar.Value = processedCount;
                            LoadingStatusText.Text = $"正在校验 MD5... ({processedCount}/{totalGroups})";
                        });
                    }
                });

                // 启动后台计算 (使用普通 foreach，不用 Parallel)
                await Task.Run(async () =>
                {
                    foreach (var group in groups)
                    {
                        if (token.IsCancellationRequested) break;

                        var hashes = new Dictionary<string, List<StorageFile>>();

                        foreach (var item in group)
                        {
                            try
                            {
                                // 单线程顺序执行，绝不崩溃
                                string hash = await FileHelper.ComputeMD5Async(item.f);
                                if (!string.IsNullOrEmpty(hash))
                                {
                                    if (!hashes.ContainsKey(hash)) hashes[hash] = new List<StorageFile>();
                                    hashes[hash].Add(item.f);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"跳过损坏文件: {ex.Message}");
                            }
                        }

                        foreach (var hList in hashes.Values)
                        {
                            if (hList.Count > 1) dupes.Add(hList);
                        }

                        Interlocked.Increment(ref processedCount);
                    }
                });

                await uiMonitorTask; // 等待 UI 监控结束

                RestoreUI(true);
                _isDialogActive = false;

                if (dupes.Any())
                {
                    await ShowDuplicateResultsDialog(dupes);
                }
                else
                {
                    ShowTipDialog("恭喜，没有发现内容完全一致的重复文件。");
                }
            }
            catch (OperationCanceledException)
            {
                RestoreUI(true);
                _isDialogActive = false;
            }
            catch (Exception ex)
            {
                ShowTipDialog($"扫描出错: {ex.Message}");
                RestoreUI(true);
                _isDialogActive = false;
            }
        }

        // --- 结果弹窗与删除 ---

        private async Task ShowDuplicateResultsDialog(List<List<StorageFile>> dupes)
        {
            var flatList = new ObservableCollection<DuplicateItem>();
            foreach (var g in dupes)
            {
                flatList.Add(DuplicateItem.CreateSeparator());
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    flatList.Add(new DuplicateItem(sorted[i], i == 0)); // 默认保留最新的一个
                }
            }
            if (flatList.Any()) flatList.RemoveAt(0);

            var listView = new ListView
            {
                ItemsSource = flatList,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 400
            };

            string xaml = @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Grid>
                        <TextBlock Text='重复组' FontWeight='Bold' Foreground='#00FFFF' Opacity='0.6' Margin='0,10,0,0'
                                   Visibility='{Binding SeparatorVisibility}'/>
                                   
                        <CheckBox IsChecked='{Binding IsChecked, Mode=TwoWay}' Margin='0,2,0,2'
                                  Visibility='{Binding CheckBoxVisibility}'>
                            <StackPanel Orientation='Horizontal'>
                                <TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold' />
                                <TextBlock Text=' (' Foreground='#80FFFFFF' FontSize='12'/>
                                <TextBlock Text='{Binding DateString}' Foreground='#80FFFFFF' FontSize='12'/>
                                <TextBlock Text=')' Foreground='#80FFFFFF' FontSize='12'/>
                                <TextBlock Text=' [推荐保留]' Foreground='LightGreen' Margin='10,0,0,0' FontSize='12'
                                           Visibility='{Binding SuggestionVisibility}'/>
                            </StackPanel>
                        </CheckBox>
                    </Grid>
                </DataTemplate>";

            listView.ItemTemplate = (DataTemplate)XamlReader.Load(xaml);

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
                var filesToDelete = flatList
                    .Where(x => x.File != null && x.IsChecked)
                    .Select(x => x.File!)
                    .ToList();

                if (filesToDelete.Any())
                {
                    await PerformDeleteFiles(filesToDelete);
                }
            }
        }

        private async Task PerformDeleteFiles(List<StorageFile> files)
        {
            SetBusyState($"正在删除... (0/{files.Count})", true, files.Count);

            int deletedCount = 0;
            int total = files.Count;

            var uiTask = Task.Run(async () =>
            {
                while (deletedCount < total)
                {
                    await Task.Delay(100);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ScanningProgressBar.Value = deletedCount;
                        LoadingStatusText.Text = $"正在删除... ({deletedCount}/{total})";
                    });
                }
            });

            await Task.Run(async () =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        await file.DeleteAsync();
                        var cacheItem = _cachedAllItems.FirstOrDefault(x => x.ImagePath == file.Path);
                        if (cacheItem != null) _cachedAllItems.Remove(cacheItem);
                    }
                    catch { }
                    Interlocked.Increment(ref deletedCount);
                }
            });

            await uiTask;

            RefreshViewFromCache();
            ShowTipDialog($"删除完成，共清理 {deletedCount} 个文件。");
        }

        private async void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            var selectedItems = ImageGrid.SelectedItems.Cast<ImageItem>().ToList();
            if (!selectedItems.Any()) { ShowTipDialog("请先选择文件"); return; }

            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除选中的 {selectedItems.Count} 个文件吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var files = new List<StorageFile>();
                foreach (var item in selectedItems)
                {
                    try { files.Add(await StorageFile.GetFileFromPathAsync(item.ImagePath)); } catch { }
                }
                await PerformDeleteFiles(files);
            }
        }

        private void ImageGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ImageGrid.SelectedItem is ImageItem item)
            {
                ShowTipDialog($"文件名: {item.FileName}\n路径: {item.ImagePath}\n大小: {item.FileSizeString}");
            }
        }
    }
}