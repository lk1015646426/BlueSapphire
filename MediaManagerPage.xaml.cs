#pragma warning disable CA1416 // 忽略 "仅在 Windows xxx 上受支持" 的平台兼容性警告
#pragma warning disable IDE0060 // 忽略 "移除未使用的参数" 建议

using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models; // 【新增】引用分离的模型命名空间
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;

// 确保项目引用了 Magick.NET-Q8-AnyCPU (用于 HEIC 识别)

namespace BlueSapphire
{
    // ==========================================
    // 页面逻辑: MediaManagerPage
    // ==========================================
    public sealed partial class MediaManagerPage : Page, ITool, INotifyPropertyChanged
    {
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private IncrementalLoadingCollection<ImageItem>? _images;
        public IncrementalLoadingCollection<ImageItem>? Images
        {
            get => _images;
            set { if (_images != value) { _images = value; OnPropertyChanged(); } }
        }

        private ObservableCollection<DuplicateGroup> _duplicateGroups = new ObservableCollection<DuplicateGroup>();
        public ObservableCollection<DuplicateGroup> DuplicateGroups
        {
            get => _duplicateGroups;
            set { if (_duplicateGroups != value) { _duplicateGroups = value; OnPropertyChanged(); } }
        }

        private List<ImageItem> _cachedAllItems = new List<ImageItem>();
        private StorageFolder? _currentFolder;
        private bool _isDialogActive = false;
        private string CurrentSortTag { get; set; } = "Name";
        private CancellationTokenSource? _globalCts;

        // 内部类：用于扫描过程中的临时数据 (无需拆分，因为只在此处使用)
        private class ScannedItemInfo
        {
            public StorageFile File { get; set; } = null!;
            public ulong? VisualHash { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public long FileSize { get; set; }
            public DateTimeOffset CreationTime { get; set; }
            public bool IsImage { get; set; }
            public bool IsProcessed { get; set; } = false;
        }

        public MediaManagerPage()
        {
            this.InitializeComponent();
            UpdateSortDirectionIcon();
        }

        public void Initialize() { }

        // --- UI 状态控制 ---
        private void SetBusyState(string statusText, bool isProgress = false, int max = 100)
        {
            DispatcherQueue.TryEnqueue(() =>
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
            });
        }

