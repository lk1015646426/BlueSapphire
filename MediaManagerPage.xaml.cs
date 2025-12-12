#pragma warning disable CA1416
#pragma warning disable IDE0060

using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
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

                // 每次进入忙碌状态时，先重置副标题
                if (StatusDetailText != null) StatusDetailText.Text = "";

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

        // 【修改版】支持主标题和副标题（详细数量）更新
        private void UpdateProgress(double value, string? mainText = null, string? detailText = null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ScanningProgressBar.Value = value;

                if (!string.IsNullOrEmpty(mainText))
                {
                    LoadingStatusText.Text = mainText;
                }

                if (StatusDetailText != null)
                {
                    StatusDetailText.Text = detailText ?? "";
                    StatusDetailText.Visibility = string.IsNullOrEmpty(detailText) ? Visibility.Collapsed : Visibility.Visible;
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
                    return sortedList.Skip(currentLen).Take((int)count);
                });
            });

            RestoreUI(true);
        }

        // =========================================================
        // 核心重构: 分级去重算法 (大小优先 -> MD5精筛)
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
                SetBusyState("正在初始化扫描...", true, 100);

                var fileExtensions = new List<string> {
                    ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp", ".heic",
                    ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp"
                };

                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions);
                // 预取文件大小属性，极大加快初筛速度
                queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new[] { "System.Size" });

                var allFiles = await _currentFolder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();
                int totalFiles = allFiles.Count;

                if (totalFiles < 2)
                {
                    RestoreUI(true);
                    _isDialogActive = false;
                    ShowTipDialog("文件不足，无需扫描");
                    return;
                }

                // 更新进度条最大值
                DispatcherQueue.TryEnqueue(() => ScanningProgressBar.Maximum = totalFiles);

                var finalDuplicateGroups = new List<List<StorageFile>>();
                int processedCount = 0;

                // --- 启动 UI 监控任务 (仅用于第一阶段) ---
                var uiMonitorTask = Task.Run(async () =>
                {
                    while (processedCount < totalFiles && !token.IsCancellationRequested)
                    {
                        await Task.Delay(100);
                        string countStr = $"{processedCount} / {totalFiles}";
                        UpdateProgress(processedCount, "正在建立索引 (对比大小)...", countStr);
                    }
                });

                // --- 开始后台处理 ---
                await Task.Run(async () =>
                {
                    // === 第一阶段：基于文件大小分组 ===
                    var sizeGroups = new Dictionary<ulong, List<StorageFile>>();

                    foreach (var file in allFiles)
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            var props = await file.GetBasicPropertiesAsync();
                            ulong size = props.Size;

                            if (!sizeGroups.ContainsKey(size))
                            {
                                sizeGroups[size] = new List<StorageFile>();
                            }
                            sizeGroups[size].Add(file);
                        }
                        catch { }

                        Interlocked.Increment(ref processedCount);
                    }

                    await uiMonitorTask;

                    // 筛选出大小相同的“可疑组”
                    var suspectGroups = sizeGroups.Values.Where(g => g.Count > 1).ToList();

                    // === 准备进入第二阶段 ===
                    int totalSuspects = suspectGroups.Sum(g => g.Count);
                    int hashedCount = 0;

                    if (totalSuspects > 0)
                    {
                        // 重置进度条
                        DispatcherQueue.TryEnqueue(() => {
                            ScanningProgressBar.Maximum = totalSuspects;
                            ScanningProgressBar.Value = 0;
                        });

                        // === 第二阶段：深度校验 MD5 ===
                        foreach (var group in suspectGroups)
                        {
                            if (token.IsCancellationRequested) break;

                            var md5SubGroups = new Dictionary<string, List<StorageFile>>();

                            foreach (var file in group)
                            {
                                string hash = "";
                                try { hash = await FileHelper.ComputeMD5Async(file); } catch { }

                                if (!string.IsNullOrEmpty(hash))
                                {
                                    if (!md5SubGroups.ContainsKey(hash))
                                    {
                                        md5SubGroups[hash] = new List<StorageFile>();
                                    }
                                    md5SubGroups[hash].Add(file);
                                }

                                hashedCount++;

                                // 直接更新第二阶段进度
                                string detailStr = $"{hashedCount} / {totalSuspects}";
                                UpdateProgress(hashedCount, "正在深度校验 (MD5计算)...", detailStr);
                            }

                            // 添加确认重复的组
                            foreach (var subGroup in md5SubGroups.Values.Where(g => g.Count > 1))
                            {
                                finalDuplicateGroups.Add(subGroup);
                            }
                        }
                    }
                });

                RestoreUI(true);
                _isDialogActive = false;

                if (finalDuplicateGroups.Any())
                {
                    await ShowDuplicateResultsDialog(finalDuplicateGroups);
                }
                else
                {
                    ShowTipDialog("扫描完成，未发现重复文件。\n(已自动区分连拍照片与不同大小的视频)");
                }
            }
            catch (Exception ex)
            {
                RestoreUI(true);
                _isDialogActive = false;
                ShowTipDialog($"扫描中断: {ex.Message}");
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