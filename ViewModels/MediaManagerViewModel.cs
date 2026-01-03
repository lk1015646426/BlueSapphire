using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Concurrent; // 用于线程安全集合
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // 用于文件名正则解析
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

        // [功能二] 批量重命名命令 (Exif > 智能文件名解析 > 跳过)
        [RelayCommand]
        private async Task RenameSelected(IList<object> selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要重命名的图片");
                return;
            }

            if (_currentFolder == null) return;

            // 1. 准备阶段
            SetBusy(true, "正在并发分析文件属性...");
            var previewList = new ConcurrentBag<RenamePreviewItem>(); // 线程安全集合
            int skippedCount = 0;

            // 获取现有文件名用于冲突检测 (线程安全字典)
            var existingFiles = await _currentFolder.GetFilesAsync();
            var allFilenames = new ConcurrentDictionary<string, byte>(
                existingFiles.Select(f => new KeyValuePair<string, byte>(f.Name.ToLower(), 0)));

            try
            {
                var items = selectedItems.Cast<ImageItem>().ToList();
                int total = items.Count;
                int processed = 0;

                // [性能优化] 使用 SemaphoreSlim 限制并发数为 50
                using var semaphore = new SemaphoreSlim(50);

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (string.IsNullOrEmpty(item.ImagePath)) return;

                        StorageFile file;
                        try { file = await StorageFile.GetFileFromPathAsync(item.ImagePath); }
                        catch { return; }

                        // --- 核心逻辑开始 ---

                        // A. 优先尝试读取 Exif 拍摄时间
                        var props = await file.Properties.GetImagePropertiesAsync();
                        DateTimeOffset targetTime = props.DateTaken;

                        // [校验] 拦截 1601/1/1 等无效时间
                        bool isInvalidTime = targetTime == DateTimeOffset.MinValue || targetTime.Year < 1980;

                        // B. 回退：智能解析
                        if (isInvalidTime)
                        {
                            targetTime = await SmartParseDateAsync(file);
                        }

                        // C. 最终判决：如果还是无效，或者解析失败(比如 mmexport 文件)
                        // 则 increment skippedCount 并直接返回
                        if (targetTime == DateTimeOffset.MinValue || targetTime.Year < 1980)
                        {
                            Interlocked.Increment(ref skippedCount);
                            return; // 跳过此文件，不加入 previewList
                        }

                        // --- 核心逻辑结束 ---

                        string extension = file.FileType;

                        // [格式] 0点用 yyyy-MM-dd，有时间用 yyyy-MM-dd_HH-mm-ss
                        bool isTimeZero = targetTime.TimeOfDay == TimeSpan.Zero;
                        string dateFormat = isTimeZero ? "yyyy-MM-dd" : "yyyy-MM-dd_HH-mm-ss";

                        string dateStr = targetTime.ToString(dateFormat);
                        string baseName = dateStr;
                        string newName = baseName + extension;

                        // [冲突检测]
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

                                // 名字冲突，添加序号
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
                        if (current % 20 == 0)
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

                // 如果没有文件可以重命名，且有跳过的文件
                if (sortedPreview.Count == 0)
                {
                    string msg = "未找到包含有效时间信息的图片。";
                    if (skippedCount > 0)
                    {
                        msg += $"\n\n已跳过 {skippedCount} 个文件。\n原因：无 Exif 信息，且文件名不包含清晰的日期格式（如 mmexport 等随机命名）。";
                    }
                    await _view.ShowTipAsync(msg);
                    return;
                }

                // 显示预览，同时告知跳过了多少个
                bool confirm = await _view.ShowRenamePreviewAsync(sortedPreview, skippedCount);

                if (confirm)
                {
                    await PerformRenameFiles(sortedPreview);
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
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

                        if (++processed % 50 == 0)
                            _dispatcherQueue.TryEnqueue(() => ProgressValue = processed);
                    }
                    return sizeGroups.Values.Where(g => g.Count > 1).ToList();
                });

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

        // [核心改进] 智能解析文件名日期
        // 关键点：加入了 (?<!\d) 断言，防止匹配到长数字中间的年份
        private static async Task<DateTimeOffset> SmartParseDateAsync(StorageFile file)
        {
            string fileName = file.Name;

            // Tier 1: 尝试提取【完整时间】
            // (?<!\d) 意味着: 20xx的前面不能是数字！
            // 这能完美解决 mmexport172043... 中匹配到 2043 的问题
            var fullTimePattern = new Regex(
                @"(?<!\d)(?<year>20\d{2})[-_.\s]?(?<month>0[1-9]|1[0-2])[-_.\s]?(?<day>0[1-9]|[12]\d|3[01])[-_.\sT]+(?<hour>[01]\d|2[0-3])[-_:.]?(?<minute>[0-5]\d)[-_:.]?(?<second>[0-5]\d)?");

            var match = fullTimePattern.Match(fileName);
            if (match.Success)
            {
                return ParseRegexMatch(match);
            }

            // Tier 2: 尝试提取【仅日期】
            // 同样加入 (?<!\d) 保护
            var dateOnlyPattern = new Regex(
                @"(?<!\d)(?<year>20\d{2})[-_.\s年]?(?<month>0?[1-9]|1[0-2])[-_.\s月]?(?<day>0?[1-9]|[12]\d|3[01])[日号]?");

            match = dateOnlyPattern.Match(fileName);
            if (match.Success)
            {
                return ParseRegexMatch(match).Date;
            }

            // mmexport 等乱码文件将在这里返回 MinValue，然后在外部被跳过
            return DateTimeOffset.MinValue;
        }

        private static DateTime ParseRegexMatch(Match match)
        {
            int y = int.Parse(match.Groups["year"].Value);
            int m = int.Parse(match.Groups["month"].Value);
            int d = int.Parse(match.Groups["day"].Value);

            int h = 0, min = 0, s = 0;
            if (match.Groups["hour"].Success) h = int.Parse(match.Groups["hour"].Value);
            if (match.Groups["minute"].Success) min = int.Parse(match.Groups["minute"].Value);
            if (match.Groups["second"].Success) s = int.Parse(match.Groups["second"].Value);

            return new DateTime(y, m, d, h, min, s);
        }

        // [新增] 执行重命名逻辑 (支持节流更新)
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
                    catch
                    {
                        failCount++;
                    }

                    // 进度节流
                    if ((i + 1) % 20 == 0 || i == items.Count - 1)
                    {
                        int current = i + 1;
                        _dispatcherQueue.TryEnqueue(() =>
                        {
                            ProgressValue = current;
                            StatusMainText = $"正在重命名... {current}/{items.Count}";
                        });
                    }
                }
            });

            SetBusy(false);

            if (_currentFolder != null)
            {
                await LoadFolderContentAsync(_currentFolder);
            }

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
                    catch { }

                    Interlocked.Increment(ref deletedCount);
                    if (deletedCount % 10 == 0)
                    {
                        _dispatcherQueue.TryEnqueue(() =>
                        {
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
            _dispatcherQueue.TryEnqueue(() =>
            {
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
            _dispatcherQueue.TryEnqueue(() =>
            {
                ProgressValue = val;
                ProgressMax = max;
                StatusMainText = mainText;
            });
        }
    }
}