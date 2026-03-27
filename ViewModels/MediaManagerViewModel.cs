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
// [极客优化] 已彻底移除 System.Diagnostics，告别低效的 Debug.WriteLine 阻塞写入
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
        private IMediaViewInteraction _view = null!;
        private DispatcherQueue _dispatcherQueue = null!;
        private List<ImageItem> _cachedAllItems = new List<ImageItem>();
        private CancellationTokenSource? _globalCts;
        private StorageFolder? _currentFolder;

        private IncrementalLoadingCollection<ImageItem>? _images;
        public IncrementalLoadingCollection<ImageItem>? Images
        {
            get => _images;
            set => SetProperty(ref _images, value);
        }

        private string _statusMainText = "READY";
        public string StatusMainText
        {
            get => _statusMainText;
            set => SetProperty(ref _statusMainText, value);
        }

        private string _statusDetailText = "";
        public string StatusDetailText
        {
            get => _statusDetailText;
            set => SetProperty(ref _statusDetailText, value);
        }

        private string _pathText = "PATH: NULL";
        public string PathText
        {
            get => _pathText;
            set => SetProperty(ref _pathText, value);
        }

        private string _countText = "ITEMS: 0";
        public string CountText
        {
            get => _countText;
            set => SetProperty(ref _countText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private bool _isProgressVisible;
        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => SetProperty(ref _isProgressVisible, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private double _progressMax = 100;
        public double ProgressMax
        {
            get => _progressMax;
            set => SetProperty(ref _progressMax, value);
        }

        private bool _isEmptyStateVisible = true;
        public bool IsEmptyStateVisible
        {
            get => _isEmptyStateVisible;
            set => SetProperty(ref _isEmptyStateVisible, value);
        }

        private string _currentSortField = "Name";
        public string CurrentSortField
        {
            get => _currentSortField;
            set => SetProperty(ref _currentSortField, value);
        }

        private bool _isSortDescending;
        public bool IsSortDescending
        {
            get => _isSortDescending;
            set => SetProperty(ref _isSortDescending, value);
        }

        private readonly MediaRenameService _renameService;
        private readonly BlueSapphire.Services.MediaDeduplicationService _deduplicationService;
        private readonly NativeFileService _nativeFileService; // ✅ 回收站服务

        public MediaManagerViewModel(
            MediaRenameService renameService,
            BlueSapphire.Services.MediaDeduplicationService deduplicationService,
            NativeFileService nativeFileService)
        {
            _renameService = renameService;
            _deduplicationService = deduplicationService;
            _nativeFileService = nativeFileService;
        }

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

            var existingFiles = await _currentFolder.GetFilesAsync();
            var allFilenames = new ConcurrentDictionary<string, byte>(
                existingFiles.Select(f => new KeyValuePair<string, byte>(f.Name.ToLower(), 0)));

            try
            {
                var items = selectedItems.Cast<ImageItem>().ToList();
                int total = items.Count;
                int processed = 0;
                int ghostCount = 0; // ✅ 新增：幽灵文件计数器

                using var semaphore = new SemaphoreSlim(8);
                var noDateFiles = new ConcurrentBag<StorageFile>();

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (string.IsNullOrEmpty(item.ImagePath)) return;

                        StorageFile file;
                        try
                        {
                            file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                        }
                        catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex.HResult == unchecked((int)0x80070002))
                        {
                            // ✅ 幽灵文件容错
                            Interlocked.Increment(ref ghostCount); // 记录幽灵数量
                            _dispatcherQueue.TryEnqueue(() => {
                                lock (_cachedAllItems)
                                {
                                    var ghost = _cachedAllItems.FirstOrDefault(x => x.ImagePath == item.ImagePath);
                                    if (ghost != null) _cachedAllItems.Remove(ghost);
                                }
                                RefreshViewFromCache();
                            });
                            return;
                        }
                        catch (Exception ex)
                        {
                            MatrixLogService.LogError($"Rename_GetFile ({item.FileName ?? item.ImagePath})", ex);
                            return;
                        }

                        var props = await file.Properties.GetImagePropertiesAsync();
                        DateTimeOffset targetTime = props.DateTaken;

                        if (targetTime == DateTimeOffset.MinValue || targetTime.Year < 1900)
                        {
                            targetTime = await _renameService.SmartParseDateAsync(file);
                        }

                        if (targetTime == DateTimeOffset.MinValue || targetTime.Year < 1900)
                        {
                            noDateFiles.Add(file);
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
                                if (allFilenames.TryAdd(lowerName, 0)) break;
                                newName = $"{baseName}_{counter:D2}{extension}";
                                counter++;
                            }
                        }

                        previewList.Add(new RenamePreviewItem { File = file, OriginalName = file.Name, NewName = newName });

                        int current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0)
                        {
                            _dispatcherQueue.TryEnqueue(() => { ProgressValue = current; ProgressMax = total; StatusMainText = $"正在分析... {current}/{total}"; });
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                SetBusy(false);

                // ✅ 专属拦截：如果发现了幽灵文件，进行专属提示并安全退出
                if (ghostCount > 0)
                {
                    await _view.ShowTipAsync($"已自动清理 {ghostCount} 个在外部被删除的失效文件。");
                    // 如果全是幽灵文件，或者剩下的全被幽灵顶了，直接退出
                    if (previewList.IsEmpty && noDateFiles.IsEmpty) return;
                }

                // 处理无日期的文件
                var failedFiles = noDateFiles.ToList();
                if (failedFiles.Count > 0)
                {
                    string? fallback = await _view.ShowInputPromptAsync(
                        "发现无日期信息的文件",
                        $"有 {failedFiles.Count} 个文件缺失拍摄时间。\n请输入一个自定义前缀（例如：照片、壁纸），程序将自动为它们顺序编号。如果不填将跳过它们。",
                        "未命名照片");

                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        int counter = 1;
                        foreach (var file in failedFiles)
                        {
                            string extension = file.FileType;
                            string newName = $"{fallback}_{counter:D2}{extension}";

                            while (allFilenames.ContainsKey(newName.ToLower()))
                            {
                                counter++;
                                newName = $"{fallback}_{counter:D2}{extension}";
                            }
                            allFilenames.TryAdd(newName.ToLower(), 0);
                            previewList.Add(new RenamePreviewItem { File = file, OriginalName = file.Name, NewName = newName });
                            counter++;
                        }
                    }
                }

                var sortedPreview = previewList.OrderBy(x => x.NewName).ToList();

                if (sortedPreview.Count == 0)
                {
                    // 只有在没幽灵、没失败文件的情况下，才报这个兜底错
                    if (ghostCount == 0 && failedFiles.Count == 0)
                    {
                        await _view.ShowTipAsync("没有生成任何需要重命名的任务。");
                    }
                    return;
                }

                bool confirm = await _view.ShowRenamePreviewAsync(sortedPreview, 0);
                if (confirm) await PerformRenameFiles(sortedPreview);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MatrixLogService.LogError("Rename_Process_Critical", ex);
                await _view.ShowTipAsync($"预处理失败: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ScanDuplicates(string mode) // ✅ 增加 mode 参数
        {
            if (_currentFolder == null) { await _view.ShowTipAsync("请先导入文件夹"); return; }
            if (IsBusy) return;

            _globalCts?.Cancel();
            _globalCts = new CancellationTokenSource();
            var token = _globalCts.Token;

            try
            {
                string modeName = mode == "Similar" ? "智能扫描" : "精确扫描";
                SetBusy(true, $"正在初始化{modeName}...", 0, 100);

                var progress = new Progress<(double Value, string Message, string Detail)>(p =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        ProgressValue = p.Value;
                        if (!string.IsNullOrEmpty(p.Message)) StatusMainText = p.Message;
                        if (p.Detail != null) StatusDetailText = p.Detail;
                    });
                });

                // ✅ 根据传入的模式调用不同引擎
                List<List<StorageFile>> finalDuplicates;
                if (mode == "Similar")
                {
                    finalDuplicates = await _deduplicationService.FindSimilarImagesAsync(_currentFolder, progress, token);
                }
                else
                {
                    finalDuplicates = await _deduplicationService.FindDuplicatesAsync(_currentFolder, progress, token);
                }

                SetBusy(false);
                if (token.IsCancellationRequested) return;

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
                    await _view.ShowTipAsync(mode == "Similar" ? "扫描完成，未发现相似照片。" : "扫描完成，未发现内容重复的文件。");
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MatrixLogService.LogError("Scan_Duplicates_Critical", ex);
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
                int ghostCount = 0; // ✅ 新增幽灵文件计数

                foreach (var item in selectedItems.Cast<ImageItem>())
                {
                    try
                    {
                        if (item.ImagePath != null)
                            files.Add(await StorageFile.GetFileFromPathAsync(item.ImagePath));
                    }
                    catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex.HResult == unchecked((int)0x80070002))
                    {
                        // ✅ 幽灵文件容错
                        ghostCount++;
                        _dispatcherQueue.TryEnqueue(() => {
                            lock (_cachedAllItems)
                            {
                                var ghost = _cachedAllItems.FirstOrDefault(x => x.ImagePath == item.ImagePath);
                                if (ghost != null) _cachedAllItems.Remove(ghost);
                            }
                            RefreshViewFromCache();
                        });
                    }
                    catch (Exception ex)
                    {
                        MatrixLogService.LogError("Delete_GetFile", ex);
                    }
                }

                // ✅ 专属拦截提示
                if (ghostCount > 0)
                {
                    await _view.ShowTipAsync($"已自动清理 {ghostCount} 个在外部被删除的失效文件。");
                }

                if (files.Count > 0)
                {
                    await PerformDeleteFiles(files);
                }
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
                        MatrixLogService.LogError($"Rename_Execute ({item.OriginalName})", ex);
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
            // ✅ 极客优化：局部内存更新，拒绝全局 I/O 扫描
            lock (_cachedAllItems)
            {
                foreach (var item in items)
                {
                    if (!item.OriginalName.Equals(item.NewName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 在缓存中找到对应的旧项，直接更新名字和新路径
                        var cacheItem = _cachedAllItems.FirstOrDefault(x => x.FileName == item.OriginalName);
                        if (cacheItem != null)
                        {
                            cacheItem.FileName = item.NewName;
                            cacheItem.ImagePath = item.File.Path; // 文件路径也变了，必须更新
                        }
                    }
                }
            }
            RefreshViewFromCache(); // 重新触发视图排序和绑定
            string msg = $"重命名完成。\n成功: {successCount} 个\n失败: {failCount} 个";
            if (failCount > 0) msg += "\n(失败原因通常是文件被占用)";
            await _view.ShowTipAsync(msg);
        }

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

                // ✅ 极速并发 I/O 架构
                var concurrentBag = new ConcurrentBag<ImageItem>();
                using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

                var tasks = files.Select(async file =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var props = await file.GetBasicPropertiesAsync();
                        concurrentBag.Add(new ImageItem
                        {
                            FileName = file.Name,
                            ImagePath = file.Path,
                            DateCreated = file.DateCreated,
                            FileSize = props.Size
                        });
                    }
                    catch (Exception ex)
                    {
                        // 摒弃 Interlocked 原子操作，仅记录日志，让底层纯净执行
                        MatrixLogService.LogError($"Load_File ({file.Name})", ex);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                lock (_cachedAllItems)
                {
                    _cachedAllItems.AddRange(concurrentBag);
                }

                CountText = $"ITEMS: {_cachedAllItems.Count}";
                RefreshViewFromCache();

                // ✅ 最优解：直接通过总文件数与并发抓取成果的差值计算跳过数量
                int skippedCount = files.Count - concurrentBag.Count;
                if (skippedCount > 0)
                {
                    await _view.ShowTipAsync($"加载完成，但有 {skippedCount} 个文件因无法读取或损坏而被跳过。");
                }
            }
            catch (Exception ex)
            {
                MatrixLogService.LogError("Load_Folder_Critical", ex);
                await _view.ShowTipAsync($"读取失败: {ex.Message}");
                IsEmptyStateVisible = true;
            }
            finally { SetBusy(false); }
        }

        private void RefreshViewFromCache()
        {
            if (_cachedAllItems.Count == 0) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                // ✅ 加锁提取数据快照，防止与后台任务发生读写冲突
                List<ImageItem> snapshot;
                lock (_cachedAllItems)
                {
                    snapshot = _cachedAllItems.ToList();
                }

                IEnumerable<ImageItem> query = snapshot;
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
            SetBusy(true, "正在移至回收站...", 0, files.Count);
            int deletedCount = 0;

            await Task.Run(async () =>
            {
                // ✅ 引入并发架构加速大批量文件的回收站操作
                using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

                var tasks = files.Select(async file =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        bool success = await _nativeFileService.MoveToRecycleBinAsync(file.Path);
                        if (success)
                        {
                            lock (_cachedAllItems)
                            {
                                var cacheItem = _cachedAllItems.FirstOrDefault(x => x.ImagePath == file.Path);
                                if (cacheItem != null) _cachedAllItems.Remove(cacheItem);
                            }
                            Interlocked.Increment(ref deletedCount);
                        }
                        else
                        {
                            throw new Exception("移动到回收站失败，可能被占用或权限不足");
                        }
                    }
                    catch (Exception ex)
                    {
                        MatrixLogService.LogError($"Delete_Execute ({file.Name})", ex);
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                    // 局部近似更新，避免主线程刷新堵塞
                    int current = deletedCount;
                    if (current % 10 == 0)
                    {
                        _dispatcherQueue.TryEnqueue(() => {
                            ProgressValue = current;
                            StatusMainText = $"正在移至回收站... ({current}/{files.Count})";
                        });
                    }
                });

                await Task.WhenAll(tasks);
            });

            SetBusy(false);
            CountText = $"ITEMS: {_cachedAllItems.Count}";
            RefreshViewFromCache();
            await _view.ShowTipAsync($"清理完成，共移至回收站 {deletedCount} 个文件。");
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