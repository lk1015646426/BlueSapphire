using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics; // [新增] 用于日志输出
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;

namespace BlueSapphire.ViewModels
{
    public partial class MediaManagerViewModel : ObservableObject
    {
        private IMediaViewInteraction _view = null!;
        private DispatcherQueue _dispatcherQueue = null!;
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
        private string _currentSortField = "Name";

        [ObservableProperty]
        private bool _isSortDescending;

        // ✅ 增加一个私有只读字段存放 Service
        private readonly MediaRenameService _renameService;

        // ✅ 增加存放去重服务的字段
        private readonly BlueSapphire.Services.MediaDeduplicationService _deduplicationService;

        // ✅ 依赖注入容器会自动把实例从这里“喂”进来
        // ✅ 修改构造函数接收两个服务
        public MediaManagerViewModel(
            MediaRenameService renameService,
            BlueSapphire.Services.MediaDeduplicationService deduplicationService)
        {
            _renameService = renameService;
            _deduplicationService = deduplicationService;
        }
        public MediaManagerViewModel()
        {
        }

        // ✅ 修改 3：把原本写在构造函数里的赋值逻辑，挪到这个独立的 Initialize 方法里
        public void Initialize(IMediaViewInteraction view, DispatcherQueue dispatcherQueue)
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
        private async Task RenameSelected(IList<object> selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要重命名的图片");
                return;
            }

            if (_currentFolder == null) return;

            SetBusy(true, "正在并发分析文件属性...");
            var previewList = new ConcurrentBag<RenamePreviewItem>();
            int skippedCount = 0;

            var existingFiles = await _currentFolder.GetFilesAsync();
            var allFilenames = new ConcurrentDictionary<string, byte>(
                existingFiles.Select(f => new KeyValuePair<string, byte>(f.Name.ToLower(), 0)));

            try
            {
                var items = selectedItems.Cast<ImageItem>().ToList();
                int total = items.Count;
                int processed = 0;

                using var semaphore = new SemaphoreSlim(8);

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (string.IsNullOrEmpty(item.ImagePath)) return;

                        StorageFile file;
                        try { file = await StorageFile.GetFileFromPathAsync(item.ImagePath); }
                        catch (Exception ex) { Debug.WriteLine($"[Rename] GetFile Error: {ex.Message}"); return; }

                        var props = await file.Properties.GetImagePropertiesAsync();
                        DateTimeOffset targetTime = props.DateTaken;

                        // [逻辑修复] 放宽年份限制到 1900
                        bool isInvalidTime = targetTime == DateTimeOffset.MinValue || targetTime.Year < 1900;

                        if (isInvalidTime)
                        {
                            // 修改为调用注入的 Service
                            targetTime = await _renameService.SmartParseDateAsync(file);
                        }

                        if (targetTime == DateTimeOffset.MinValue || targetTime.Year < 1900)
                        {
                            Interlocked.Increment(ref skippedCount);
                            return;
                        }

                        string extension = file.FileType;
                        bool isTimeZero = targetTime.TimeOfDay == TimeSpan.Zero;
                        string dateFormat = isTimeZero ? "yyyy-MM-dd" : "yyyy-MM-dd_HH-mm-ss";
                        string dateStr = targetTime.ToString(dateFormat);
                        string baseName = dateStr;
                        string newName = baseName + extension;

                        if (!newName.Equals(file.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            int counter = 1;
                            while (true)
                            {
                                string lowerName = newName.ToLower();
                                if (allFilenames.TryAdd(lowerName, 0))
                                {
                                    break;
                                }
                                newName = $"{baseName}_{counter:D2}{extension}";
                                counter++;
                            }
                        }

                        previewList.Add(new RenamePreviewItem
                        {
                            File = file,
                            OriginalName = file.Name,
                            NewName = newName
                        });

                        int current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0)
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                ProgressValue = current;
                                ProgressMax = total;
                                StatusMainText = $"正在分析... {current}/{total}";
                            });
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                SetBusy(false);

                var sortedPreview = previewList.OrderBy(x => x.NewName).ToList();

                if (sortedPreview.Count == 0)
                {
                    string msg = "未找到包含有效时间信息的图片。";
                    if (skippedCount > 0)
                    {
                        msg += $"\n\n已跳过 {skippedCount} 个文件。\n原因：无 Exif 信息，且文件名不包含清晰的日期格式。";
                    }
                    await _view.ShowTipAsync(msg);
                    return;
                }

                bool confirm = await _view.ShowRenamePreviewAsync(sortedPreview, skippedCount);

                if (confirm)
                {
                    await PerformRenameFiles(sortedPreview);
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
                Debug.WriteLine($"[Rename] Process Error: {ex}");
                await _view.ShowTipAsync($"预处理失败: {ex.Message}");
            }
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

