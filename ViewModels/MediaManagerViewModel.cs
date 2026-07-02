using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.ViewModels
{
    public partial class MediaManagerViewModel : ObservableObject
    {
        private readonly MediaRenameService _renameService;
        private readonly MediaDeduplicationService _deduplicationService;
        private readonly NativeFileService _nativeFileService;
        private readonly ImageProcessingService _imageProcessingService;
        private readonly ImageMetadataService _imageMetadataService;
        private readonly MediaTagService _mediaTagService;
        private readonly ILogger<MediaManagerViewModel> _logger;
        private readonly List<ImageItem> _cachedAllItems = new();

        private IMediaViewInteraction _view = null!;
        private DispatcherQueue? _dispatcherQueue;
        private StorageFolder? _currentFolder;
        private CancellationTokenSource? _globalCts;
        private List<ImageItem> _lastVisibleItems = new();
        private string? _lastImageOperationSummary;

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

        private string _statusDetailText = string.Empty;
        public string StatusDetailText
        {
            get => _statusDetailText;
            set => SetProperty(ref _statusDetailText, value);
        }

        private string _pathText = "-";
        public string PathText
        {
            get => _pathText;
            set => SetProperty(ref _pathText, value);
        }

        private string _countText = "0";
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

        private bool _hasImages;
        public bool HasImages
        {
            get => _hasImages;
            set => SetProperty(ref _hasImages, value);
        }

        private bool _isImageWorkspaceVisible;
        public bool IsImageWorkspaceVisible
        {
            get => _isImageWorkspaceVisible;
            set
            {
                if (SetProperty(ref _isImageWorkspaceVisible, value))
                {
                    OnPropertyChanged(nameof(IsModulePickerVisible));
                }
            }
        }

        public bool IsModulePickerVisible => !IsImageWorkspaceVisible;

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

        private string _imageQueueStatusText = "图片队列：空闲";
        public string ImageQueueStatusText
        {
            get => _imageQueueStatusText;
            set => SetProperty(ref _imageQueueStatusText, value);
        }

        private string _imageQueueDetailText = "等待图片任务。";
        public string ImageQueueDetailText
        {
            get => _imageQueueDetailText;
            set => SetProperty(ref _imageQueueDetailText, value);
        }

        public bool HasImageOperationResults => !string.IsNullOrWhiteSpace(_lastImageOperationSummary);
        public string LastImageOperationSummaryText => _lastImageOperationSummary ?? "暂无最近一次图片处理结果。";
        public string SortButtonText
        {
            get
            {
                string sortFieldName = CurrentSortField switch
                {
                    "Date" => "日期",
                    "Size" => "大小",
                    _ => "名称"
                };

                string direction = IsSortDescending ? "降序" : "升序";
                return $"{sortFieldName} · {direction}";
            }
        }

        public string EmptyStateText => "等待接入图片媒体库...";
        public string ContextModeText => "图片 · 上下文模式";
        public string TabStatusText => "TAB: 图片";
        public string EmptyStateIconGlyph => "\uE8B9";

        public MediaManagerViewModel(
            MediaRenameService renameService,
            MediaDeduplicationService deduplicationService,
            NativeFileService nativeFileService,
            ImageProcessingService imageProcessingService,
            ImageMetadataService imageMetadataService,
            MediaTagService mediaTagService,
            ILogger<MediaManagerViewModel> logger)
        {
            _renameService = renameService;
            _deduplicationService = deduplicationService;
            _nativeFileService = nativeFileService;
            _imageProcessingService = imageProcessingService;
            _imageMetadataService = imageMetadataService;
            _mediaTagService = mediaTagService;
            _logger = logger;
        }

        public void Initialize(IMediaViewInteraction view, DispatcherQueue dispatcherQueue)
        {
            _view = view;
            _dispatcherQueue = dispatcherQueue;
        }

        [RelayCommand]
        private void OpenImageWorkspace()
        {
            IsImageWorkspaceVisible = true;
        }

        [RelayCommand]
        private void ReturnToMediaHome()
        {
            IsImageWorkspaceVisible = false;
        }

        [RelayCommand]
        private async Task OpenFolder()
        {
            IsImageWorkspaceVisible = true;

            var folder = await _view.PickFolderAsync();
            if (folder == null)
            {
                return;
            }

            _currentFolder = folder;
            await LoadFolderContentAsync(folder);
        }

        [RelayCommand]
        private async Task OpenFiles()
        {
            IsImageWorkspaceVisible = true;

            var files = await _view.PickFilesAsync();
            if (files == null || files.Count == 0)
            {
                return;
            }

            // Set _currentFolder to the folder of the first file if possible
            if (files.Count > 0)
            {
                try
                {
                    _currentFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(files[0].Path));
                }
                catch { _currentFolder = null; }
            }

            await LoadFilesAsync(files, _currentFolder?.Path ?? "已选图片");
        }

        [RelayCommand]
        private async Task ChangeSort(string field)
        {
            CurrentSortField = field;
            OnPropertyChanged(nameof(SortButtonText));
            await RefreshViewFromCacheAsync();
        }

        [RelayCommand]
        private async Task ToggleSortDirection()
        {
            IsSortDescending = !IsSortDescending;
            OnPropertyChanged(nameof(SortButtonText));
            await RefreshViewFromCacheAsync();
        }

        [RelayCommand]
        private async Task ApplySort(string option)
        {
            string normalized = option ?? string.Empty;
            if (normalized.EndsWith("Desc", StringComparison.OrdinalIgnoreCase))
            {
                IsSortDescending = true;
            }
            else if (normalized.EndsWith("Asc", StringComparison.OrdinalIgnoreCase))
            {
                IsSortDescending = false;
            }

            if (normalized.StartsWith("Date", StringComparison.OrdinalIgnoreCase))
            {
                CurrentSortField = "Date";
            }
            else if (normalized.StartsWith("Size", StringComparison.OrdinalIgnoreCase))
            {
                CurrentSortField = "Size";
            }
            else
            {
                CurrentSortField = "Name";
            }

            OnPropertyChanged(nameof(SortButtonText));
            await RefreshViewFromCacheAsync();
        }

        [RelayCommand]
        private async Task RenameSelected(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要重命名的图片。");
                return;
            }

            SetBusy(true, "正在分析图片时间...", 0, items.Count);

            var candidates = new ConcurrentBag<RenameCandidate>();
            var unresolvedFiles = new ConcurrentBag<StorageFile>();
            var ghostPaths = new ConcurrentBag<string>();

            try
            {
                using var semaphore = new SemaphoreSlim(8);
                int processed = 0;

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var file = await TryGetStorageFileAsync(item.ImagePath);
                        if (file == null)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ImagePath))
                            {
                                ghostPaths.Add(item.ImagePath);
                            }

                            return;
                        }

                        var timestamp = await _renameService.ResolveBestTimestampAsync(file);
                        if (!_renameService.HasUsableTimestamp(timestamp))
                        {
                            unresolvedFiles.Add(file);
                            return;
                        }

                        candidates.Add(new RenameCandidate(file, file.Path, file.Name, BuildTimestampBaseName(timestamp)));

                        int current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0 || current == items.Count)
                        {
                            RunOnUi(() =>
                            {
                                ProgressValue = current;
                                ProgressMax = items.Count;
                                StatusMainText = $"正在分析... {current}/{items.Count}";
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Rename_Analyze ({FileName})", item.FileName ?? item.ImagePath);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                await RemoveGhostFilesAsync(ghostPaths);
                SetBusy(false);

                if (ghostPaths.Count > 0)
                {
                    await _view.ShowTipAsync($"已自动清理 {ghostPaths.Count} 个在外部被删除的失效文件。");
                }

                var reservations = BuildDirectoryNameReservations();
                ReleaseOriginalNames(reservations, items);

                var previewItems = BuildRenamePreviewItems(
                    candidates.OrderBy(candidate => candidate.OriginalPath, StringComparer.OrdinalIgnoreCase),
                    reservations);

                int skippedCount = unresolvedFiles.Count;
                if (unresolvedFiles.Count > 0)
                {
                    string? fallbackPrefix = await _view.ShowInputPromptAsync(
                        "发现缺失时间信息的图片",
                        $"有 {unresolvedFiles.Count} 个图片无法解析可靠时间。请输入一个前缀，程序会顺序编号；留空则跳过这些图片。",
                        "未命名图片");

                    if (!string.IsNullOrWhiteSpace(fallbackPrefix))
                    {
                        previewItems.AddRange(BuildFallbackPreviewItems(unresolvedFiles.ToList(), fallbackPrefix, reservations));
                        skippedCount = 0;
                    }
                }

                var sortedPreview = previewItems
                    .OrderBy(item => GetDirectoryPath(item.OriginalPath), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.NewName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (sortedPreview.Count == 0)
                {
                    await _view.ShowTipAsync(skippedCount > 0
                        ? "没有可执行的重命名任务，未解析出时间的图片已跳过。"
                        : "没有生成任何需要重命名的任务。");
                    return;
                }

                bool confirm = await _view.ShowRenamePreviewAsync(sortedPreview, skippedCount);
                if (confirm)
                {
                    await PerformRenameFilesAsync(sortedPreview);
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
                _logger.LogError(ex, "Rename_Process_Critical");
                await _view.ShowTipAsync($"重命名预处理失败: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelOperation()
        {
            var cts = _globalCts;
            if (cts != null && !cts.IsCancellationRequested)
            {
                StatusDetailText = "正在取消操作...";
                Task.Run(() =>
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch { }
                });
            }
        }

        [RelayCommand]
        private async Task ScanDuplicates(string mode)
        {
            if (_currentFolder == null)
            {
                await _view.ShowTipAsync("请先导入图片文件夹。");
                return;
            }

            if (IsBusy)
            {
                return;
            }

            _globalCts?.Cancel();
            _globalCts = new CancellationTokenSource();
            var token = _globalCts.Token;

            try
            {
                string modeName = string.Equals(mode, "Similar", StringComparison.OrdinalIgnoreCase) ? "智能扫描" : "精确扫描";
                SetBusy(true, $"正在初始化{modeName}...", 0, 100);

                var progress = new Progress<(double Value, string Message, string Detail)>(value =>
                {
                    RunOnUi(() =>
                    {
                        ProgressValue = value.Value;
                        if (!string.IsNullOrWhiteSpace(value.Message))
                        {
                            StatusMainText = value.Message;
                        }

                        StatusDetailText = value.Detail ?? string.Empty;
                    });
                });

                List<List<StorageFile>> finalDuplicates = string.Equals(mode, "Similar", StringComparison.OrdinalIgnoreCase)
                    ? await _deduplicationService.FindSimilarImagesAsync(_currentFolder, progress, token)
                    : await _deduplicationService.FindDuplicatesAsync(_currentFolder, progress, token);

                SetBusy(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (finalDuplicates.Count == 0)
                {
                    await _view.ShowTipAsync(string.Equals(mode, "Similar", StringComparison.OrdinalIgnoreCase)
                        ? "扫描完成，未发现相似图片。"
                        : "扫描完成，未发现内容重复的图片。");
                    return;
                }

                var filesToDelete = await _view.ShowDuplicateResultsAsync(finalDuplicates);
                if (filesToDelete.Count > 0)
                {
                    await PerformDeleteFilesAsync(filesToDelete);
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
                _logger.LogError(ex, "Scan_Duplicates_Critical");
                await _view.ShowTipAsync($"扫描中断: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DeleteSelected(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                return;
            }

            bool confirm = await _view.ShowDeleteConfirmationAsync(items.Count);
            if (!confirm)
            {
                return;
            }

            var files = new List<StorageFile>();
            var ghostPaths = new List<string>();

            foreach (var item in items)
            {
                var file = await TryGetStorageFileAsync(item.ImagePath);
                if (file == null)
                {
                    if (!string.IsNullOrWhiteSpace(item.ImagePath))
                    {
                        ghostPaths.Add(item.ImagePath);
                    }

                    continue;
                }

                files.Add(file);
            }

            await RemoveGhostFilesAsync(ghostPaths);
            if (ghostPaths.Count > 0)
            {
                await _view.ShowTipAsync($"已自动清理 {ghostPaths.Count} 个在外部被删除的失效文件。");
            }

            if (files.Count > 0)
            {
                await PerformDeleteFilesAsync(files);
            }
        }

        [RelayCommand]
        private async Task OpenSelectedLocation(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要打开位置的图片。");
                return;
            }

            var validPaths = new List<string>();
            var ghostPaths = new List<string>();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ImagePath))
                {
                    continue;
                }

                if (File.Exists(item.ImagePath))
                {
                    validPaths.Add(item.ImagePath);
                }
                else
                {
                    ghostPaths.Add(item.ImagePath);
                }
            }

            await RemoveGhostFilesAsync(ghostPaths);

            if (validPaths.Count == 0)
            {
                await _view.ShowTipAsync("未找到可打开的有效图片，已自动清理失效项。");
                return;
            }

            if (validPaths.Count == 1)
            {
                bool success = await _nativeFileService.RevealInExplorerAsync(validPaths[0]);
                await _view.ShowTipAsync(success ? "已定位到图片所在位置。" : "打开位置失败，请检查资源管理器是否可用。");
                return;
            }

            var folderPaths = validPaths
                .Select(GetDirectoryPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int openedCount = 0;
            foreach (var folderPath in folderPaths.Take(3))
            {
                if (await _nativeFileService.OpenFolderAsync(folderPath))
                {
                    openedCount++;
                }
            }

            await _view.ShowTipAsync(openedCount == 0
                ? "打开位置失败，请检查资源管理器是否可用。"
                : folderPaths.Count > 3
                    ? $"已打开 {openedCount} 个所在文件夹。为避免刷屏，仅打开前 3 个不同目录。"
                    : $"已打开 {openedCount} 个所在文件夹。");
        }

        [RelayCommand]
        private async Task EditSelectedMediaTags(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要打标签的图片。");
                return;
            }

            string defaultText = BuildSharedTagInput(items);
            string? tagInput = await _view.ShowInputPromptAsync(
                "管理自定义标签",
                $"将为所选 {items.Count} 张图片写入自定义标签；使用逗号分隔，留空表示清空。",
                defaultText);

            if (tagInput == null)
            {
                return;
            }

            var tags = _mediaTagService.ParseTags(tagInput);
            SetImageQueueState("图片队列：准备中", $"已加入 {items.Count} 张图片，准备更新自定义标签。");
            SetBusy(true, "正在更新自定义标签...", 0, items.Count);

            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (string.IsNullOrWhiteSpace(item.ImagePath))
                {
                    failCount++;
                    continue;
                }

                RunOnUi(() =>
                {
                    ProgressValue = i + 1;
                    ProgressMax = items.Count;
                    StatusMainText = $"正在更新自定义标签... {i + 1}/{items.Count}";
                    StatusDetailText = item.FileName ?? item.ImagePath;
                    SetImageQueueState("图片队列：处理中", $"正在更新标签：{item.FileName}");
                });

                var result = await _mediaTagService.ReplaceTagsAsync(item.ImagePath, tags);
                if (result.Success)
                {
                    successCount++;
                    item.CustomTags = tags;
                }
                else
                {
                    failCount++;
                }
            }

            SetBusy(false);
            SetImageQueueState("图片队列：已完成", $"自定义标签更新完成。成功 {successCount}，失败 {failCount}。");
            await _view.ShowTipAsync($"自定义标签更新完成。\n成功: {successCount} 张\n失败: {failCount} 张");
        }

        [RelayCommand]
        private async Task ShowLastImageOperationResults()
        {
            await _view.ShowTipAsync(LastImageOperationSummaryText);
        }

        [RelayCommand]
        private async Task OpenFormatConvertDialog(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var filePaths = items.Select(x => x.ImagePath).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList();
            var options = await _view.ShowFormatConvertDialogAsync(filePaths);
            if (options == null) return;

            string targetName = _imageProcessingService.GetTargetDisplayName(options.TargetFormat);
            string targetExtension = _imageProcessingService.GetTargetExtension(options.TargetFormat);
            
            var filteredItems = items
                .Where(item => !string.Equals(Path.GetExtension(item.FileName), targetExtension, StringComparison.OrdinalIgnoreCase) || options.Quality < 0.95)
                .ToList();

            if (filteredItems.Count == 0)
            {
                await _view.ShowTipAsync("源文件已经是目标格式，无需转换。");
                return;
            }

            await RunImageOperationAndPresentAsync(
                filteredItems,
                $"正在转换图片为 {targetName}...",
                $"图片转 {targetName}",
                $"已加入 {filteredItems.Count} 张图片，目标：{targetName}",
                (_, _, item) => $"正在转换为 {targetName}：{item.FileName}",
                (path, token) => _imageProcessingService.ConvertAsync(path, options, token));
        }

        [RelayCommand]
        private async Task OpenAdvancedEditorDialog(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var previewPaths = new List<string>();
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.ImagePath) && MediaFileCatalog.IsImage(item.ImagePath))
                {
                    previewPaths.Add(item.ImagePath);
                }
            }

            if (previewPaths.Count == 0)
            {
                await _view.ShowTipAsync("所选文件中没有受支持的图片格式。");
                return;
            }

            var options = await _view.ShowAdvancedEditorDialogAsync(previewPaths);
            if (options == null) return;

            await RunImageOperationAndPresentAsync(
                items,
                $"正在进行高级编辑...",
                $"高级图片编辑",
                $"已加入 {items.Count} 张图片进行处理",
                (_, _, item) => $"正在处理：{item.FileName}",
                (path, token) => _imageProcessingService.ProcessAdvancedAsync(path, options, token));
        }

        [RelayCommand]
        private async Task OpenEnhanceDialog(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var options = await _view.ShowEnhanceDialogAsync(items.Count == 1 ? items[0].ImagePath : null);
            if (options == null) return;

            await RunImageOperationAndPresentAsync(
                items,
                $"正在增强图片...",
                $"AI 增强",
                $"已加入 {items.Count} 张图片，图片增强",
                (_, _, item) => $"正在增强：{item.FileName}",
                (path, token) => _imageProcessingService.EnhanceAsync(path, options, token));
        }

        private async Task RunImageOperationAndPresentAsync(
            List<ImageItem> items,
            string busyText,
            string operationName,
            string queueReadyText,
            Func<int, int, ImageItem, string> buildQueueDetailText,
            Func<string, CancellationToken, Task<ImageProcessResult>> processAsync)
        {
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var result = await RunImageBatchOperationAsync(
                items,
                busyText,
                operationName,
                queueReadyText,
                buildQueueDetailText,
                processAsync);

            CacheImageOperationSummary(result.SummaryText);
            await _view.ShowTipAsync(result.SummaryText);
        }

        private async Task<ImageOperationBatchResult> RunImageBatchOperationAsync(
            List<ImageItem> items,
            string busyText,
            string operationName,
            string queueReadyText,
            Func<int, int, ImageItem, string> buildQueueDetailText,
            Func<string, CancellationToken, Task<ImageProcessResult>> processAsync)
        {
            _globalCts?.Cancel();
            _globalCts = new CancellationTokenSource();
            var token = _globalCts.Token;

            SetImageQueueState("图片队列：准备中", queueReadyText);
            SetBusy(true, busyText, 0, items.Count);

            int successCount = 0;
            int failCount = 0;
            int skippedCount = 0;
            int processedCount = 0;
            var messages = new List<string>();
            var ghostPaths = new List<string>();

            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

            try
            {
                var tasks = items.Select(item => Task.Run(async () =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        await semaphore.WaitAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        int current = Interlocked.Increment(ref processedCount);
                        RunOnUi(() =>
                        {
                            ProgressValue = current;
                            ProgressMax = items.Count;
                            StatusMainText = $"{busyText} {current}/{items.Count}";
                            StatusDetailText = item.FileName ?? string.Empty;
                            SetImageQueueState($"图片队列：{current}/{items.Count}", buildQueueDetailText(current, items.Count, item));
                        });

                        var file = await TryGetStorageFileAsync(item.ImagePath);
                        if (file == null)
                        {
                            Interlocked.Increment(ref skippedCount);
                            if (!string.IsNullOrWhiteSpace(item.ImagePath))
                            {
                                lock (ghostPaths)
                                {
                                    ghostPaths.Add(item.ImagePath);
                                }
                            }

                            return;
                        }

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        ImageProcessResult result;
                        try
                        {
                            result = await processAsync(file.Path, token);
                        }
                        catch (OperationCanceledException)
                        {
                            lock (messages)
                            {
                                messages.Add($"{file.Name}: 操作已被用户取消。");
                            }
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{OperationName} ({FileName})", operationName, file.Name);
                            result = ImageProcessResult.Failed(file.Path, ex.Message);
                        }

                        if (result.Success)
                        {
                            Interlocked.Increment(ref successCount);
                            if (!string.IsNullOrWhiteSpace(result.OutputPath))
                            {
                                await TrackOutputPathAsync(result.OutputPath);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref failCount);
                        }

                        if (!string.IsNullOrWhiteSpace(result.Message))
                        {
                            lock (messages)
                            {
                                messages.Add($"{file.Name}: {result.Message}");
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, token));

                await Task.WhenAll(tasks);
            }
            catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
            {
                // Cancelled, message handled below
            }
            finally
            {
                SetBusy(false);
            }

            if (token.IsCancellationRequested)
            {
                lock (messages)
                {
                    if (!messages.Contains("操作已被用户取消。"))
                    {
                        messages.Add("操作已被用户取消。");
                    }
                }
            }

            await RemoveGhostFilesAsync(ghostPaths, refreshView: false);
            await RefreshViewFromCacheAsync();

            string summary = BuildOperationSummary(operationName, successCount, failCount, skippedCount, messages);
            SetImageQueueState("图片队列：已完成", summary.Replace(Environment.NewLine, " "));
            return new ImageOperationBatchResult(summary, successCount, failCount, skippedCount);
        }

        private async Task LoadFolderContentAsync(StorageFolder folder)
        {
            SetBusy(true, "正在扫描文件夹...");

            try
            {
                var files = await folder.CreateFileQueryWithOptions(MediaFileCatalog.CreateImageQueryOptions()).GetFilesAsync();
                await LoadFilesAsync(files, folder.Path);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                await _view.ShowTipAsync($"加载文件夹失败: {ex.Message}");
            }
        }

        private async Task LoadFilesAsync(IReadOnlyList<StorageFile> files, string displayPath)
        {
            SetBusy(true, "正在读取图片信息...");

            Images = null;
            lock (_cachedAllItems)
            {
                _cachedAllItems.Clear();
            }

            PathText = displayPath;
            CountText = "0";
            HasImages = false;
            IsEmptyStateVisible = false;

            if (files.Count == 0)
            {
                IsEmptyStateVisible = true;
                SetBusy(false);
                return;
            }

            var concurrentItems = new ConcurrentBag<ImageItem>();
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            int processedCount = 0;
            int totalFiles = files.Count;

            RunOnUi(() => 
            {
                ProgressMax = totalFiles;
                ProgressValue = 0;
            });

            bool isLargeDataset = totalFiles > 500;
            int uiStep = totalFiles > 5000 ? 200 : (totalFiles > 1000 ? 50 : 10);

            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var item = await CreateImageItemAsync(file, !isLargeDataset);
                    concurrentItems.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Load_Image ({FileName})", file.Name);
                }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref processedCount);
                    if (current % uiStep == 0 || current == totalFiles)
                    {
                        RunOnUi(() =>
                        {
                            ProgressValue = current;
                            StatusDetailText = $"{current} / {totalFiles}";
                        });
                    }
                }
            });

            try
            {
                await Task.WhenAll(tasks);

                lock (_cachedAllItems)
                {
                    _cachedAllItems.AddRange(concurrentItems);
                }

                await RefreshViewFromCacheAsync();

                int skippedCount = files.Count - concurrentItems.Count;
                if (skippedCount > 0)
                {
                    await _view.ShowTipAsync($"加载完成，但有 {skippedCount} 张图片因无法读取或损坏而被跳过。");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Load_Files_Critical");
                await _view.ShowTipAsync($"读取失败: {ex.Message}");
                IsEmptyStateVisible = true;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RefreshViewFromCacheAsync()
        {
            SetBusy(true, "正在重新排序...", 0, 100);

            List<ImageItem> snapshot;
            lock (_cachedAllItems)
            {
                snapshot = _cachedAllItems.ToList();
            }

            var sortedList = await Task.Run(() =>
            {
                return CurrentSortField switch
                {
                    "Date" => IsSortDescending
                        ? snapshot.OrderByDescending(item => item.DateCreated).ToList()
                        : snapshot.OrderBy(item => item.DateCreated).ToList(),
                    "Size" => IsSortDescending
                        ? snapshot.OrderByDescending(item => item.FileSize).ToList()
                        : snapshot.OrderBy(item => item.FileSize).ToList(),
                    _ => IsSortDescending
                        ? snapshot.OrderByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                        : snapshot.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                };
            });

            RunOnUi(() =>
            {
                CountText = snapshot.Count.ToString();
                HasImages = snapshot.Count > 0;
                IsEmptyStateVisible = snapshot.Count == 0;

                _lastVisibleItems = sortedList;

                int offset = 0;
                Images = new IncrementalLoadingCollection<ImageItem>((ct, count) =>
                {
                    int takeCount = Math.Min((int)count, sortedList.Count - offset);
                    if (takeCount <= 0)
                    {
                        return Task.FromResult<IEnumerable<ImageItem>>(Array.Empty<ImageItem>());
                    }

                    var batch = sortedList.GetRange(offset, takeCount);
                    offset += takeCount;

                    // 后台异步填充当前分页可视窗口内图片的 EXIF 元数据（高阶分辨率与色彩位深）
                    _ = Task.Run(async () =>
                    {
                        foreach (var item in batch)
                        {
                            if (ct.IsCancellationRequested) break;
                            if (item.ImageWidth == 0 && item.ImageHeight == 0)
                            {
                                try
                                {
                                    var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                                    var meta = await _imageMetadataService.TryReadAsync(file);
                                    if (meta != null)
                                    {
                                        RunOnUi(() =>
                                        {
                                            item.ImageWidth = meta.Width;
                                            item.ImageHeight = meta.Height;
                                            item.ImageFormat = meta.FormatName;
                                            item.ImageBitDepth = meta.BitDepth;
                                            item.ImageDateTaken = meta.DateTaken;
                                        });
                                    }
                                }
                                catch { }
                            }
                        }
                    });

                    return Task.FromResult<IEnumerable<ImageItem>>(batch);
                });
                
                SetBusy(false);
            });
        }

        private async Task<ImageItem> CreateImageItemAsync(StorageFile file, bool loadMetadata = true)
        {
            ulong fileSize = 0;
            DateTimeOffset dateCreated = file.DateCreated;
            try
            {
                var fi = new FileInfo(file.Path);
                if (fi.Exists)
                {
                    fileSize = (ulong)fi.Length;
                    dateCreated = fi.CreationTimeUtc;
                }
                else
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    fileSize = properties.Size;
                }
            }
            catch
            {
                try { var properties = await file.GetBasicPropertiesAsync(); fileSize = properties.Size; } catch { }
            }

            var item = new ImageItem
            {
                FileName = file.Name,
                ImagePath = file.Path,
                DateCreated = dateCreated,
                FileSize = fileSize,
                CustomTags = await _mediaTagService.GetTagsAsync(file.Path)
            };

            if (loadMetadata)
            {
                var metadata = await _imageMetadataService.TryReadAsync(file);
                if (metadata != null)
                {
                    item.ImageWidth = metadata.Width;
                    item.ImageHeight = metadata.Height;
                    item.ImageFormat = metadata.FormatName;
                    item.ImageBitDepth = metadata.BitDepth;
                    item.ImageDateTaken = metadata.DateTaken;
                }
            }

            return item;
        }

        private async Task PerformRenameFilesAsync(List<RenamePreviewItem> items)
        {
            SetBusy(true, "正在重命名...", 0, items.Count);

            int successCount = 0;
            int failCount = 0;
            var renamedResults = new List<(string OriginalPath, string NewPath, string NewName)>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                try
                {
                    if (!item.OriginalName.Equals(item.NewName, StringComparison.OrdinalIgnoreCase))
                    {
                        await item.File.RenameAsync(item.NewName, NameCollisionOption.FailIfExists);
                        string newPath = BuildSiblingPath(item.OriginalPath, item.NewName);
                        await _mediaTagService.MoveTagsAsync(item.OriginalPath, newPath);
                        renamedResults.Add((item.OriginalPath, newPath, item.NewName));
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogError(ex, "Rename_Execute ({OriginalName})", item.OriginalName);
                }

                if ((i + 1) % 20 == 0 || i == items.Count - 1)
                {
                    int current = i + 1;
                    RunOnUi(() =>
                    {
                        ProgressValue = current;
                        ProgressMax = items.Count;
                        StatusMainText = $"正在重命名... {current}/{items.Count}";
                    });
                }
            }

            lock (_cachedAllItems)
            {
                foreach (var renamed in renamedResults)
                {
                    var cacheItem = _cachedAllItems.FirstOrDefault(item =>
                        string.Equals(item.ImagePath, renamed.OriginalPath, StringComparison.OrdinalIgnoreCase));

                    if (cacheItem != null)
                    {
                        cacheItem.FileName = renamed.NewName;
                        cacheItem.ImagePath = renamed.NewPath;
                    }
                }
            }

            SetBusy(false);
            await RefreshViewFromCacheAsync();

            await _view.ShowTipAsync($"重命名完成。\n成功: {successCount} 张\n失败: {failCount} 张");
        }

        private async Task PerformDeleteFilesAsync(List<StorageFile> files)
        {
            SetBusy(true, "正在移至回收站...", 0, files.Count);

            int success = 0;
            int fail = 0;
            var deletedPaths = new List<string>();

            const int chunkSize = 50;
            for (int i = 0; i < files.Count; i += chunkSize)
            {
                var chunk = files.Skip(i).Take(chunkSize).ToList();
                var paths = chunk.Select(f => f.Path).ToList();

                var successfulPaths = await _nativeFileService.MoveToRecycleBinBatchAsync(paths);
                success += successfulPaths.Count;
                fail += (chunk.Count - successfulPaths.Count);
                deletedPaths.AddRange(successfulPaths);

                int current = Math.Min(i + chunk.Count, files.Count);
                RunOnUi(() =>
                {
                    ProgressValue = current;
                    ProgressMax = files.Count;
                    StatusMainText = $"正在移至回收站... ({current}/{files.Count})";
                    StatusDetailText = current == files.Count ? "处理完成" : chunk.Last().Name;
                });
            }

            await _mediaTagService.RemoveTagsAsync(deletedPaths);
            var deletedPathSet = new HashSet<string>(deletedPaths, StringComparer.OrdinalIgnoreCase);
            lock (_cachedAllItems)
            {
                _cachedAllItems.RemoveAll(item =>
                    !string.IsNullOrWhiteSpace(item.ImagePath) &&
                    deletedPathSet.Contains(item.ImagePath));
            }

            SetBusy(false);
            await RefreshViewFromCacheAsync();

            await _view.ShowTipAsync($"删除完成。\n成功移至回收站: {success} 个\n失败: {fail} 个");
        }

        private async Task<StorageFile?> TryGetStorageFileAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return await StorageFile.GetFileFromPathAsync(path);
            }
            catch
            {
                return null;
            }
        }

        private List<ImageItem> ExtractSelectedItems(IList<object>? selectedItems)
        {
            return selectedItems?
                .OfType<ImageItem>()
                .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath) && MediaFileCatalog.IsImage(item.FileName))
                .ToList()
                ?? new List<ImageItem>();
        }

        private async Task TrackOutputPathAsync(string outputPath)
        {
            if (!File.Exists(outputPath) || !MediaFileCatalog.IsImage(outputPath) || !IsPathUnderCurrentFolder(outputPath))
            {
                return;
            }

            await TrackFileInCacheAsync(outputPath);
        }

        private async Task TrackFileInCacheAsync(string filePath)
        {
            var file = await TryGetStorageFileAsync(filePath);
            if (file == null || !MediaFileCatalog.IsImage(file.Name))
            {
                return;
            }

            var trackedItem = await CreateImageItemAsync(file);
            lock (_cachedAllItems)
            {
                int index = _cachedAllItems.FindIndex(item =>
                    string.Equals(item.ImagePath, file.Path, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    _cachedAllItems[index] = trackedItem;
                }
                else
                {
                    _cachedAllItems.Add(trackedItem);
                }
            }
        }

        private async Task RemoveGhostFilesAsync(IEnumerable<string> ghostPaths, bool refreshView = true)
        {
            var pathSet = ghostPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (pathSet.Count == 0)
            {
                return;
            }

            lock (_cachedAllItems)
            {
                _cachedAllItems.RemoveAll(item =>
                    !string.IsNullOrWhiteSpace(item.ImagePath) &&
                    pathSet.Contains(item.ImagePath));
            }

            await _mediaTagService.RemoveTagsAsync(pathSet);
            if (refreshView)
            {
                await RefreshViewFromCacheAsync();
            }
        }

        private Dictionary<string, HashSet<string>> BuildDirectoryNameReservations()
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            lock (_cachedAllItems)
            {
                foreach (var item in _cachedAllItems)
                {
                    if (string.IsNullOrWhiteSpace(item.ImagePath) || string.IsNullOrWhiteSpace(item.FileName))
                    {
                        continue;
                    }

                    string directoryPath = GetDirectoryPath(item.ImagePath);
                    if (!result.TryGetValue(directoryPath, out var names))
                    {
                        names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        result[directoryPath] = names;
                    }

                    names.Add(item.FileName);
                }
            }

            return result;
        }

        private static void ReleaseOriginalNames(Dictionary<string, HashSet<string>> reservations, IEnumerable<ImageItem> items)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ImagePath) || string.IsNullOrWhiteSpace(item.FileName))
                {
                    continue;
                }

                string directoryPath = GetDirectoryPath(item.ImagePath);
                if (reservations.TryGetValue(directoryPath, out var names))
                {
                    names.Remove(item.FileName);
                }
            }
        }

        private List<RenamePreviewItem> BuildRenamePreviewItems(
            IEnumerable<RenameCandidate> candidates,
            Dictionary<string, HashSet<string>> reservations)
        {
            var previewItems = new List<RenamePreviewItem>();

            foreach (var candidate in candidates)
            {
                string reservedName = ReserveUniqueName(
                    reservations,
                    candidate.OriginalPath,
                    candidate.BaseName,
                    candidate.File.FileType);

                if (candidate.OriginalName.Equals(reservedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                previewItems.Add(new RenamePreviewItem
                {
                    File = candidate.File,
                    OriginalPath = candidate.OriginalPath,
                    OriginalName = candidate.OriginalName,
                    NewName = reservedName
                });
            }

            return previewItems;
        }

        private List<RenamePreviewItem> BuildFallbackPreviewItems(
            List<StorageFile> files,
            string fallbackPrefix,
            Dictionary<string, HashSet<string>> reservations)
        {
            var result = new List<RenamePreviewItem>();
            int counter = 1;

            foreach (var file in files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                string baseName = $"{fallbackPrefix}_{counter:D2}";
                string newName = ReserveUniqueName(reservations, file.Path, baseName, file.FileType);

                if (!file.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new RenamePreviewItem
                    {
                        File = file,
                        OriginalPath = file.Path,
                        OriginalName = file.Name,
                        NewName = newName
                    });
                }

                counter++;
            }

            return result;
        }

        private static string ReserveUniqueName(
            Dictionary<string, HashSet<string>> reservations,
            string originalPath,
            string baseName,
            string extension)
        {
            string directoryPath = GetDirectoryPath(originalPath);
            if (!reservations.TryGetValue(directoryPath, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                reservations[directoryPath] = names;
            }

            string candidateName = baseName + extension;
            int counter = 1;
            while (!names.Add(candidateName))
            {
                candidateName = $"{baseName}_{counter:D2}{extension}";
                counter++;
            }

            return candidateName;
        }

        private void CacheImageOperationSummary(string summary)
        {
            _lastImageOperationSummary = summary;
            OnPropertyChanged(nameof(HasImageOperationResults));
            OnPropertyChanged(nameof(LastImageOperationSummaryText));
        }

        private static string BuildOperationSummary(
            string operationName,
            int successCount,
            int failCount,
            int skippedCount,
            IReadOnlyList<string> messages)
        {
            var lines = new List<string>
            {
                $"{operationName}完成。",
                $"成功: {successCount} 张",
                $"失败: {failCount} 张",
                $"跳过: {skippedCount} 张"
            };

            if (messages.Count > 0)
            {
                lines.Add("详情:");
                lines.AddRange(messages.Take(5));
                if (messages.Count > 5)
                {
                    lines.Add($"另有 {messages.Count - 5} 条详情已省略。");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private string BuildSharedTagInput(IReadOnlyList<ImageItem> items)
        {
            if (items.Count == 0 || items[0].CustomTags.Count == 0)
            {
                return string.Empty;
            }

            var first = items[0].CustomTags;
            bool allSame = items.All(item => item.CustomTags.SequenceEqual(first, StringComparer.OrdinalIgnoreCase));
            return allSame ? string.Join(", ", first) : string.Empty;
        }

        private static string BuildTimestampBaseName(DateTimeOffset timestamp)
        {
            return timestamp.TimeOfDay == TimeSpan.Zero
                ? timestamp.ToString("yyyy-MM-dd")
                : timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        private bool IsPathUnderCurrentFolder(string filePath)
        {
            if (_currentFolder == null || string.IsNullOrWhiteSpace(_currentFolder.Path))
            {
                return false;
            }

            string folderPath = Path.GetFullPath(_currentFolder.Path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidatePath = Path.GetFullPath(filePath);

            return candidatePath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidatePath, folderPath, StringComparison.OrdinalIgnoreCase);
        }

        private void SetBusy(bool busy, string text = "", double value = 0, double max = 100)
        {
            RunOnUi(() =>
            {
                IsBusy = busy;
                IsProgressVisible = busy;
                ProgressValue = value;
                ProgressMax = max;
                StatusMainText = busy ? text : "READY";
                StatusDetailText = string.Empty;
            });
        }

        private void SetImageQueueState(string statusText, string detailText)
        {
            RunOnUi(() =>
            {
                ImageQueueStatusText = statusText;
                ImageQueueDetailText = detailText;
            });
        }

        private void RunOnUi(Action action)
        {
            if (_dispatcherQueue == null)
            {
                action();
                return;
            }

            _dispatcherQueue.TryEnqueue(() => action());
        }

        private static string GetDirectoryPath(string? path)
        {
            return Path.GetDirectoryName(path ?? string.Empty) ?? string.Empty;
        }

        private static string BuildSiblingPath(string originalPath, string newName)
        {
            return Path.Combine(GetDirectoryPath(originalPath), newName);
        }

        private static string? NormalizeFolderPathInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            string normalized = input.Trim().Trim('"');
            normalized = Environment.ExpandEnvironmentVariables(normalized);
            return Path.GetFullPath(normalized);
        }

        private sealed record RenameCandidate(StorageFile File, string OriginalPath, string OriginalName, string BaseName);

        private sealed record ImageOperationBatchResult(string SummaryText, int SuccessCount, int FailCount, int SkippedCount);
    }
}
