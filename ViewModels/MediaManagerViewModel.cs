using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;

namespace BlueSapphire.ViewModels
{
    public partial class MediaManagerViewModel : ObservableObject
    {
        private readonly IMediaViewInteraction _view;
        private readonly DispatcherQueue _dispatcherQueue;
        private List<ImageItem> _cachedAllItems = new List<ImageItem>();
        private CancellationTokenSource? _globalCts;
        private StorageFolder? _currentFolder;

        // --- 绑定属性 ---

        [ObservableProperty]
        private IncrementalLoadingCollection<ImageItem>? _images;

        [ObservableProperty]
        private string _statusMainText = "READY";

        [ObservableProperty]
        private string _statusDetailText = "";

        [ObservableProperty]
        private string _pathText = "PATH: NULL";

        [ObservableProperty]
        private string _countText = "ITEMS: 0";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isProgressVisible;

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private double _progressMax = 100;

        [ObservableProperty]
        private bool _isEmptyStateVisible = true;

        [ObservableProperty]
        private string _currentSortField = "Name"; // Name, Date, Size

        [ObservableProperty]
        private bool _isSortDescending;

        public MediaManagerViewModel(IMediaViewInteraction view, DispatcherQueue dispatcherQueue)
        {
            _view = view;
            _dispatcherQueue = dispatcherQueue;
        }

        // --- 命令 (Commands) ---

        [RelayCommand]
        private async Task OpenFolder()
        {
            var folder = await _view.PickFolderAsync();
            if (folder != null)
            {
                _currentFolder = folder;
                await LoadFolderContentAsync(folder);
            }
        }

        [RelayCommand]
        private void ChangeSort(string field)
        {
            CurrentSortField = field;
            RefreshViewFromCache();
        }

        [RelayCommand]
        private void ToggleSortDirection()
        {
            IsSortDescending = !IsSortDescending;
            RefreshViewFromCache();
        }

        [RelayCommand]
        private async Task ScanDuplicates()
        {
            if (_currentFolder == null) { await _view.ShowTipAsync("请先导入文件夹"); return; }
            if (IsBusy) return;

            _globalCts?.Cancel();
            _globalCts = new CancellationTokenSource();
            var token = _globalCts.Token;

            try
            {
                SetBusy(true, "正在初始化扫描...", 0, 100);

                // 1. 获取所有文件 (预取大小)
                var fileExtensions = new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp", ".heic", ".mp4", ".mov", ".avi", ".wmv", ".mkv" };
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions);
                queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new[] { "System.Size" });

                var allFiles = await _currentFolder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (allFiles.Count < 2)
                {
                    SetBusy(false);
                    await _view.ShowTipAsync("文件不足，无需扫描");
                    return;
                }

                UpdateProgress(0, allFiles.Count, "正在建立索引 (对比大小)...");