                // ✅ 优雅地使用 IProgress 接收底层服务传来的进度更新，并分发到 UI 线程
                var progress = new Progress<(double Value, string Message, string Detail)>(p =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        ProgressValue = p.Value;
                        if (!string.IsNullOrEmpty(p.Message)) StatusMainText = p.Message;
                        if (p.Detail != null) StatusDetailText = p.Detail;
                    });
                });

                // ✅ 核心魔法：一行代码调用分离出去的扫描算法
                var finalDuplicates = await _deduplicationService.FindDuplicatesAsync(_currentFolder, progress, token);

                SetBusy(false);
                if (token.IsCancellationRequested) return;

                // 处理扫描结果
                if (finalDuplicates.Count > 0)
                {
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
                Debug.WriteLine($"[Scan] Critical Error: {ex}");
                await _view.ShowTipAsync($"扫描中断: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DeleteSelected(IList<object> selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0) return;

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
                    catch (Exception ex) { Debug.WriteLine($"[Delete] GetFile Error: {ex.Message}"); }
                }
                await PerformDeleteFiles(files);
            }
        }

        private async Task PerformRenameFiles(List<RenamePreviewItem> items)
        {
            SetBusy(true, "正在重命名...", 0, items.Count);
            int successCount = 0;
            int failCount = 0;

            await Task.Run(async () =>
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    try
                    {
                        if (!item.OriginalName.Equals(item.NewName, StringComparison.OrdinalIgnoreCase))
                        {
                            await item.File.RenameAsync(item.NewName, NameCollisionOption.GenerateUniqueName);
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Debug.WriteLine($"[Rename] Execute Error ({item.OriginalName}): {ex.Message}");
                    }

                    if ((i + 1) % 20 == 0 || i == items.Count - 1)
                    {
                        int current = i + 1;
                        _dispatcherQueue.TryEnqueue(() => {
                            ProgressValue = current;
                            StatusMainText = $"正在重命名... {current}/{items.Count}";
                        });
                    }
                }
            });

            SetBusy(false);
            if (_currentFolder != null) await LoadFolderContentAsync(_currentFolder);
            string msg = $"重命名完成。\n成功: {successCount} 个\n失败: {failCount} 个";
            if (failCount > 0) msg += "\n(失败原因通常是文件被占用)";
            await _view.ShowTipAsync(msg);
        }

        // 在 BlueSapphire/ViewModels/MediaManagerViewModel.cs 中

        private async Task LoadFolderContentAsync(StorageFolder folder)
        {
            SetBusy(true, "正在扫描文件...");
            Images = null;
            _cachedAllItems.Clear();
            PathText = $"PATH: {folder.Path}";
            IsEmptyStateVisible = false;

            // [优化] 定义局部变量用于统计加载失败的文件数
            int skippedCount = 0;

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
                    catch (Exception ex)
                    {
                        // [优化] 记录日志并增加跳过计数
                        Debug.WriteLine($"[Load] File Error ({file.Name}): {ex.Message}");
                        skippedCount++;
                    }
                }

                CountText = $"ITEMS: {_cachedAllItems.Count}";
                RefreshViewFromCache();

                // [优化] 如果有跳过的文件，向用户显示提示 (Toast)
                if (skippedCount > 0)
                {
                    // 这里的文案可以根据需要调整，例如："部分文件加载失败"
                    await _view.ShowTipAsync($"加载完成，但有 {skippedCount} 个文件因无法读取而被跳过。");

                    // 可选：也可以同时在状态栏保留一条记录
                    // StatusDetailText = $"警告：{skippedCount} 个文件加载失败";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Load] Folder Error: {ex}");
                await _view.ShowTipAsync($"读取失败: {ex.Message}");
                IsEmptyStateVisible = true;
            }
            finally { SetBusy(false); }
        }

        // ... (RefreshViewFromCache, PerformDeleteFiles, SetBusy, UpdateProgress 保持原样，仅添加了 try-catch 日志，此处为节省篇幅略去)
        private void RefreshViewFromCache()
        {
            if (_cachedAllItems.Count == 0) return;
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
                        lock (_cachedAllItems)
                        {
                            var cacheItem = _cachedAllItems.FirstOrDefault(x => x.ImagePath == file.Path);
                            if (cacheItem != null) _cachedAllItems.Remove(cacheItem);
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[Delete] Error: {ex.Message}"); }
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
            RefreshViewFromCache();
            await _view.ShowTipAsync($"清理完成，共删除 {deletedCount} 个文件。");
        }

        private void SetBusy(bool busy, string text = "", double val = 0, double max = 100)
        {
            _dispatcherQueue.TryEnqueue(() => {
                IsBusy = busy;
                StatusMainText = text;
                StatusDetailText = "";
                IsProgressVisible = busy;
                ProgressValue = val;
                ProgressMax = max;
            });
        }

        private void UpdateProgress(double val, double max, string mainText)
        {
            _dispatcherQueue.TryEnqueue(() => {
                ProgressValue = val;
                ProgressMax = max;
                StatusMainText = mainText;
            });
        }
    }
}