        private void UpdateProgress(double value, string? text = null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ScanningProgressBar.Value = value;
                if (!string.IsNullOrEmpty(text))
                {
                    LoadingStatusText.Text = text;
                }
            });
        }

        private void RestoreUI(bool hasContent = true)
        {
            DispatcherQueue.TryEnqueue(() =>
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
            });
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
                var openPicker = new Windows.Storage.Pickers.FolderPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
                };
                openPicker.FileTypeFilter.Add("*");

                // WinUI 3 需要窗口句柄
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
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp", ".heic"
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
                    // 确保 count 被显式转换为 int
                    return sortedList.Skip(currentLen).Take((int)count);
                });
            });

            RestoreUI(true);
        }

        // =========================================================
        // 核心修复: 智能混合去重 (视觉 dHash + 精确 MD5)
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
                SetBusyState("正在索引文件...");

                var fileExtensions = new List<string> {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp", ".heic",
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp"
                };

                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions);
                queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new[] { "System.Size" });

                var allFiles = await _currentFolder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (allFiles.Count < 2)
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("文件不足，无需扫描");
                    return;
                }

                var imageFiles = new List<StorageFile>();
                var otherFiles = new List<StorageFile>();
                var imgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp", ".heic"
                };

                foreach (var f in allFiles)
                {
                    if (imgExts.Contains(f.FileType)) imageFiles.Add(f);
                    else otherFiles.Add(f);
                }

                int totalCount = imageFiles.Count + otherFiles.Count;
                int processedCount = 0;
                var finalDuplicateGroups = new List<List<StorageFile>>();

                var uiMonitorTask = Task.Run(async () =>
                {
                    while (processedCount < totalCount && !token.IsCancellationRequested)
                    {
                        await Task.Delay(200);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ScanningProgressBar.Value = processedCount;
                            LoadingStatusText.Text = $"正在深度分析... ({processedCount}/{totalCount})";
                        });
                    }
                });

                // --- 策略 A: 视频/其他文件 (先按大小分组，再算 MD5) ---
                if (otherFiles.Count > 0)
                {
                    await Task.Run(async () =>
                    {
                        var fileSizes = new List<(StorageFile f, ulong size)>();
                        foreach (var f in otherFiles)
                        {
                            try
                            {
                                var props = await f.GetBasicPropertiesAsync();
                                fileSizes.Add((f, props.Size));
                            }
                            catch { }
                            if (token.IsCancellationRequested) break;
                        }

                        var sizeGroups = fileSizes.GroupBy(x => x.size).Where(g => g.Count() > 1).ToList();
                        int skippedCount = otherFiles.Count - sizeGroups.Sum(g => g.Count());
                        Interlocked.Add(ref processedCount, skippedCount);

                        foreach (var group in sizeGroups)
                        {
                            if (token.IsCancellationRequested) break;

                            var md5Map = new Dictionary<string, List<StorageFile>>();
                            foreach (var item in group)
                            {
                                try
                                {
                                    string hash = await FileHelper.ComputeMD5Async(item.f);
                                    if (!string.IsNullOrEmpty(hash))
                                    {
                                        if (!md5Map.ContainsKey(hash)) md5Map[hash] = new List<StorageFile>();
                                        md5Map[hash].Add(item.f);
                                    }
                                }
                                catch { }
                                Interlocked.Increment(ref processedCount);
                            }

                            finalDuplicateGroups.AddRange(md5Map.Values.Where(g => g.Count > 1));
                        }
                    });
                }

                // --- 策略 B: 图片文件 (忽略大小，全量计算 dHash) ---
                if (imageFiles.Count > 0)
                {
                    var fileHashes = new List<(StorageFile file, ulong hash)>();

                    await Task.Run(async () =>
                    {
                        foreach (var file in imageFiles)
                        {
                            if (token.IsCancellationRequested) break;

                            var hash = await FileHelper.ComputeDHashAsync(file);

                            if (hash.HasValue)
                            {
                                fileHashes.Add((file, hash.Value));
                            }
                            Interlocked.Increment(ref processedCount);
                        }
                    });

                    await Task.Run(() =>
                    {
                        var processedIndices = new bool[fileHashes.Count];

                        for (int i = 0; i < fileHashes.Count; i++)
                        {
                            if (token.IsCancellationRequested) break;
                            if (processedIndices[i]) continue;

                            var currentGroup = new List<StorageFile> { fileHashes[i].file };
                            processedIndices[i] = true;

                            for (int j = i + 1; j < fileHashes.Count; j++)
                            {
                                if (processedIndices[j]) continue;

                                int distance = FileHelper.CalculateHammingDistance(fileHashes[i].hash, fileHashes[j].hash);

                                if (distance <= 3)
                                {
                                    currentGroup.Add(fileHashes[j].file);
                                    processedIndices[j] = true;
                                }
                            }

                            if (currentGroup.Count > 1)
                            {
                                finalDuplicateGroups.Add(currentGroup);
                            }
                        }
                    });
                }

                await uiMonitorTask;

                RestoreUI(true);
                _isDialogActive = false;

                if (finalDuplicateGroups.Any())
                {
                    await ShowDuplicateResultsDialog(finalDuplicateGroups);
                }
                else
                {
                    ShowTipDialog("未发现重复文件。\n(图片使用了视觉相似度检查，视频使用了MD5检查)");
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
                    flatList.Add(new DuplicateItem(sorted[i], i == 0));
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
                        if (cacheItem != null)
                        {
                            lock (_cachedAllItems) { _cachedAllItems.Remove(cacheItem); }
                        }
                    }
                    catch { }
                    Interlocked.Increment(ref deletedCount);
                }
            });

            await uiTask;

            RefreshViewFromCache();
            ShowTipDialog($"删除完成，共清理 {deletedCount} 个文件。");
        }

        // 静态方法优化
        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            double number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return string.Format("{0:n1}{1}", number, suffixes[counter]);
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
                    try { if (item.ImagePath != null) files.Add(await StorageFile.GetFileFromPathAsync(item.ImagePath)); } catch { }
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