                // 2. 第一阶段：大小分组
                var suspectGroups = await Task.Run(() =>
                {
                    var sizeGroups = new Dictionary<ulong, List<StorageFile>>();
                    int processed = 0;
                    foreach (var file in allFiles)
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            ulong size = file.GetBasicPropertiesAsync().AsTask().Result.Size;
                            if (!sizeGroups.ContainsKey(size)) sizeGroups[size] = new List<StorageFile>();
                            sizeGroups[size].Add(file);
                        }
                        catch { }

                        // 简单的进度节流
                        if (++processed % 50 == 0)
                            _dispatcherQueue.TryEnqueue(() => ProgressValue = processed);
                    }
                    return sizeGroups.Values.Where(g => g.Count > 1).ToList();
                });

                // 3. 第二阶段：MD5 校验
                int totalSuspects = suspectGroups.Sum(g => g.Count);
                if (totalSuspects == 0)
                {
                    SetBusy(false);
                    await _view.ShowTipAsync("未发现重复文件 (大小均不相同)");
                    return;
                }

                UpdateProgress(0, totalSuspects, "正在深度校验 (MD5计算)...");

                var finalDuplicates = await Task.Run(async () =>
                {
                    var result = new List<List<StorageFile>>();
                    int hashed = 0;

                    foreach (var group in suspectGroups)
                    {
                        if (token.IsCancellationRequested) break;
                        var md5Groups = new Dictionary<string, List<StorageFile>>();

                        foreach (var file in group)
                        {
                            string hash = await FileHelper.ComputeMD5Async(file);
                            if (!string.IsNullOrEmpty(hash))
                            {
                                if (!md5Groups.ContainsKey(hash)) md5Groups[hash] = new List<StorageFile>();
                                md5Groups[hash].Add(file);
                            }

                            hashed++;
                            if (hashed % 5 == 0)
                                _dispatcherQueue.TryEnqueue(() => {
                                    ProgressValue = hashed;
                                    StatusDetailText = $"{hashed} / {totalSuspects}";
                                });
                        }

                        foreach (var g in md5Groups.Values.Where(g => g.Count > 1))
                        {
                            result.Add(g);
                        }
                    }
                    return result;
                });

                SetBusy(false);

                if (finalDuplicates.Count > 0)
                {
                    // 弹出删除确认框 (由 View 实现)
                    var filesToDelete = await _view.ShowDuplicateResultsAsync(finalDuplicates);
                    if (filesToDelete != null && filesToDelete.Count > 0)
                    {
                        await PerformDeleteFiles(filesToDelete);
                    }
                }
                else
                {
                    await _view.ShowTipAsync("扫描完成，未发现内容重复的文件。");
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
                await _view.ShowTipAsync($"扫描中断: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DeleteSelected(IList<object> selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0)
            {
                await _view.ShowTipAsync("请先选择文件");
                return;
            }

            var confirm = await _view.ShowDeleteConfirmationAsync(selectedItems.Count);
            if (confirm)
            {
                var files = new List<StorageFile>();
                foreach (var item in selectedItems.Cast<ImageItem>())
                {
                    try
                    {
                        if (item.ImagePath != null)
                            files.Add(await StorageFile.GetFileFromPathAsync(item.ImagePath));
                    }
                    catch { }
                }
                await PerformDeleteFiles(files);
            }
        }

        // --- 私有方法 ---

        private async Task LoadFolderContentAsync(StorageFolder folder)
        {
            SetBusy(true, "正在扫描文件...");
            Images = null;
            _cachedAllItems.Clear();
            PathText = $"PATH: {folder.Path}";
            IsEmptyStateVisible = false;

            try
            {
                var fileExtensions = new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp", ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".heic" };
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions) { FolderDepth = FolderDepth.Deep };
                var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (files.Count == 0)
                {
                    CountText = "ITEMS: 0";
                    IsEmptyStateVisible = true;
                    SetBusy(false);
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

                CountText = $"ITEMS: {_cachedAllItems.Count}";
                RefreshViewFromCache();
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync($"读取失败: {ex.Message}");
                IsEmptyStateVisible = true;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RefreshViewFromCache()
        {
            if (_cachedAllItems.Count == 0) return;

            // 在 UI 线程更新集合，防止多线程冲突
            _dispatcherQueue.TryEnqueue(() =>
            {
                IEnumerable<ImageItem> query = _cachedAllItems;
                query = CurrentSortField switch
                {
                    "Date" => IsSortDescending ? query.OrderByDescending(x => x.DateCreated) : query.OrderBy(x => x.DateCreated),
                    "Size" => IsSortDescending ? query.OrderByDescending(x => x.FileSize) : query.OrderBy(x => x.FileSize),
                    _ => IsSortDescending ? query.OrderByDescending(x => x.FileName) : query.OrderBy(x => x.FileName),
                };

                var sortedList = query.ToList();
                Images = new IncrementalLoadingCollection<ImageItem>(async (token, count) =>
                {
                    return await Task.Run(() => sortedList.Skip(Images?.Count ?? 0).Take((int)count));
                });
            });
        }

        private async Task PerformDeleteFiles(List<StorageFile> files)
        {
            SetBusy(true, "正在删除...", 0, files.Count);
            int deletedCount = 0;

            await Task.Run(async () =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        await file.DeleteAsync();

                        // 线程安全地从缓存移除
                        lock (_cachedAllItems)
                        {
                            var cacheItem = _cachedAllItems.FirstOrDefault(x => x.ImagePath == file.Path);
                            if (cacheItem != null) _cachedAllItems.Remove(cacheItem);
                        }
                    }
                    catch { }

                    Interlocked.Increment(ref deletedCount);
                    if (deletedCount % 10 == 0)
                    {
                        _dispatcherQueue.TryEnqueue(() => {
                            ProgressValue = deletedCount;
                            StatusMainText = $"正在删除... ({deletedCount}/{files.Count})";
                        });
                    }
                }
            });

            SetBusy(false);
            CountText = $"ITEMS: {_cachedAllItems.Count}";
            RefreshViewFromCache(); // 重新生成视图
            await _view.ShowTipAsync($"清理完成，共删除 {deletedCount} 个文件。");
        }

        private void SetBusy(bool busy, string text = "", double val = 0, double max = 100)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsBusy = busy;
                StatusMainText = text;
                StatusDetailText = "";
                IsProgressVisible = busy; // 简化处理，忙碌即显示进度条
                ProgressValue = val;
                ProgressMax = max;
            });
        }

        private void UpdateProgress(double val, double max, string mainText)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ProgressValue = val;
                ProgressMax = max;
                StatusMainText = mainText;
            });
        }
    }
}