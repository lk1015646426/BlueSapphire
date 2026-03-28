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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.ViewModels
{
    public partial class MediaManagerViewModel : ObservableObject
    {
        private readonly MediaRenameService _renameService;
        private readonly MediaDeduplicationService _deduplicationService;
        private readonly NativeFileService _nativeFileService;
        private readonly DocumentConversionService _documentConversionService;
        private readonly PdfDocumentService _pdfDocumentService;
        private readonly ImageProcessingService _imageProcessingService;
        private readonly ImageMetadataService _imageMetadataService;
        private readonly MediaTagService _mediaTagService;
        private readonly AudioConversionService _audioConversionService;
        private readonly AudioCatalogExportService _audioCatalogExportService;
        private readonly AudioMetadataService _audioMetadataService;
        private readonly AudioTagService _audioTagService;
        private readonly AudioPlaylistService _audioPlaylistService;
        private readonly AudioPreviewService _audioPreviewService;
        private readonly List<ImageItem> _cachedAllItems = new();
        private readonly List<DocumentConversionBatchReport> _documentReportHistory = new();
        private List<ImageItem> _lastVisibleItems = new();

        private IMediaViewInteraction _view = null!;
        private DispatcherQueue _dispatcherQueue = null!;
        private CancellationTokenSource? _globalCts;
        private StorageFolder? _currentFolder;
        private Task<DocumentConversionEnvironmentStatus>? _documentConversionEnvironmentTask;
        private bool _hasShownDocumentConversionTip;
        private bool _isAudioPreviewSubscribed;
        private bool _isAudioPreviewSeeking;
        private string? _audioPreviewLoadedPath;
        private AudioPreviewLoopMode _audioPreviewLoopMode = AudioPreviewLoopMode.Off;
        private DocumentConversionBatchReport? _lastImageOperationReport;
        private DocumentConversionBatchReport? _lastDocumentConversionReport;
        private DocumentConversionBatchReport? _lastAudioConversionReport;
        private DocumentRetryContext? _lastDocumentRetryContext;

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

        private string _currentMediaType = "All";
        public string CurrentMediaType
        {
            get => _currentMediaType;
            set => SetProperty(ref _currentMediaType, value);
        }

        public bool IsTypeAll => CurrentMediaType == "All";
        public bool IsTypeImage => CurrentMediaType == "Image";
        public bool IsTypeAudio => CurrentMediaType == "Audio";
        public bool IsTypeDoc => CurrentMediaType == "Doc";

        private bool _isDocumentConversionAvailable;
        public bool IsDocumentConversionAvailable
        {
            get => _isDocumentConversionAvailable;
            set => SetProperty(ref _isDocumentConversionAvailable, value);
        }

        private string _documentConversionStatusText = "文档引擎：未检测";
        public string DocumentConversionStatusText
        {
            get => _documentConversionStatusText;
            set => SetProperty(ref _documentConversionStatusText, value);
        }

        private string _documentConversionSupportText = "切换到文档模式后自动检测当前机器的文档转换环境。";
        public string DocumentConversionSupportText
        {
            get => _documentConversionSupportText;
            set => SetProperty(ref _documentConversionSupportText, value);
        }

        private string _documentQueueStatusText = "转换队列：空闲";
        public string DocumentQueueStatusText
        {
            get => _documentQueueStatusText;
            set => SetProperty(ref _documentQueueStatusText, value);
        }

        private string _documentQueueDetailText = "等待文档任务。";
        public string DocumentQueueDetailText
        {
            get => _documentQueueDetailText;
            set => SetProperty(ref _documentQueueDetailText, value);
        }

        public bool HasDocumentConversionResults => _lastDocumentConversionReport != null;

        public string LastDocumentConversionSummaryText => _lastDocumentConversionReport?.SummaryText ?? "暂无最近一次文档处理结果。";

        public bool HasDocumentTaskHistory => _documentReportHistory.Count > 0;

        public bool CanRetryFailedDocumentItems =>
            _lastDocumentRetryContext != null &&
            _lastDocumentConversionReport?.FailedCount > 0;

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

        public bool HasImageOperationResults => _lastImageOperationReport != null;

        public string LastImageOperationSummaryText => _lastImageOperationReport?.SummaryText ?? "暂无最近一次图片处理结果。";

        private string _audioQueueStatusText = "音频队列：空闲";
        public string AudioQueueStatusText
        {
            get => _audioQueueStatusText;
            set => SetProperty(ref _audioQueueStatusText, value);
        }

        private string _audioQueueDetailText = "等待音频任务。";
        public string AudioQueueDetailText
        {
            get => _audioQueueDetailText;
            set => SetProperty(ref _audioQueueDetailText, value);
        }

        public bool HasAudioConversionResults => _lastAudioConversionReport != null;

        public string LastAudioConversionSummaryText => _lastAudioConversionReport?.SummaryText ?? "暂无最近一次音频处理结果。";

        private bool _isAudioPreviewVisible;
        public bool IsAudioPreviewVisible
        {
            get => _isAudioPreviewVisible;
            set => SetProperty(ref _isAudioPreviewVisible, value);
        }

        private bool _canControlAudioPreview;
        public bool CanControlAudioPreview
        {
            get => _canControlAudioPreview;
            set => SetProperty(ref _canControlAudioPreview, value);
        }

        private string _audioPreviewTitleText = "选择单个音频以开始预览";
        public string AudioPreviewTitleText
        {
            get => _audioPreviewTitleText;
            set => SetProperty(ref _audioPreviewTitleText, value);
        }

        private string _audioPreviewSubtitleText = "支持播放、暂停、跳转和定位。";
        public string AudioPreviewSubtitleText
        {
            get => _audioPreviewSubtitleText;
            set => SetProperty(ref _audioPreviewSubtitleText, value);
        }

        private string _audioPreviewPlayPauseText = "播放";
        public string AudioPreviewPlayPauseText
        {
            get => _audioPreviewPlayPauseText;
            set => SetProperty(ref _audioPreviewPlayPauseText, value);
        }

        private string _audioPreviewPlayPauseGlyph = "\uE768";
        public string AudioPreviewPlayPauseGlyph
        {
            get => _audioPreviewPlayPauseGlyph;
            set => SetProperty(ref _audioPreviewPlayPauseGlyph, value);
        }

        private string _audioPreviewPositionText = "00:00";
        public string AudioPreviewPositionText
        {
            get => _audioPreviewPositionText;
            set => SetProperty(ref _audioPreviewPositionText, value);
        }

        private string _audioPreviewDurationText = "00:00";
        public string AudioPreviewDurationText
        {
            get => _audioPreviewDurationText;
            set => SetProperty(ref _audioPreviewDurationText, value);
        }

        private double _audioPreviewSeekValue;
        public double AudioPreviewSeekValue
        {
            get => _audioPreviewSeekValue;
            set => SetProperty(ref _audioPreviewSeekValue, value);
        }

        private double _audioPreviewSeekMaximum = 1;
        public double AudioPreviewSeekMaximum
        {
            get => _audioPreviewSeekMaximum;
            set => SetProperty(ref _audioPreviewSeekMaximum, value);
        }

        private bool _canGoToPreviousAudioPreview;
        public bool CanGoToPreviousAudioPreview
        {
            get => _canGoToPreviousAudioPreview;
            set => SetProperty(ref _canGoToPreviousAudioPreview, value);
        }

        private bool _canGoToNextAudioPreview;
        public bool CanGoToNextAudioPreview
        {
            get => _canGoToNextAudioPreview;
            set => SetProperty(ref _canGoToNextAudioPreview, value);
        }

        private string _audioPreviewQueueText = "- / -";
        public string AudioPreviewQueueText
        {
            get => _audioPreviewQueueText;
            set => SetProperty(ref _audioPreviewQueueText, value);
        }

        public string AudioPreviewLoopModeText => _audioPreviewLoopMode switch
        {
            AudioPreviewLoopMode.All => "列表循环",
            AudioPreviewLoopMode.One => "单曲循环",
            _ => "顺序播放"
        };

        public string EmptyStateText => $"等待接入{GetCurrentMediaTypeDisplayName()}媒体库...";
        public string ContextModeText => $"{GetCurrentMediaTypeDisplayName()} · 上下文模式";
        public string TabStatusText => $"TAB: {GetCurrentMediaTypeDisplayName()}";
        public string EmptyStateIconGlyph => CurrentMediaType switch
        {
            "Image" => "\uE8B9",
            "Audio" => "\uE8D6",
            "Doc" => "\uE8A5",
            _ => "\uE81E"
        };

        public MediaManagerViewModel(
            MediaRenameService renameService,
            MediaDeduplicationService deduplicationService,
            NativeFileService nativeFileService,
            DocumentConversionService documentConversionService,
            PdfDocumentService pdfDocumentService,
            ImageProcessingService imageProcessingService,
            ImageMetadataService imageMetadataService,
            MediaTagService mediaTagService,
            AudioConversionService audioConversionService,
            AudioCatalogExportService audioCatalogExportService,
            AudioMetadataService audioMetadataService,
            AudioTagService audioTagService,
            AudioPlaylistService audioPlaylistService,
            AudioPreviewService audioPreviewService)
        {
            _renameService = renameService;
            _deduplicationService = deduplicationService;
            _nativeFileService = nativeFileService;
            _documentConversionService = documentConversionService;
            _pdfDocumentService = pdfDocumentService;
            _imageProcessingService = imageProcessingService;
            _imageMetadataService = imageMetadataService;
            _mediaTagService = mediaTagService;
            _audioConversionService = audioConversionService;
            _audioCatalogExportService = audioCatalogExportService;
            _audioMetadataService = audioMetadataService;
            _audioTagService = audioTagService;
            _audioPlaylistService = audioPlaylistService;
            _audioPreviewService = audioPreviewService;
        }

        public void Initialize(IMediaViewInteraction view, DispatcherQueue dispatcherQueue)
        {
            _view = view;
            _dispatcherQueue = dispatcherQueue;

            if (!_isAudioPreviewSubscribed)
            {
                _audioPreviewService.StateChanged += AudioPreviewService_StateChanged;
                _audioPreviewService.PlaybackEnded += AudioPreviewService_PlaybackEnded;
                _isAudioPreviewSubscribed = true;
            }

            ResetAudioPreviewToIdle();
        }

        [RelayCommand]
        private void ChangeMediaType(string mediaType)
        {
            if (CurrentMediaType == mediaType)
            {
                return;
            }

            CurrentMediaType = mediaType;
            RaiseMediaTypeStateChanged();
            RefreshViewFromCache();

            if (mediaType == "Doc")
            {
                _ = EnsureDocumentConversionEnvironmentAsync(showPrompt: true);
            }

            if (mediaType != "Audio")
            {
                StopAudioPreview();
            }
            else
            {
                IsAudioPreviewVisible = true;
            }
        }

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
                await _view.ShowTipAsync("请先选择要重命名的文件。");
                return;
            }

            if (_currentFolder == null)
            {
                return;
            }

            var items = selectedItems.OfType<ImageItem>()
                .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath))
                .ToList();

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("当前选择中没有可处理的文件。");
                return;
            }

            SetBusy(true, "正在分析文件时间...");

            var candidates = new ConcurrentBag<RenameCandidate>();
            var unresolvedFiles = new ConcurrentBag<StorageFile>();
            var ghostPaths = new ConcurrentBag<string>();

            try
            {
                using var semaphore = new SemaphoreSlim(8);
                int processed = 0;
                int total = items.Count;

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var file = await TryGetStorageFileAsync(item.ImagePath!);
                        if (file == null)
                        {
                            ghostPaths.Add(item.ImagePath!);
                            return;
                        }

                        var timestamp = await _renameService.ResolveBestTimestampAsync(file);
                        if (!_renameService.HasUsableTimestamp(timestamp))
                        {
                            unresolvedFiles.Add(file);
                            return;
                        }

                        candidates.Add(new RenameCandidate(
                            file,
                            file.Path,
                            file.Name,
                            BuildTimestampBaseName(timestamp)));

                        int current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0 || current == total)
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                ProgressValue = current;
                                ProgressMax = total;
                                StatusMainText = $"正在分析... {current}/{total}";
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        MatrixLogService.LogError($"Rename_Analyze ({item.FileName ?? item.ImagePath})", ex);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                RemoveGhostFiles(ghostPaths);
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
                        "发现缺失时间信息的文件",
                        $"有 {unresolvedFiles.Count} 个文件无法解析可靠时间。请输入一个前缀，程序会顺序编号；留空则跳过这些文件。",
                        "未命名文件");

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
                        ? "没有可执行的重命名任务，未解析出时间的文件已跳过。"
                        : "没有生成任何需要重命名的任务。");
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
                MatrixLogService.LogError("Rename_Process_Critical", ex);
                await _view.ShowTipAsync($"重命名预处理失败: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ScanDuplicates(string mode)
        {
            if (_currentFolder == null)
            {
                await _view.ShowTipAsync("请先导入文件夹。");
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
                string modeName = mode == "Similar" ? "智能扫描" : "精确扫描";
                SetBusy(true, $"正在初始化{modeName}...", 0, 100);

                var progress = new Progress<(double Value, string Message, string Detail)>(value =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        ProgressValue = value.Value;
                        if (!string.IsNullOrWhiteSpace(value.Message))
                        {
                            StatusMainText = value.Message;
                        }

                        StatusDetailText = value.Detail ?? string.Empty;
                    });
                });

                List<List<StorageFile>> finalDuplicates = mode == "Similar"
                    ? await _deduplicationService.FindSimilarImagesAsync(_currentFolder, progress, token)
                    : await _deduplicationService.FindDuplicatesAsync(_currentFolder, progress, token);

                SetBusy(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (finalDuplicates.Count > 0)
                {
                    var filesToDelete = await _view.ShowDuplicateResultsAsync(finalDuplicates);
                    if (filesToDelete.Count > 0)
                    {
                        await PerformDeleteFiles(filesToDelete);
                    }
                }
                else
                {
                    await _view.ShowTipAsync(mode == "Similar"
                        ? "扫描完成，未发现相似照片。"
                        : "扫描完成，未发现内容重复的文件。");
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
            if (selectedItems == null || selectedItems.Count == 0)
            {
                return;
            }

            bool confirm = await _view.ShowDeleteConfirmationAsync(selectedItems.Count);
            if (!confirm)
            {
                return;
            }

            var files = new List<StorageFile>();
            var ghostPaths = new List<string>();

            foreach (var item in selectedItems.OfType<ImageItem>())
            {
                if (string.IsNullOrWhiteSpace(item.ImagePath))
                {
                    continue;
                }

                var file = await TryGetStorageFileAsync(item.ImagePath);
                if (file == null)
                {
                    ghostPaths.Add(item.ImagePath);
                    continue;
                }

                files.Add(file);
            }

            RemoveGhostFiles(ghostPaths);

            if (ghostPaths.Count > 0)
            {
                await _view.ShowTipAsync($"已自动清理 {ghostPaths.Count} 个在外部被删除的失效文件。");
            }

            if (files.Count > 0)
            {
                await PerformDeleteFiles(files);
            }
        }

        [RelayCommand]
        private async Task OpenSelectedLocation(IList<object> selectedItems)
        {
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要打开位置的文件。");
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

            RemoveGhostFiles(ghostPaths);

            if (validPaths.Count == 0)
            {
                await _view.ShowTipAsync("未找到可打开的有效文件，已自动清理失效项。");
                return;
            }

            if (validPaths.Count == 1)
            {
                bool success = await _nativeFileService.RevealInExplorerAsync(validPaths[0]);
                await _view.ShowTipAsync(success ? "已定位到文件所在位置。" : "打开位置失败，请检查资源管理器是否可用。");
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

            if (openedCount == 0)
            {
                await _view.ShowTipAsync("打开位置失败，请检查资源管理器是否可用。");
                return;
            }

            string suffix = folderPaths.Count > 3 ? "，已限制为前 3 个目录" : string.Empty;
            await _view.ShowTipAsync($"已打开 {openedCount} 个所在目录{suffix}。");
        }

        public async Task ConvertSelectedImagesToTargetAsync(IList<object> selectedItems, string targetKey)
        {
            if (!_imageProcessingService.TryParseConversionTarget(targetKey, out var target))
            {
                await _view.ShowTipAsync("未知的图片目标格式。");
                return;
            }

            string targetName = _imageProcessingService.GetTargetDisplayName(target);
            string targetExtension = _imageProcessingService.GetTargetExtension(target);
            var items = ExtractSelectedImageItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要转换的图片文件。");
                return;
            }

            var reportItems = new List<DocumentConversionBatchItem>();
            var processableItems = new List<ImageItem>();
            foreach (var item in items)
            {
                if (string.Equals(Path.GetExtension(item.FileName), targetExtension, StringComparison.OrdinalIgnoreCase))
                {
                    reportItems.Add(new DocumentConversionBatchItem(
                        item.ImagePath ?? string.Empty,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        $"源文件已经是 {targetName} 格式。"));
                    continue;
                }

                processableItems.Add(item);
            }

            if (processableItems.Count == 0)
            {
                var report = new DocumentConversionBatchReport(
                    reportItems,
                    operationName: $"图片转 {targetName}",
                    dialogTitle: $"{targetName} 图片转换结果");
                await PresentImageOperationReportAsync(report, "图片队列：未执行");
                return;
            }

            var conversionReport = await ProcessSelectedImagesCoreAsync(
                processableItems,
                reportItems,
                busyText: $"正在转换图片为 {targetName}...",
                operationName: $"图片转 {targetName}",
                dialogTitle: $"{targetName} 图片转换结果",
                queueReadyText: $"已加入 {processableItems.Count} 个图片文件，目标：{targetName}",
                queueActionName: $"正在转换为 {targetName}",
                processAsync: path => _imageProcessingService.ConvertAsync(path, target),
                logKey: $"Image_Convert_{targetName}");

            await PresentImageOperationReportAsync(conversionReport);
        }

        public async Task ResizeSelectedImagesAsync(IList<object> selectedItems, string presetKey)
        {
            if (!_imageProcessingService.TryParseResizePreset(presetKey, out var preset))
            {
                await _view.ShowTipAsync("未知的图片尺寸预设。");
                return;
            }

            string presetName = _imageProcessingService.GetResizeDisplayName(preset);
            var items = ExtractSelectedImageItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要调整尺寸的图片文件。");
                return;
            }

            var report = await ProcessSelectedImagesCoreAsync(
                items,
                reportItems: new List<DocumentConversionBatchItem>(),
                busyText: $"正在调整图片尺寸为 {presetName}...",
                operationName: "图片尺寸调整",
                dialogTitle: "图片尺寸调整结果",
                queueReadyText: $"已加入 {items.Count} 个图片文件，目标：{presetName}",
                queueActionName: $"正在调整尺寸：{presetName}",
                processAsync: path => _imageProcessingService.ResizeAsync(path, preset),
                logKey: $"Image_Resize_{presetName}");

            await PresentImageOperationReportAsync(report);
        }

        public async Task CropSelectedImagesAsync(IList<object> selectedItems, string presetKey)
        {
            if (!_imageProcessingService.TryParseCropPreset(presetKey, out var preset))
            {
                await _view.ShowTipAsync("未知的裁剪预设。");
                return;
            }

            string presetName = _imageProcessingService.GetCropDisplayName(preset);
            var items = ExtractSelectedImageItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要裁剪的图片文件。");
                return;
            }

            var report = await ProcessSelectedImagesCoreAsync(
                items,
                reportItems: new List<DocumentConversionBatchItem>(),
                busyText: $"正在执行 {presetName}...",
                operationName: "图片裁剪",
                dialogTitle: "图片裁剪结果",
                queueReadyText: $"已加入 {items.Count} 个图片文件，预设：{presetName}",
                queueActionName: presetName,
                processAsync: path => _imageProcessingService.CropAsync(path, preset),
                logKey: $"Image_Crop_{presetName}");

            await PresentImageOperationReportAsync(report);
        }

        public async Task CompressSelectedImagesAsync(IList<object> selectedItems, string presetKey)
        {
            if (!_imageProcessingService.TryParseCompressionPreset(presetKey, out var preset))
            {
                await _view.ShowTipAsync("未知的图片压缩预设。");
                return;
            }

            string presetName = _imageProcessingService.GetCompressionDisplayName(preset);
            var items = ExtractSelectedImageItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要压缩导出的图片文件。");
                return;
            }

            var report = await ProcessSelectedImagesCoreAsync(
                items,
                reportItems: new List<DocumentConversionBatchItem>(),
                busyText: $"正在执行 {presetName}...",
                operationName: "图片压缩导出",
                dialogTitle: "图片压缩导出结果",
                queueReadyText: $"已加入 {items.Count} 个图片文件，预设：{presetName}",
                queueActionName: presetName,
                processAsync: path => _imageProcessingService.CompressAsync(path, preset),
                logKey: $"Image_Compress_{presetName}");

            await PresentImageOperationReportAsync(report);
        }

        public async Task EnhanceSelectedImagesAsync(IList<object> selectedItems, string presetKey)
        {
            if (!_imageProcessingService.TryParseEnhancementPreset(presetKey, out var preset))
            {
                await _view.ShowTipAsync("未知的图片增强预设。");
                return;
            }

            string presetName = _imageProcessingService.GetEnhancementDisplayName(preset);
            var items = ExtractSelectedImageItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要增强的图片文件。");
                return;
            }

            var report = await ProcessSelectedImagesCoreAsync(
                items,
                reportItems: new List<DocumentConversionBatchItem>(),
                busyText: $"正在执行 {presetName}...",
                operationName: "图片增强",
                dialogTitle: "图片增强结果",
                queueReadyText: $"已加入 {items.Count} 个图片文件，预设：{presetName}",
                queueActionName: presetName,
                processAsync: path => _imageProcessingService.EnhanceAsync(path, preset),
                logKey: $"Image_Enhance_{presetName}");

            await PresentImageOperationReportAsync(report);
        }

        [RelayCommand]
        private async Task ShowLastImageOperationResults()
        {
            if (_lastImageOperationReport == null)
            {
                await _view.ShowTipAsync("暂无最近一次图片处理结果。");
                return;
            }

            await _view.ShowDocumentConversionResultsAsync(_lastImageOperationReport);
        }

        [RelayCommand]
        private async Task EditSelectedMediaTags(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要打标签的文件。");
                return;
            }

            string defaultText = BuildSharedTagInput(items);
            string? tagInput = await _view.ShowInputPromptAsync(
                "管理自定义标签",
                $"将为所选 {items.Count} 个{GetCurrentMediaTypeDisplayName()}文件写入自定义标签；使用逗号分隔，留空表示清空。",
                defaultText);

            if (tagInput == null)
            {
                return;
            }

            var tags = _mediaTagService.ParseTags(tagInput);
            var (queueLabel, setQueueState) = GetContextualQueueDispatcher();
            var report = await RunFileBatchOperationAsync(
                items,
                new FileBatchOperationOptions(
                    BusyText: "正在更新自定义标签...",
                    OperationName: "自定义标签更新",
                    DialogTitle: "自定义标签更新结果",
                    QueueLabel: queueLabel,
                    QueueReadyDetailText: $"已加入 {items.Count} 个文件，准备更新自定义标签。",
                    BuildQueueDetailText: (_, _, item) => $"正在更新标签：{item.FileName}",
                    SetQueueState: setQueueState,
                    ErrorLogContext: "MediaTags_Update"),
                async (file, _) =>
                {
                    var result = await _mediaTagService.ReplaceTagsAsync(file.Path, tags);
                    if (result.Success)
                    {
                        await TrackFileInCacheAsync(file.Path);
                    }

                    return new DocumentConversionBatchItem(
                        file.Path,
                        null,
                        result.Success ? DocumentConversionBatchItemStatus.Succeeded : DocumentConversionBatchItemStatus.Failed,
                        result.Message);
                });

            await PresentContextualOperationReportAsync(report);
        }

        [RelayCommand]
        private async Task ConvertSelectedDocumentsToPdf(IList<object> selectedItems)
        {
            await ConvertSelectedDocumentsCoreAsync(selectedItems, DocumentConversionTarget.Pdf);
        }

        [RelayCommand]
        private async Task ShowLastDocumentConversionResults()
        {
            if (_lastDocumentConversionReport == null)
            {
                await _view.ShowTipAsync("暂无最近一次文档处理结果。");
                return;
            }

            await _view.ShowDocumentConversionResultsAsync(_lastDocumentConversionReport);
        }

        [RelayCommand]
        private async Task ShowDocumentTaskHistory()
        {
            if (_documentReportHistory.Count == 0)
            {
                await _view.ShowTipAsync("暂无文档任务历史。");
                return;
            }

            var selectedReport = await _view.ShowDocumentTaskHistoryAsync(_documentReportHistory.ToList());
            if (selectedReport != null)
            {
                await _view.ShowDocumentConversionResultsAsync(selectedReport);
            }
        }

        [RelayCommand]
        private async Task RetryFailedDocumentItems()
        {
            if (_lastDocumentConversionReport == null || _lastDocumentRetryContext == null)
            {
                await _view.ShowTipAsync("暂无可重试的文档任务。");
                return;
            }

            var failedPaths = _lastDocumentConversionReport.Items
                .Where(item => item.Status == DocumentConversionBatchItemStatus.Failed && !string.IsNullOrWhiteSpace(item.SourcePath))
                .Select(item => item.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (failedPaths.Count == 0)
            {
                await _view.ShowTipAsync("最近一次文档任务没有失败项。");
                return;
            }

            var items = await ResolveItemsByPathsAsync(failedPaths);
            if (items.Count == 0)
            {
                await _view.ShowTipAsync("失败项源文件已不存在或不在当前媒体库中。");
                return;
            }

            switch (_lastDocumentRetryContext.Kind)
            {
                case DocumentRetryKind.ConvertTarget when _lastDocumentRetryContext.Target.HasValue:
                    await ConvertSelectedDocumentsCoreAsync(items.Cast<object>().ToList(), _lastDocumentRetryContext.Target.Value);
                    break;
                case DocumentRetryKind.ExtractPdfPages when !string.IsNullOrWhiteSpace(_lastDocumentRetryContext.PageSelectionText):
                    var report = await ExtractPdfPagesCoreAsync(items, _lastDocumentRetryContext.PageSelectionText!);
                    await PresentDocumentOperationReportAsync(report, _lastDocumentRetryContext);
                    break;
                default:
                    await _view.ShowTipAsync("当前文档任务暂不支持失败项重试。");
                    break;
            }
        }

        public async Task ConvertSelectedDocumentsToTargetAsync(IList<object> selectedItems, string targetKey)
        {
            if (!_documentConversionService.TryParseTarget(targetKey, out var target))
            {
                await _view.ShowTipAsync("未知的目标格式。");
                return;
            }

            await ConvertSelectedDocumentsCoreAsync(selectedItems, target);
        }

        public async Task ConvertSelectedAudioToTargetAsync(IList<object> selectedItems, string targetKey)
        {
            if (!_audioConversionService.TryParseTarget(targetKey, out var target))
            {
                await _view.ShowTipAsync("未知的音频目标格式。");
                return;
            }

            await ConvertSelectedAudioCoreAsync(selectedItems, target);
        }

        public async Task RenameSelectedAudioByMetadataAsync(IList<object> selectedItems, string patternKey)
        {
            if (!_renameService.TryParseAudioRenamePattern(patternKey, out var pattern))
            {
                await _view.ShowTipAsync("未知的音频重命名规则。");
                return;
            }

            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要按元数据重命名的音频文件。");
                return;
            }

            if (_currentFolder == null)
            {
                return;
            }

            string patternName = _renameService.GetAudioRenamePatternDisplayName(pattern);
            SetBusy(true, "正在读取音频元数据...");
            SetAudioQueueState("音频队列：分析中", $"正在按“{patternName}”生成重命名预览。");

            var candidates = new ConcurrentBag<RenameCandidate>();
            var ghostPaths = new ConcurrentBag<string>();
            int metadataSkippedCount = 0;

            try
            {
                using var semaphore = new SemaphoreSlim(6);
                int processed = 0;
                int total = items.Count;

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var file = await TryGetStorageFileAsync(item.ImagePath!);
                        if (file == null)
                        {
                            ghostPaths.Add(item.ImagePath!);
                            return;
                        }

                        var metadata = await _audioMetadataService.TryReadAsync(file);
                        if (metadata == null || !_renameService.TryBuildAudioMetadataBaseName(metadata, pattern, out string baseName))
                        {
                            Interlocked.Increment(ref metadataSkippedCount);
                            return;
                        }

                        candidates.Add(new RenameCandidate(file, file.Path, file.Name, baseName));
                    }
                    catch (Exception ex)
                    {
                        MatrixLogService.LogError($"Audio_Rename_Analyze ({item.FileName ?? item.ImagePath})", ex);
                    }
                    finally
                    {
                        int current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0 || current == total)
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                ProgressValue = current;
                                ProgressMax = total;
                                StatusMainText = $"正在分析音频... {current}/{total}";
                                StatusDetailText = patternName;
                            });
                        }

                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                MatrixLogService.LogError("Audio_Rename_Process_Critical", ex);
                await _view.ShowTipAsync($"音频元数据重命名预处理失败: {ex.Message}");
                return;
            }
            finally
            {
                SetBusy(false);
            }

            RemoveGhostFiles(ghostPaths);
            RefreshViewFromCache();

            if (ghostPaths.Count > 0)
            {
                await _view.ShowTipAsync($"已自动清理 {ghostPaths.Count} 个在外部被删除的失效音频文件。");
            }

            var reservations = BuildDirectoryNameReservations();
            ReleaseOriginalNames(reservations, items);

            var previewItems = BuildRenamePreviewItems(
                candidates.OrderBy(candidate => candidate.OriginalPath, StringComparer.OrdinalIgnoreCase),
                reservations);

            var sortedPreview = previewItems
                .OrderBy(item => GetDirectoryPath(item.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.NewName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sortedPreview.Count == 0)
            {
                SetAudioQueueState("音频队列：未执行", metadataSkippedCount > 0
                    ? $"有 {metadataSkippedCount} 个文件缺少可用标题元数据，未生成重命名任务。"
                    : "当前所选音频已经符合命名规则，无需重命名。");
                await _view.ShowTipAsync(metadataSkippedCount > 0
                    ? "没有生成任何需要重命名的任务；缺少标题元数据的音频已跳过。"
                    : "当前所选音频已经符合命名规则，无需重命名。");
                return;
            }

            SetAudioQueueState("音频队列：待确认", $"已生成 {sortedPreview.Count} 个重命名预览，规则：{patternName}。");

            bool confirm = await _view.ShowRenamePreviewAsync(sortedPreview, metadataSkippedCount);
            if (!confirm)
            {
                SetAudioQueueState("音频队列：已取消", $"已取消“{patternName}”音频重命名。");
                return;
            }

            await PerformRenameFiles(sortedPreview);
            SetAudioQueueState("音频队列：已完成", $"音频元数据重命名已完成，共处理 {sortedPreview.Count} 个文件。");
        }

        public async Task ImportSelectedAudioTagsFromFileNameAsync(IList<object> selectedItems, string patternKey)
        {
            if (!_renameService.TryParseAudioRenamePattern(patternKey, out var pattern))
            {
                await _view.ShowTipAsync("未知的文件名标签规则。");
                return;
            }

            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要写入标签的音频文件。");
                return;
            }

            string patternName = _renameService.GetAudioRenamePatternDisplayName(pattern);
            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在按文件名写入音频标签...",
                    OperationName: "文件名写入标签",
                    DialogTitle: "文件名写入标签结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备按“{patternName}”将文件名写入标签。",
                    BuildQueueDetailText: (_, _, item) => $"正在按文件名写入标签：{item.FileName}",
                    ErrorLogContext: "Audio_Tag_Import",
                    ResolveFailureStatus: result => result.Message.Contains("文件名不符合", StringComparison.Ordinal)
                        ? DocumentConversionBatchItemStatus.Skipped
                        : DocumentConversionBatchItemStatus.Failed),
                (file, _) =>
                {
                    if (!_renameService.TryBuildAudioTagRequestFromFileName(file.Name, pattern, out var request))
                    {
                        return Task.FromResult(AudioTagUpdateResult.Failed(file.Path, $"文件名不符合“{patternName}”规则。"));
                    }

                    return _audioTagService.UpdateAsync(file, request);
                });
        }

        [RelayCommand]
        private async Task EditSelectedAudioTags(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要编辑标签的音频文件。");
                return;
            }

            var request = await _view.ShowAudioTagEditDialogAsync(BuildAudioTagEditSeed(items));
            if (request == null || !request.HasChanges)
            {
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在更新音频标签...",
                    OperationName: "音频标签编辑",
                    DialogTitle: "音频标签编辑结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备更新标签。",
                    BuildQueueDetailText: (_, _, item) => $"正在更新音频标签：{item.FileName}",
                    ErrorLogContext: "Audio_Tag_Edit"),
                (file, _) => _audioTagService.UpdateAsync(file, request));
        }

        [RelayCommand]
        private async Task ApplySelectedAudioCoverArt(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要设置封面的音频文件。");
                return;
            }

            var imageFile = await _view.PickImageFileAsync();
            if (imageFile == null)
            {
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在写入音频封面...",
                    OperationName: "音频封面写入",
                    DialogTitle: "音频封面写入结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备写入封面。",
                    BuildQueueDetailText: (_, _, item) => $"正在写入音频封面：{item.FileName}",
                    ErrorLogContext: "Audio_Cover_Apply"),
                (file, _) => _audioTagService.UpdateCoverArtAsync(file, imageFile));
        }

        [RelayCommand]
        private async Task ImportSelectedAudioCoverArtFromSidecar(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导入封面的音频文件。");
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在导入同名封面...",
                    OperationName: "同名封面导入",
                    DialogTitle: "同名封面导入结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备导入同名或目录封面。",
                    BuildQueueDetailText: (_, _, item) => $"正在导入同名封面：{item.FileName}",
                    ErrorLogContext: "Audio_Cover_Sidecar_Import",
                    ResolveFailureStatus: result => result.Message.Contains("未找到", StringComparison.Ordinal)
                        ? DocumentConversionBatchItemStatus.Skipped
                        : DocumentConversionBatchItemStatus.Failed),
                (file, _) => _audioTagService.ImportSidecarCoverArtAsync(file));
        }

        [RelayCommand]
        private async Task ClearSelectedAudioCoverArt(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要清除封面的音频文件。");
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在清除音频封面...",
                    OperationName: "音频封面清除",
                    DialogTitle: "音频封面清除结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备清除封面。",
                    BuildQueueDetailText: (_, _, item) => $"正在清除音频封面：{item.FileName}",
                    ErrorLogContext: "Audio_Cover_Clear"),
                (file, _) => _audioTagService.ClearCoverArtAsync(file));
        }

        [RelayCommand]
        private async Task ExportSelectedAudioCoverArt(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导出封面的音频文件。");
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在导出音频封面...",
                    OperationName: "音频封面导出",
                    DialogTitle: "音频封面导出结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备导出封面。",
                    BuildQueueDetailText: (_, _, item) => $"正在导出音频封面：{item.FileName}",
                    ErrorLogContext: "Audio_Cover_Export",
                    ResolveFailureStatus: result => result.Message.Contains("未嵌入", StringComparison.Ordinal)
                        ? DocumentConversionBatchItemStatus.Skipped
                        : DocumentConversionBatchItemStatus.Failed,
                    SuccessResultTargetKind: DocumentOperationResultTargetKind.File),
                (file, _) => _audioTagService.ExportCoverArtAsync(file));
        }

        [RelayCommand]
        private async Task ImportSelectedAudioLyricsFromFile(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导入歌词的音频文件。");
                return;
            }

            if (items.Count > 1)
            {
                await _view.ShowTipAsync("从歌词文件导入仅支持单个音频；批量请使用“从同名歌词导入”。");
                return;
            }

            var lyricsFile = await _view.PickLyricsFileAsync();
            if (lyricsFile == null)
            {
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在导入歌词文件...",
                    OperationName: "歌词文件导入",
                    DialogTitle: "歌词文件导入结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备导入歌词文件。",
                    BuildQueueDetailText: (_, _, item) => $"正在导入歌词文件：{item.FileName}",
                    ErrorLogContext: "Audio_Lyrics_File_Import"),
                (file, _) => _audioTagService.ImportLyricsFromFileAsync(file, lyricsFile));
        }

        [RelayCommand]
        private async Task ImportSelectedAudioLyricsFromSidecar(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导入歌词的音频文件。");
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在导入同名歌词...",
                    OperationName: "同名歌词导入",
                    DialogTitle: "同名歌词导入结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备导入同名歌词文件。",
                    BuildQueueDetailText: (_, _, item) => $"正在导入同名歌词：{item.FileName}",
                    ErrorLogContext: "Audio_Lyrics_Sidecar_Import",
                    ResolveFailureStatus: result => result.Message.Contains("未找到", StringComparison.Ordinal)
                        ? DocumentConversionBatchItemStatus.Skipped
                        : DocumentConversionBatchItemStatus.Failed),
                (file, _) => _audioTagService.ImportSidecarLyricsAsync(file));
        }

        [RelayCommand]
        private async Task ExportSelectedAudioLyrics(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导出歌词的音频文件。");
                return;
            }

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在导出音频歌词...",
                    OperationName: "音频歌词导出",
                    DialogTitle: "音频歌词导出结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备导出歌词。",
                    BuildQueueDetailText: (_, _, item) => $"正在导出音频歌词：{item.FileName}",
                    ErrorLogContext: "Audio_Lyrics_Export",
                    ResolveFailureStatus: result => result.Message.Contains("未嵌入", StringComparison.Ordinal)
                        ? DocumentConversionBatchItemStatus.Skipped
                        : DocumentConversionBatchItemStatus.Failed,
                    SuccessResultTargetKind: DocumentOperationResultTargetKind.File),
                (file, _) => _audioTagService.ExportLyricsAsync(file));
        }

        [RelayCommand]
        private async Task ExportSelectedAudioCatalog(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导出清单的音频文件。");
                return;
            }

            var orderedItems = OrderItemsByVisibleSequence(items);
            var entries = orderedItems
                .Select(CreateAudioCatalogExportEntry)
                .ToList();

            string outputDirectory = ResolveAudioPlaylistOutputDirectory(entries[0].Path);
            string defaultName = _audioCatalogExportService.BuildSuggestedName(_currentFolder?.Path ?? outputDirectory, entries.Count);
            string? fileName = await _view.ShowInputPromptAsync(
                "导出音频清单",
                $"将为所选 {entries.Count} 个音频导出 CSV 清单。",
                defaultName);

            if (fileName == null)
            {
                return;
            }

            string outputPath = _audioCatalogExportService.BuildOutputPath(outputDirectory, fileName);
            SetBusy(true, "正在导出音频清单...", 0, 1);
            SetAudioQueueState("音频队列：处理中", $"正在导出 {entries.Count} 个音频的元数据清单。");

            DocumentConversionBatchItem resultItem;
            try
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue = 0;
                    StatusMainText = "正在导出音频清单...";
                    StatusDetailText = Path.GetFileName(outputPath);
                });

                var result = await _audioCatalogExportService.ExportAsync(entries, outputPath);
                await TrackOutputPathAsync(result.OutputPath, DocumentOperationResultTargetKind.File);
                resultItem = new DocumentConversionBatchItem(
                    result.OutputPath,
                    result.OutputPath,
                    DocumentConversionBatchItemStatus.Succeeded,
                    result.Message,
                    DocumentOperationResultTargetKind.File);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue = 1;
                });
            }
            catch (Exception ex)
            {
                resultItem = new DocumentConversionBatchItem(
                    outputPath,
                    null,
                    DocumentConversionBatchItemStatus.Failed,
                    ex.Message);
                MatrixLogService.LogError("Audio_Catalog_Export", ex);
            }
            finally
            {
                SetBusy(false);
            }

            RefreshViewFromCache();

            var report = new DocumentConversionBatchReport(
                new[] { resultItem },
                operationName: "音频清单导出",
                dialogTitle: "音频清单导出结果");
            await PresentAudioOperationReportAsync(report);
        }

        [RelayCommand]
        private async Task ImportSelectedAudioCatalog(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导入清单的音频文件。");
                return;
            }

            var csvFile = await _view.PickCsvFileAsync();
            if (csvFile == null)
            {
                return;
            }

            IReadOnlyList<AudioCatalogImportRow> rows;
            try
            {
                rows = await _audioCatalogExportService.ParseImportRowsAsync(csvFile.Path);
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync($"读取清单失败: {ex.Message}");
                return;
            }

            if (rows.Count == 0)
            {
                await _view.ShowTipAsync("清单中没有可导入的数据行。");
                return;
            }

            var pathLookup = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Path))
                .GroupBy(row => row.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

            var fileNameLookup = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.FileName))
                .GroupBy(row => row.FileName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

            await RunAudioTagBatchAndPresentAsync(
                items,
                new AudioBatchOperationOptions(
                    BusyText: "正在导入音频清单...",
                    OperationName: "音频清单导入",
                    DialogTitle: "音频清单导入结果",
                    QueueReadyDetailText: $"已加入 {items.Count} 个音频，准备按清单更新标签。",
                    BuildQueueDetailText: (_, _, item) => $"正在导入音频清单：{item.FileName}",
                    ErrorLogContext: "Audio_Catalog_Import",
                    ResolveFailureStatus: result =>
                        result.Message.Contains("清单中未找到", StringComparison.Ordinal) ||
                        result.Message.Contains("没有可应用的标签字段", StringComparison.Ordinal)
                            ? DocumentConversionBatchItemStatus.Skipped
                            : DocumentConversionBatchItemStatus.Failed),
                (file, _) =>
                {
                    if (!TryFindCatalogRow(file, pathLookup, fileNameLookup, out var row))
                    {
                        return Task.FromResult(AudioTagUpdateResult.Failed(file.Path, "清单中未找到匹配的路径或唯一文件名。"));
                    }

                    if (!TryBuildAudioTagRequestFromCatalogRow(row, out var request))
                    {
                        return Task.FromResult(AudioTagUpdateResult.Failed(file.Path, "匹配到清单行，但没有可应用的标签字段。"));
                    }

                    return _audioTagService.UpdateAsync(file, request);
                });
        }

        [RelayCommand]
        private void ToggleAudioPreviewPlayback()
        {
            if (!CanControlAudioPreview)
            {
                return;
            }

            _audioPreviewService.TogglePlayPause();
        }

        [RelayCommand]
        private void SkipBackwardAudioPreview()
        {
            if (!CanControlAudioPreview)
            {
                return;
            }

            _audioPreviewService.Skip(TimeSpan.FromSeconds(-10));
        }

        [RelayCommand]
        private void SkipForwardAudioPreview()
        {
            if (!CanControlAudioPreview)
            {
                return;
            }

            _audioPreviewService.Skip(TimeSpan.FromSeconds(10));
        }

        [RelayCommand]
        private async Task PreviousAudioPreview()
        {
            await NavigateAudioPreviewAsync(-1, autoPlay: true, allowWrap: _audioPreviewLoopMode == AudioPreviewLoopMode.All);
        }

        [RelayCommand]
        private async Task NextAudioPreview()
        {
            await NavigateAudioPreviewAsync(1, autoPlay: true, allowWrap: _audioPreviewLoopMode == AudioPreviewLoopMode.All);
        }

        [RelayCommand]
        private void CycleAudioPreviewLoopMode()
        {
            _audioPreviewLoopMode = _audioPreviewLoopMode switch
            {
                AudioPreviewLoopMode.Off => AudioPreviewLoopMode.All,
                AudioPreviewLoopMode.All => AudioPreviewLoopMode.One,
                _ => AudioPreviewLoopMode.Off
            };

            OnPropertyChanged(nameof(AudioPreviewLoopModeText));
            UpdateAudioPreviewNavigationState();
        }

        public async Task UpdateAudioPreviewSelectionAsync(IList<object>? selectedItems)
        {
            if (!IsTypeAudio)
            {
                StopAudioPreview();
                return;
            }

            IsAudioPreviewVisible = true;

            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count != 1)
            {
                _audioPreviewService.Stop();
                _audioPreviewLoadedPath = null;
                ResetAudioPreviewToIdle(items.Count);
                UpdateAudioPreviewNavigationState();
                return;
            }

            var item = items[0];
            UpdateAudioPreviewSelectionInfo(item);

            if (string.Equals(_audioPreviewLoadedPath, item.ImagePath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyAudioPreviewState(_audioPreviewService.GetState());
                UpdateAudioPreviewNavigationState();
                return;
            }

            var file = await TryGetStorageFileAsync(item.ImagePath!);
            if (file == null)
            {
                RemoveGhostFiles(new[] { item.ImagePath! });
                RefreshViewFromCache();
                ResetAudioPreviewToIdle();
                return;
            }

            bool loaded = await _audioPreviewService.LoadAsync(file.Path);
            _audioPreviewLoadedPath = loaded ? file.Path : null;

            if (!loaded)
            {
                ResetAudioPreviewToIdle();
                AudioPreviewSubtitleText = "无法加载当前音频文件。";
                UpdateAudioPreviewNavigationState();
                return;
            }

            ApplyAudioPreviewState(_audioPreviewService.GetState());
            UpdateAudioPreviewNavigationState();
        }

        public void BeginAudioPreviewSeek()
        {
            _isAudioPreviewSeeking = true;
        }

        public void CommitAudioPreviewSeek(double value)
        {
            _isAudioPreviewSeeking = false;

            if (!CanControlAudioPreview)
            {
                return;
            }

            _audioPreviewService.Seek(TimeSpan.FromSeconds(value));
        }

        private async Task NavigateAudioPreviewAsync(int offset, bool autoPlay, bool allowWrap)
        {
            var playlist = GetVisibleAudioItems();
            if (playlist.Count == 0 || string.IsNullOrWhiteSpace(_audioPreviewLoadedPath))
            {
                return;
            }

            int currentIndex = playlist.FindIndex(item =>
                string.Equals(item.ImagePath, _audioPreviewLoadedPath, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                return;
            }

            int targetIndex = AudioPreviewService.ResolveAdjacentIndex(currentIndex, playlist.Count, offset, allowWrap);
            if (targetIndex < 0)
            {
                UpdateAudioPreviewNavigationState();
                return;
            }

            var targetItem = playlist[targetIndex];
            var file = await TryGetStorageFileAsync(targetItem.ImagePath!);
            if (file == null)
            {
                RemoveGhostFiles(new[] { targetItem.ImagePath! });
                RefreshViewFromCache();
                UpdateAudioPreviewNavigationState();
                return;
            }

            UpdateAudioPreviewSelectionInfo(targetItem);
            bool loaded = await _audioPreviewService.LoadAsync(file.Path);
            if (!loaded)
            {
                return;
            }

            _audioPreviewLoadedPath = file.Path;
            ApplyAudioPreviewState(_audioPreviewService.GetState());
            UpdateAudioPreviewNavigationState();

            if (autoPlay)
            {
                _audioPreviewService.Play();
            }
        }

        public void StopAudioPreview()
        {
            _audioPreviewLoadedPath = null;
            _isAudioPreviewSeeking = false;
            _audioPreviewService.Stop();
            ResetAudioPreviewToIdle();
        }

        public void ReleaseAudioPreview()
        {
            if (_isAudioPreviewSubscribed)
            {
                _audioPreviewService.StateChanged -= AudioPreviewService_StateChanged;
                _audioPreviewService.PlaybackEnded -= AudioPreviewService_PlaybackEnded;
                _isAudioPreviewSubscribed = false;
            }

            _audioPreviewService.Dispose();
        }

        [RelayCommand]
        private async Task ExportSelectedAudioSegments(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count < 2)
            {
                await _view.ShowTipAsync("请至少选择 2 个音频文件；单文件请直接使用“裁剪”。");
                return;
            }

            var trimRequest = await _view.ShowAudioTrimDialogAsync(
                $"已选择 {items.Count} 个音频文件",
                duration: null,
                isBatch: true);
            if (trimRequest == null)
            {
                return;
            }

            var report = await TrimAudioItemsAsync(
                items,
                trimRequest,
                busyText: "正在批量导出音频片段...",
                operationName: "音频片段导出",
                dialogTitle: "音频片段导出结果");

            await PresentAudioOperationReportAsync(report);
        }

        [RelayCommand]
        private async Task ImportAudioPlaylist()
        {
            StopAudioPreview();

            var playlistFile = await _view.PickPlaylistFileAsync();
            if (playlistFile == null)
            {
                return;
            }

            IReadOnlyList<string> playlistPaths;
            try
            {
                playlistPaths = await _audioPlaylistService.ParsePlaylistPathsAsync(playlistFile.Path);
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync($"读取播放列表失败: {ex.Message}");
                return;
            }

            if (playlistPaths.Count == 0)
            {
                await _view.ShowTipAsync("播放列表中没有可定位的音频路径。");
                return;
            }

            if (_currentFolder == null)
            {
                try
                {
                    string playlistDirectory = Path.GetDirectoryName(playlistFile.Path)
                        ?? throw new InvalidOperationException("无法确定播放列表所在目录。");
                    var folder = await StorageFolder.GetFolderFromPathAsync(playlistDirectory);
                    _currentFolder = folder;
                    await LoadFolderContentAsync(folder);
                }
                catch (Exception ex)
                {
                    await _view.ShowTipAsync($"载入播放列表目录失败: {ex.Message}");
                    return;
                }
            }

            var matchedPaths = playlistPaths
                .Where(IsPathUnderCurrentFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var matchedLookup = matchedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

            await _view.SelectItemsByPathsAsync(matchedPaths);

            var reportItems = playlistPaths
                .Select(path => new DocumentConversionBatchItem(
                    path,
                    null,
                    matchedLookup.Contains(path)
                        ? DocumentConversionBatchItemStatus.Succeeded
                        : DocumentConversionBatchItemStatus.Skipped,
                    matchedLookup.Contains(path)
                        ? "已在当前媒体库中定位并选中。"
                        : "当前数据源中未找到该音频。"))
                .ToList();

            var report = new DocumentConversionBatchReport(
                reportItems,
                operationName: "播放列表导入",
                dialogTitle: "播放列表导入结果");
            await PresentAudioOperationReportAsync(
                report,
                matchedPaths.Count > 0 ? "音频队列：已完成" : "音频队列：未匹配",
                $"播放列表共 {playlistPaths.Count} 个条目，已匹配 {matchedPaths.Count} 个，未匹配 {playlistPaths.Count - matchedPaths.Count} 个。");
        }

        [RelayCommand]
        private async Task ExportSelectedAudioPlaylist(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要导出的音频文件。");
                return;
            }

            var orderedItems = OrderItemsByVisibleSequence(items);
            var playlistEntries = new List<AudioPlaylistEntry>();
            var ghostPaths = new List<string>();

            foreach (var item in orderedItems)
            {
                var file = await TryGetStorageFileAsync(item.ImagePath!);
                if (file == null)
                {
                    ghostPaths.Add(item.ImagePath!);
                    continue;
                }

                playlistEntries.Add(CreateAudioPlaylistEntry(item, file.Path));
            }

            RemoveGhostFiles(ghostPaths);
            RefreshViewFromCache();

            if (playlistEntries.Count == 0)
            {
                await _view.ShowTipAsync("没有可导出的有效音频文件；失效条目已自动清理。");
                return;
            }

            string outputDirectory = ResolveAudioPlaylistOutputDirectory(playlistEntries[0].SourcePath);
            string defaultPlaylistName = _audioPlaylistService.BuildSuggestedName(_currentFolder?.Path ?? outputDirectory, playlistEntries.Count);
            string? playlistName = await _view.ShowInputPromptAsync(
                "导出播放列表",
                $"将为所选 {playlistEntries.Count} 个音频导出 .m3u8 播放列表。",
                defaultPlaylistName);

            if (playlistName == null)
            {
                return;
            }

            string outputPath = _audioPlaylistService.BuildOutputPath(outputDirectory, playlistName);
            SetBusy(true, "正在导出播放列表...", 0, 1);
            SetAudioQueueState("音频队列：处理中", $"正在导出 {playlistEntries.Count} 个音频到播放列表。");

            DocumentConversionBatchItem resultItem;
            try
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue = 0;
                    StatusMainText = "正在导出播放列表...";
                    StatusDetailText = Path.GetFileName(outputPath);
                });

                var result = await _audioPlaylistService.ExportAsync(playlistEntries, outputPath);
                resultItem = new DocumentConversionBatchItem(
                    result.PlaylistPath,
                    result.PlaylistPath,
                    DocumentConversionBatchItemStatus.Succeeded,
                    result.Message,
                    DocumentOperationResultTargetKind.File);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue = 1;
                });
            }
            catch (Exception ex)
            {
                resultItem = new DocumentConversionBatchItem(
                    outputPath,
                    null,
                    DocumentConversionBatchItemStatus.Failed,
                    ex.Message);
                MatrixLogService.LogError("Audio_Playlist_Export", ex);
            }
            finally
            {
                SetBusy(false);
            }

            var report = new DocumentConversionBatchReport(
                new[] { resultItem },
                operationName: "播放列表导出",
                dialogTitle: "播放列表导出结果");
            await PresentAudioOperationReportAsync(report);
        }

        [RelayCommand]
        private async Task TrimSelectedAudio(IList<object> selectedItems)
        {
            StopAudioPreview();
            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择 1 个音频文件。");
                return;
            }

            if (items.Count > 1)
            {
                await _view.ShowTipAsync("当前版本的音频裁剪先支持单文件处理，避免批量裁剪时误用同一时间区间。");
                return;
            }

            var item = items[0];
            if (!_audioConversionService.CanTrim(item.FileName))
            {
                await _view.ShowTipAsync("当前音频格式暂不支持直接裁剪。");
                return;
            }

            string sourcePath = item.ImagePath!;
            var file = await TryGetStorageFileAsync(sourcePath);
            if (file == null)
            {
                RemoveGhostFiles(new[] { sourcePath });
                RefreshViewFromCache();
                await _view.ShowTipAsync("源文件不存在，已自动从当前列表清理。");
                return;
            }

            var trimRequest = await _view.ShowAudioTrimDialogAsync(
                item.FileName ?? file.Name,
                item.AudioDuration > TimeSpan.Zero ? item.AudioDuration : null);
            if (trimRequest == null)
            {
                return;
            }

            var report = await TrimAudioItemsAsync(
                new List<ImageItem> { item },
                trimRequest,
                busyText: "正在裁剪音频...",
                operationName: "音频裁剪",
                dialogTitle: "音频裁剪结果");

            await PresentAudioOperationReportAsync(report);
        }

        [RelayCommand]
        private async Task MergeSelectedPdfFiles(IList<object> selectedItems)
        {
            var pdfItems = ExtractSelectedPdfItems(selectedItems);
            if (pdfItems.Count < 2)
            {
                await _view.ShowTipAsync("请至少选择 2 个 PDF 文件进行合并。");
                return;
            }

            SetBusy(true, "正在合并 PDF...", 0, 1);
            SetDocumentQueueState("转换队列：准备中", $"已加入 {pdfItems.Count} 个 PDF，等待合并。");

            DocumentConversionBatchItem result;
            try
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue = 0;
                    StatusMainText = "正在合并 PDF...";
                    StatusDetailText = $"{pdfItems.Count} 个文件";
                });

                result = await _pdfDocumentService.MergePdfFilesAsync(
                    pdfItems.Select(item => item.ImagePath!).ToList());

                if (result.Status == DocumentConversionBatchItemStatus.Succeeded &&
                    !string.IsNullOrWhiteSpace(result.OutputPath))
                {
                    await TrackOutputPathAsync(result.OutputPath, result.ResultTargetKind);
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue = 1;
                    StatusMainText = "正在合并 PDF... 1/1";
                });
            }
            finally
            {
                SetBusy(false);
            }

            RefreshViewFromCache();

            var report = new DocumentConversionBatchReport(
                new[] { result },
                operationName: "PDF 合并",
                dialogTitle: "PDF 合并结果");
            await PresentDocumentOperationReportAsync(report, null);
        }

        [RelayCommand]
        private async Task ShowLastAudioConversionResults()
        {
            if (_lastAudioConversionReport == null)
            {
                await _view.ShowTipAsync("暂无最近一次音频处理结果。");
                return;
            }

            await _view.ShowDocumentConversionResultsAsync(_lastAudioConversionReport);
        }

        [RelayCommand]
        private async Task SplitSelectedPdfFiles(IList<object> selectedItems)
        {
            var pdfItems = ExtractSelectedPdfItems(selectedItems);
            if (pdfItems.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要拆分的 PDF 文件。");
                return;
            }

            var report = await RunFileBatchOperationAsync(
                pdfItems,
                new FileBatchOperationOptions(
                    BusyText: "正在拆分 PDF...",
                    OperationName: "PDF 拆分",
                    DialogTitle: "PDF 拆分结果",
                    QueueLabel: "转换队列",
                    QueueReadyDetailText: $"已加入 {pdfItems.Count} 个 PDF，等待拆分。",
                    BuildQueueDetailText: (_, _, item) => $"正在拆分：{item.FileName}",
                    SetQueueState: SetDocumentQueueState,
                    ErrorLogContext: "Pdf_Split"),
                (file, _) => _pdfDocumentService.SplitPdfFileAsync(file.Path));
            await PresentDocumentOperationReportAsync(report, null);
        }

        [RelayCommand]
        private async Task ExtractSelectedPdfPages(IList<object> selectedItems)
        {
            var pdfItems = ExtractSelectedPdfItems(selectedItems);
            if (pdfItems.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要提取页面的 PDF 文件。");
                return;
            }

            string? pageSelectionText = await _view.ShowInputPromptAsync(
                "提取 PDF 页面",
                "输入页码范围，支持 1-3,5,8-10。多个 PDF 会应用同一规则。",
                "1");

            if (string.IsNullOrWhiteSpace(pageSelectionText))
            {
                return;
            }

            var report = await ExtractPdfPagesCoreAsync(pdfItems, pageSelectionText);
            await PresentDocumentOperationReportAsync(
                report,
                new DocumentRetryContext(DocumentRetryKind.ExtractPdfPages, null, pageSelectionText));
        }

        private async Task<DocumentConversionEnvironmentStatus> EnsureDocumentConversionEnvironmentAsync(bool showPrompt)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (DocumentConversionStatusText == "文档引擎：未检测")
                {
                    DocumentConversionStatusText = "文档引擎：检测中...";
                    DocumentConversionSupportText = "正在检测当前机器的 Word / Excel / PowerPoint 自动化接口。";
                }
            });

            var statusTask = _documentConversionEnvironmentTask ??= _documentConversionService.GetEnvironmentStatusAsync();
            var status = await statusTask;

            _dispatcherQueue.TryEnqueue(() =>
            {
                IsDocumentConversionAvailable = status.IsAnyAvailable;
                DocumentConversionStatusText = status.ShortText;
                DocumentConversionSupportText = status.DetailText;
            });

            if (showPrompt && !_hasShownDocumentConversionTip)
            {
                _hasShownDocumentConversionTip = true;
                string title = status.IsAnyAvailable
                    ? "文档转换环境检测完成。"
                    : "当前机器暂不支持文档格式转换。";
                await _view.ShowTipAsync($"{title}\n{status.DetailText}");
            }

            return status;
        }

        private async Task ConvertSelectedDocumentsCoreAsync(IList<object> selectedItems, DocumentConversionTarget target)
        {
            var environmentStatus = await EnsureDocumentConversionEnvironmentAsync(showPrompt: false);
            if (!environmentStatus.IsAnyAvailable)
            {
                await _view.ShowTipAsync($"当前机器未检测到可用的文档转换环境。\n{environmentStatus.DetailText}");
                return;
            }

            string targetName = _documentConversionService.GetTargetDisplayName(target);
            string targetExtension = _documentConversionService.GetTargetExtension(target);

            var items = ExtractSelectedItems(selectedItems)
                .Where(item => MediaFileCatalog.IsDocument(item.FileName))
                .ToList();

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要转换的文档文件。");
                return;
            }

            var reportItems = new List<DocumentConversionBatchItem>();
            var convertibleItems = new List<ImageItem>();

            foreach (var item in items)
            {
                if (!_documentConversionService.CanConvertToTarget(item.FileName, target))
                {
                    reportItems.Add(new DocumentConversionBatchItem(
                        item.ImagePath ?? string.Empty,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        $"该文件不支持转换为 {targetName}。"));
                    continue;
                }

                if (string.Equals(Path.GetExtension(item.FileName), targetExtension, StringComparison.OrdinalIgnoreCase))
                {
                    reportItems.Add(new DocumentConversionBatchItem(
                        item.ImagePath ?? string.Empty,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        $"源文件已经是 {targetName} 格式。"));
                    continue;
                }

                if (!_documentConversionService.IsConversionAvailable(item.FileName, target, environmentStatus))
                {
                    reportItems.Add(new DocumentConversionBatchItem(
                        item.ImagePath ?? string.Empty,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        $"当前机器缺少 {_documentConversionService.GetRequiredCapabilityDisplayName(item.FileName, target)}，无法转换为 {targetName}。"));
                    continue;
                }

                convertibleItems.Add(item);
            }

            if (convertibleItems.Count == 0)
            {
                var unavailableReport = new DocumentConversionBatchReport(
                    reportItems,
                    operationName: $"转 {targetName}",
                    dialogTitle: $"{targetName} 转换结果");
                await PresentDocumentOperationReportAsync(
                    unavailableReport,
                    new DocumentRetryContext(DocumentRetryKind.ConvertTarget, target, null),
                    "转换队列：未执行");
                return;
            }

            var report = await RunFileBatchOperationAsync(
                convertibleItems,
                new FileBatchOperationOptions(
                    BusyText: $"正在转换为 {targetName}...",
                    OperationName: $"转 {targetName}",
                    DialogTitle: $"{targetName} 转换结果",
                    QueueLabel: "转换队列",
                    QueueReadyDetailText: $"已加入 {items.Count} 个文件，目标：{targetName}",
                    BuildQueueDetailText: (_, _, item) => $"正在转换为 {targetName}：{item.FileName}",
                    SetQueueState: SetDocumentQueueState,
                    ErrorLogContext: $"Document_Convert -> {targetName}"),
                async (file, _) =>
                {
                    var result = await _documentConversionService.ConvertAsync(file.Path, target);
                    return result.Success && !string.IsNullOrWhiteSpace(result.OutputPath)
                        ? new DocumentConversionBatchItem(
                            file.Path,
                            result.OutputPath,
                            DocumentConversionBatchItemStatus.Succeeded,
                            result.Message,
                            DocumentOperationResultTargetKind.File)
                        : new DocumentConversionBatchItem(
                            file.Path,
                            null,
                            DocumentConversionBatchItemStatus.Failed,
                            result.Message);
                },
                seedItems: reportItems);
            await PresentDocumentOperationReportAsync(
                report,
                new DocumentRetryContext(DocumentRetryKind.ConvertTarget, target, null));
        }

        private async Task ConvertSelectedAudioCoreAsync(IList<object> selectedItems, AudioConversionTarget target)
        {
            StopAudioPreview();
            string targetName = _audioConversionService.GetTargetDisplayName(target);
            string targetExtension = _audioConversionService.GetTargetExtension(target);

            var items = ExtractSelectedAudioItems(selectedItems);

            if (items.Count == 0)
            {
                await _view.ShowTipAsync("请先选择要转换的音频文件。");
                return;
            }

            var reportItems = new List<DocumentConversionBatchItem>();
            var convertibleItems = new List<ImageItem>();

            foreach (var item in items)
            {
                if (!_audioConversionService.CanConvertToTarget(item.FileName, target))
                {
                    reportItems.Add(new DocumentConversionBatchItem(
                        item.ImagePath ?? string.Empty,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        $"该音频文件不支持转换为 {targetName}。"));
                    continue;
                }

                if (string.Equals(Path.GetExtension(item.FileName), targetExtension, StringComparison.OrdinalIgnoreCase))
                {
                    reportItems.Add(new DocumentConversionBatchItem(
                        item.ImagePath ?? string.Empty,
                        null,
                        DocumentConversionBatchItemStatus.Skipped,
                        $"源文件已经是 {targetName} 格式。"));
                    continue;
                }

                convertibleItems.Add(item);
            }

            if (convertibleItems.Count == 0)
            {
                var unavailableReport = new DocumentConversionBatchReport(
                    reportItems,
                    operationName: $"转 {targetName}",
                    dialogTitle: $"{targetName} 音频转换结果");
                await PresentAudioOperationReportAsync(unavailableReport, "音频队列：未执行");
                return;
            }

            var report = await RunFileBatchOperationAsync(
                convertibleItems,
                new FileBatchOperationOptions(
                    BusyText: $"正在转换音频为 {targetName}...",
                    OperationName: $"音频转 {targetName}",
                    DialogTitle: $"{targetName} 音频转换结果",
                    QueueLabel: "音频队列",
                    QueueReadyDetailText: $"已加入 {items.Count} 个文件，目标：{targetName}",
                    BuildQueueDetailText: (_, _, item) => $"正在转换为 {targetName}：{item.FileName}",
                    SetQueueState: SetAudioQueueState,
                    ErrorLogContext: $"Audio_Convert -> {targetName}"),
                async (file, _) =>
                {
                    var result = await _audioConversionService.ConvertAsync(file.Path, target);
                    return result.Success && !string.IsNullOrWhiteSpace(result.OutputPath)
                        ? new DocumentConversionBatchItem(
                            file.Path,
                            result.OutputPath,
                            DocumentConversionBatchItemStatus.Succeeded,
                            result.Message,
                            DocumentOperationResultTargetKind.File)
                        : new DocumentConversionBatchItem(
                            file.Path,
                            null,
                            DocumentConversionBatchItemStatus.Failed,
                            result.Message);
                },
                seedItems: reportItems);
            await PresentAudioOperationReportAsync(report);
        }

        private async Task<DocumentConversionBatchReport> TrimAudioItemsAsync(
            List<ImageItem> items,
            AudioTrimRequest trimRequest,
            string busyText,
            string operationName,
            string dialogTitle)
        {
            return await RunFileBatchOperationAsync(
                items,
                new FileBatchOperationOptions(
                    BusyText: busyText,
                    OperationName: operationName,
                    DialogTitle: dialogTitle,
                    QueueLabel: "音频队列",
                    QueueReadyDetailText: $"{operationName}：{items.Count} 个文件，区间 {trimRequest.RangeText}",
                    BuildQueueDetailText: (_, _, item) => $"正在处理：{item.FileName} · 区间 {trimRequest.RangeText}",
                    SetQueueState: SetAudioQueueState,
                    ErrorLogContext: $"Audio_Trim @ {trimRequest.RangeText}"),
                async (file, _) =>
                {
                    var result = await _audioConversionService.TrimAsync(file.Path, trimRequest);
                    return result.Success && !string.IsNullOrWhiteSpace(result.OutputPath)
                        ? new DocumentConversionBatchItem(
                            file.Path,
                            result.OutputPath,
                            DocumentConversionBatchItemStatus.Succeeded,
                            result.Message,
                            DocumentOperationResultTargetKind.File)
                        : new DocumentConversionBatchItem(
                            file.Path,
                            null,
                            DocumentConversionBatchItemStatus.Failed,
                            result.Message);
                });
        }

        private async Task<DocumentConversionBatchReport> ProcessSelectedImagesCoreAsync(
            List<ImageItem> items,
            List<DocumentConversionBatchItem> reportItems,
            string busyText,
            string operationName,
            string dialogTitle,
            string queueReadyText,
            string queueActionName,
            Func<string, Task<ImageProcessResult>> processAsync,
            string logKey)
        {
            return await RunFileBatchOperationAsync(
                items,
                new FileBatchOperationOptions(
                    BusyText: busyText,
                    OperationName: operationName,
                    DialogTitle: dialogTitle,
                    QueueLabel: "图片队列",
                    QueueReadyDetailText: queueReadyText,
                    BuildQueueDetailText: (_, _, item) => $"{queueActionName}：{item.FileName}",
                    SetQueueState: SetImageQueueState,
                    ErrorLogContext: logKey),
                async (file, _) =>
                {
                    var result = await processAsync(file.Path);
                    return result.Success && !string.IsNullOrWhiteSpace(result.OutputPath)
                        ? new DocumentConversionBatchItem(
                            file.Path,
                            result.OutputPath,
                            DocumentConversionBatchItemStatus.Succeeded,
                            result.Message,
                            DocumentOperationResultTargetKind.File)
                        : new DocumentConversionBatchItem(
                            file.Path,
                            null,
                            DocumentConversionBatchItemStatus.Failed,
                            result.Message);
                },
                seedItems: reportItems);
        }

        private async Task<DocumentConversionBatchReport> ExtractPdfPagesCoreAsync(
            List<ImageItem> pdfItems,
            string pageSelectionText)
        {
            return await RunFileBatchOperationAsync(
                pdfItems,
                new FileBatchOperationOptions(
                    BusyText: "正在提取 PDF 页面...",
                    OperationName: "PDF 页面提取",
                    DialogTitle: "PDF 页面提取结果",
                    QueueLabel: "转换队列",
                    QueueReadyDetailText: $"已加入 {pdfItems.Count} 个 PDF，页码：{pageSelectionText}",
                    BuildQueueDetailText: (_, _, item) => $"正在提取页面：{item.FileName} · 页码 {pageSelectionText}",
                    SetQueueState: SetDocumentQueueState,
                    ErrorLogContext: $"Pdf_ExtractPages -> {pageSelectionText}"),
                (file, _) => _pdfDocumentService.ExtractPagesAsync(file.Path, pageSelectionText));
        }

        private async Task<DocumentConversionBatchReport> RunFileBatchOperationAsync(
            IReadOnlyList<ImageItem> items,
            FileBatchOperationOptions options,
            Func<StorageFile, ImageItem, Task<DocumentConversionBatchItem>> processFileAsync,
            List<DocumentConversionBatchItem>? seedItems = null)
        {
            SetBusy(true, options.BusyText, 0, items.Count);
            options.SetQueueState($"{options.QueueLabel}：准备中", options.QueueReadyDetailText);

            var reportItems = seedItems ?? new List<DocumentConversionBatchItem>();
            var ghostPaths = new List<string>();

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string sourcePath = item.ImagePath!;
                    int current = i + 1;

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        ProgressValue = current - 1;
                        StatusMainText = $"{options.BusyText} {current}/{items.Count}";
                        StatusDetailText = item.FileName ?? string.Empty;
                    });

                    try
                    {
                        options.SetQueueState(
                            $"{options.QueueLabel}：{current}/{items.Count}",
                            options.BuildQueueDetailText(current, items.Count, item));

                        var file = await TryGetStorageFileAsync(sourcePath);
                        if (file == null)
                        {
                            ghostPaths.Add(sourcePath);
                            reportItems.Add(new DocumentConversionBatchItem(
                                sourcePath,
                                null,
                                DocumentConversionBatchItemStatus.Skipped,
                                "源文件不存在，已自动从当前列表清理。"));
                            continue;
                        }

                        var result = await processFileAsync(file, item);
                        reportItems.Add(result);

                        if (result.Status == DocumentConversionBatchItemStatus.Succeeded &&
                            !string.IsNullOrWhiteSpace(result.OutputPath))
                        {
                            await TrackOutputPathAsync(result.OutputPath, result.ResultTargetKind);
                        }
                    }
                    catch (Exception ex)
                    {
                        reportItems.Add(new DocumentConversionBatchItem(
                            sourcePath,
                            null,
                            DocumentConversionBatchItemStatus.Failed,
                            ex.Message));
                        MatrixLogService.LogError($"{options.ErrorLogContext} ({item.FileName ?? sourcePath})", ex);
                    }

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        ProgressValue = current;
                    });
                }
            }
            finally
            {
                SetBusy(false);
            }

            RemoveGhostFiles(ghostPaths);
            RefreshViewFromCache();

            return new DocumentConversionBatchReport(
                reportItems,
                operationName: options.OperationName,
                dialogTitle: options.DialogTitle);
        }

        private async Task RunAudioTagBatchAndPresentAsync(
            IReadOnlyList<ImageItem> items,
            AudioBatchOperationOptions options,
            Func<StorageFile, ImageItem, Task<AudioTagUpdateResult>> processFileAsync)
        {
            var report = await RunAudioTagBatchOperationAsync(items, options, processFileAsync);
            await PresentAudioOperationReportAsync(report);
        }

        private async Task<DocumentConversionBatchReport> RunAudioTagBatchOperationAsync(
            IReadOnlyList<ImageItem> items,
            AudioBatchOperationOptions options,
            Func<StorageFile, ImageItem, Task<AudioTagUpdateResult>> processFileAsync)
        {
            return await RunFileBatchOperationAsync(
                items,
                new FileBatchOperationOptions(
                    BusyText: options.BusyText,
                    OperationName: options.OperationName,
                    DialogTitle: options.DialogTitle,
                    QueueLabel: "音频队列",
                    QueueReadyDetailText: options.QueueReadyDetailText,
                    BuildQueueDetailText: options.BuildQueueDetailText,
                    SetQueueState: SetAudioQueueState,
                    ErrorLogContext: options.ErrorLogContext),
                async (file, item) =>
                {
                    var result = await processFileAsync(file, item);
                    if (result.Success)
                    {
                        await TrackFileInCacheAsync(file.Path);
                    }

                    return CreateAudioBatchItem(
                        string.IsNullOrWhiteSpace(result.SourcePath) ? file.Path : result.SourcePath,
                        result,
                        options.ResolveFailureStatus,
                        options.SuccessResultTargetKind);
                });
        }

        private static DocumentConversionBatchItem CreateAudioBatchItem(
            string sourcePath,
            AudioTagUpdateResult result,
            Func<AudioTagUpdateResult, DocumentConversionBatchItemStatus>? resolveFailureStatus,
            DocumentOperationResultTargetKind successResultTargetKind)
        {
            if (result.Success)
            {
                string? outputPath = string.IsNullOrWhiteSpace(result.OutputPath) ? null : result.OutputPath;
                return new DocumentConversionBatchItem(
                    sourcePath,
                    outputPath,
                    DocumentConversionBatchItemStatus.Succeeded,
                    result.Message,
                    outputPath == null ? DocumentOperationResultTargetKind.None : successResultTargetKind);
            }

            return new DocumentConversionBatchItem(
                sourcePath,
                null,
                resolveFailureStatus?.Invoke(result) ?? DocumentConversionBatchItemStatus.Failed,
                result.Message);
        }

        private async Task PresentImageOperationReportAsync(
            DocumentConversionBatchReport report,
            string queueStatusText = "图片队列：已完成",
            string? queueDetailText = null)
        {
            CacheImageOperationReport(report);
            SetImageQueueState(queueStatusText, queueDetailText ?? report.QueueSummaryText);
            await _view.ShowDocumentConversionResultsAsync(report);
        }

        private async Task PresentDocumentOperationReportAsync(
            DocumentConversionBatchReport report,
            DocumentRetryContext? retryContext,
            string queueStatusText = "转换队列：已完成",
            string? queueDetailText = null)
        {
            CacheDocumentConversionReport(report);
            SetDocumentRetryContext(retryContext);
            SetDocumentQueueState(queueStatusText, queueDetailText ?? report.QueueSummaryText);
            await _view.ShowDocumentConversionResultsAsync(report);
        }

        private async Task PresentAudioOperationReportAsync(
            DocumentConversionBatchReport report,
            string queueStatusText = "音频队列：已完成",
            string? queueDetailText = null)
        {
            CacheAudioConversionReport(report);
            SetAudioQueueState(queueStatusText, queueDetailText ?? report.QueueSummaryText);
            await _view.ShowDocumentConversionResultsAsync(report);
        }

        private async Task PresentContextualOperationReportAsync(DocumentConversionBatchReport report)
        {
            switch (CurrentMediaType)
            {
                case "Image":
                    await PresentImageOperationReportAsync(report);
                    break;
                case "Audio":
                    await PresentAudioOperationReportAsync(report);
                    break;
                case "Doc":
                    await PresentDocumentOperationReportAsync(report, retryContext: null, queueStatusText: "文档队列：已完成");
                    break;
                default:
                    await _view.ShowDocumentConversionResultsAsync(report);
                    break;
            }
        }

        private (string QueueLabel, Action<string, string> SetQueueState) GetContextualQueueDispatcher()
        {
            return CurrentMediaType switch
            {
                "Image" => ("图片队列", SetImageQueueState),
                "Audio" => ("音频队列", SetAudioQueueState),
                "Doc" => ("文档队列", SetDocumentQueueState),
                _ => ("处理队列", (_, _) => { })
            };
        }

        private void CacheImageOperationReport(DocumentConversionBatchReport report)
        {
            _lastImageOperationReport = report;
            OnPropertyChanged(nameof(HasImageOperationResults));
            OnPropertyChanged(nameof(LastImageOperationSummaryText));
        }

        private void CacheDocumentConversionReport(DocumentConversionBatchReport report)
        {
            _lastDocumentConversionReport = report;
            _documentReportHistory.RemoveAll(existing => ReferenceEquals(existing, report));
            _documentReportHistory.Insert(0, report);
            if (_documentReportHistory.Count > 12)
            {
                _documentReportHistory.RemoveRange(12, _documentReportHistory.Count - 12);
            }

            OnPropertyChanged(nameof(HasDocumentConversionResults));
            OnPropertyChanged(nameof(LastDocumentConversionSummaryText));
            OnPropertyChanged(nameof(HasDocumentTaskHistory));
            OnPropertyChanged(nameof(CanRetryFailedDocumentItems));
        }

        private void CacheAudioConversionReport(DocumentConversionBatchReport report)
        {
            _lastAudioConversionReport = report;
            OnPropertyChanged(nameof(HasAudioConversionResults));
            OnPropertyChanged(nameof(LastAudioConversionSummaryText));
        }

        private void SetDocumentQueueState(string statusText, string detailText)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                DocumentQueueStatusText = statusText;
                DocumentQueueDetailText = detailText;
            });
        }

        private void SetImageQueueState(string statusText, string detailText)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ImageQueueStatusText = statusText;
                ImageQueueDetailText = detailText;
            });
        }

        private void SetAudioQueueState(string statusText, string detailText)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                AudioQueueStatusText = statusText;
                AudioQueueDetailText = detailText;
            });
        }

        private async Task PerformRenameFiles(List<RenamePreviewItem> items)
        {
            StopAudioPreview();
            SetBusy(true, "正在重命名...", 0, items.Count);

            int successCount = 0;
            int failCount = 0;
            var renamedResults = new ConcurrentBag<(string OriginalPath, string NewPath, string NewName)>();

            await Task.Run(async () =>
            {
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

                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failCount);
                        MatrixLogService.LogError($"Rename_Execute ({item.OriginalName})", ex);
                    }

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
            RefreshViewFromCache();

            string message = $"重命名完成。\n成功: {successCount} 个\n失败: {failCount} 个";
            if (failCount > 0)
            {
                message += "\n失败项通常是重名冲突、文件占用或权限不足。";
            }

            await _view.ShowTipAsync(message);
        }

        private async Task LoadFolderContentAsync(StorageFolder folder)
        {
            SetBusy(true, "正在扫描文件...");
            StopAudioPreview();

            Images = null;
            lock (_cachedAllItems)
            {
                _cachedAllItems.Clear();
            }

            PathText = folder.Path;
            CountText = "0";
            IsEmptyStateVisible = false;

            try
            {
                var files = await folder.CreateFileQueryWithOptions(MediaFileCatalog.CreateAllMediaQueryOptions()).GetFilesAsync();
                if (files.Count == 0)
                {
                    IsEmptyStateVisible = true;
                    SetBusy(false);
                    return;
                }

                var concurrentItems = new ConcurrentBag<ImageItem>();
                using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

                var tasks = files.Select(async file =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var item = await CreateImageItemAsync(file);
                        concurrentItems.Add(item);
                    }
                    catch (Exception ex)
                    {
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
                    _cachedAllItems.AddRange(concurrentItems);
                }

                RefreshViewFromCache();

                int skippedCount = files.Count - concurrentItems.Count;
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
            finally
            {
                SetBusy(false);
            }
        }

        private void RefreshViewFromCache()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                List<ImageItem> snapshot;
                lock (_cachedAllItems)
                {
                    snapshot = _cachedAllItems.ToList();
                }

                if (snapshot.Count == 0)
                {
                    Images = null;
                    CountText = "0";
                    IsEmptyStateVisible = true;
                    return;
                }

                IEnumerable<ImageItem> filtered = CurrentMediaType switch
                {
                    "Image" => snapshot.Where(item => MediaFileCatalog.IsImage(item.FileName)),
                    "Audio" => snapshot.Where(item => MediaFileCatalog.IsAudio(item.FileName)),
                    "Doc" => snapshot.Where(item => MediaFileCatalog.IsDocument(item.FileName)),
                    _ => snapshot
                };

                var filteredList = filtered.ToList();
                CountText = filteredList.Count.ToString();
                IsEmptyStateVisible = filteredList.Count == 0;

                var sortedList = CurrentSortField switch
                {
                    "Date" => IsSortDescending
                        ? filteredList.OrderByDescending(item => item.DateCreated).ToList()
                        : filteredList.OrderBy(item => item.DateCreated).ToList(),
                    "Size" => IsSortDescending
                        ? filteredList.OrderByDescending(item => item.FileSize).ToList()
                        : filteredList.OrderBy(item => item.FileSize).ToList(),
                    _ => IsSortDescending
                        ? filteredList.OrderByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                        : filteredList.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                };

                _lastVisibleItems = sortedList.ToList();

                if (IsTypeAudio)
                {
                    if (!string.IsNullOrWhiteSpace(_audioPreviewLoadedPath) &&
                        !_lastVisibleItems.Any(item => string.Equals(item.ImagePath, _audioPreviewLoadedPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        StopAudioPreview();
                    }
                    else
                    {
                        UpdateAudioPreviewNavigationState();
                    }
                }

                int offset = 0;
                Images = new IncrementalLoadingCollection<ImageItem>((token, count) =>
                {
                    var batch = sortedList.Skip(offset).Take((int)count).ToList();
                    offset += batch.Count;
                    return Task.FromResult<IEnumerable<ImageItem>>(batch);
                });
            });
        }

        private async Task PerformDeleteFiles(List<StorageFile> files)
        {
            StopAudioPreview();
            SetBusy(true, "正在移至回收站...", 0, files.Count);

            int deletedCount = 0;

            await Task.Run(async () =>
            {
                using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

                var tasks = files.Select(async file =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        bool success = await _nativeFileService.MoveToRecycleBinAsync(file.Path);
                        if (!success)
                        {
                            throw new InvalidOperationException("移动到回收站失败，可能被占用或权限不足。");
                        }

                        lock (_cachedAllItems)
                        {
                            var cacheItem = _cachedAllItems.FirstOrDefault(item =>
                                string.Equals(item.ImagePath, file.Path, StringComparison.OrdinalIgnoreCase));

                            if (cacheItem != null)
                            {
                                _cachedAllItems.Remove(cacheItem);
                            }
                        }

                        await _mediaTagService.RemoveTagsAsync(new[] { file.Path });

                        int current = Interlocked.Increment(ref deletedCount);
                        if (current % 10 == 0 || current == files.Count)
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                ProgressValue = current;
                                StatusMainText = $"正在移至回收站... ({current}/{files.Count})";
                            });
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
                });

                await Task.WhenAll(tasks);
            });

            SetBusy(false);
            RefreshViewFromCache();
            await _view.ShowTipAsync($"清理完成，共移至回收站 {deletedCount} 个文件。");
        }

        private async Task<StorageFile?> TryGetStorageFileAsync(string path)
        {
            try
            {
                return await StorageFile.GetFileFromPathAsync(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex.HResult == unchecked((int)0x80070002))
            {
                return null;
            }
        }

        private List<ImageItem> ExtractSelectedItems(IList<object>? selectedItems)
        {
            return selectedItems?
                .OfType<ImageItem>()
                .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath))
                .ToList()
                ?? new List<ImageItem>();
        }

        private List<ImageItem> ExtractSelectedAudioItems(IList<object>? selectedItems)
        {
            return ExtractSelectedItems(selectedItems)
                .Where(item => MediaFileCatalog.IsAudio(item.FileName))
                .ToList();
        }

        private List<ImageItem> ExtractSelectedImageItems(IList<object>? selectedItems)
        {
            return ExtractSelectedItems(selectedItems)
                .Where(item => MediaFileCatalog.IsImage(item.FileName))
                .ToList();
        }

        private List<ImageItem> ExtractSelectedPdfItems(IList<object>? selectedItems)
        {
            return ExtractSelectedItems(selectedItems)
                .Where(item => _pdfDocumentService.IsPdf(item.FileName))
                .ToList();
        }

        private async Task<List<ImageItem>> ResolveItemsByPathsAsync(IEnumerable<string> paths)
        {
            var resolvedItems = new List<ImageItem>();

            foreach (string path in paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ImageItem? cachedItem;
                lock (_cachedAllItems)
                {
                    cachedItem = _cachedAllItems.FirstOrDefault(item =>
                        string.Equals(item.ImagePath, path, StringComparison.OrdinalIgnoreCase));
                }

                if (cachedItem != null)
                {
                    resolvedItems.Add(cachedItem);
                    continue;
                }

                var file = await TryGetStorageFileAsync(path);
                if (file != null && IsPathUnderCurrentFolder(path))
                {
                    resolvedItems.Add(await CreateImageItemAsync(file));
                }
            }

            return resolvedItems;
        }

        private async Task TrackFileInCacheAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !IsPathUnderCurrentFolder(filePath))
            {
                return;
            }

            var file = await TryGetStorageFileAsync(filePath);
            if (file == null)
            {
                return;
            }

            var trackedItem = await CreateImageItemAsync(file);

            lock (_cachedAllItems)
            {
                var existingItem = _cachedAllItems.FirstOrDefault(item =>
                    string.Equals(item.ImagePath, file.Path, StringComparison.OrdinalIgnoreCase));

                if (existingItem == null)
                {
                    _cachedAllItems.Add(trackedItem);
                    return;
                }

                existingItem.FileName = trackedItem.FileName;
                existingItem.ImagePath = trackedItem.ImagePath;
                existingItem.DateCreated = trackedItem.DateCreated;
                existingItem.FileSize = trackedItem.FileSize;
                existingItem.ImageWidth = trackedItem.ImageWidth;
                existingItem.ImageHeight = trackedItem.ImageHeight;
                existingItem.ImageFormat = trackedItem.ImageFormat;
                existingItem.ImageBitDepth = trackedItem.ImageBitDepth;
                existingItem.ImageDateTaken = trackedItem.ImageDateTaken;
                existingItem.AudioDuration = trackedItem.AudioDuration;
                existingItem.AudioArtist = trackedItem.AudioArtist;
                existingItem.AudioAlbum = trackedItem.AudioAlbum;
                existingItem.AudioAlbumArtist = trackedItem.AudioAlbumArtist;
                existingItem.AudioTitle = trackedItem.AudioTitle;
                existingItem.AudioComposer = trackedItem.AudioComposer;
                existingItem.AudioGenre = trackedItem.AudioGenre;
                existingItem.AudioTrackNumber = trackedItem.AudioTrackNumber;
                existingItem.AudioDiscNumber = trackedItem.AudioDiscNumber;
                existingItem.AudioYear = trackedItem.AudioYear;
                existingItem.AudioComment = trackedItem.AudioComment;
                existingItem.AudioLyrics = trackedItem.AudioLyrics;
                existingItem.AudioBitrate = trackedItem.AudioBitrate;
                existingItem.AudioSampleRate = trackedItem.AudioSampleRate;
                existingItem.HasEmbeddedCoverArt = trackedItem.HasEmbeddedCoverArt;
                existingItem.CustomTags = trackedItem.CustomTags;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                ImageItem? existingItem;
                lock (_cachedAllItems)
                {
                    existingItem = _cachedAllItems.FirstOrDefault(item =>
                        string.Equals(item.ImagePath, file.Path, StringComparison.OrdinalIgnoreCase));
                }

                if (existingItem == null)
                {
                    return;
                }

                existingItem.InvalidatePreview();
                _ = existingItem.LoadImageAsync(_dispatcherQueue);
            });

            if (string.Equals(_audioPreviewLoadedPath, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                UpdateAudioPreviewSelectionInfo(trackedItem);
            }
        }

        private async Task<ImageItem> CreateImageItemAsync(StorageFile file)
        {
            var properties = await file.GetBasicPropertiesAsync();
            var item = new ImageItem
            {
                FileName = file.Name,
                ImagePath = file.Path,
                DateCreated = file.DateCreated,
                FileSize = properties.Size,
                CustomTags = await _mediaTagService.GetTagsAsync(file.Path)
            };

            if (MediaFileCatalog.IsAudio(file.Name))
            {
                var metadata = await _audioMetadataService.TryReadAsync(file);
                if (metadata != null)
                {
                    item.AudioDuration = metadata.Duration;
                    item.AudioArtist = metadata.Artist;
                    item.AudioAlbum = metadata.Album;
                    item.AudioAlbumArtist = metadata.AlbumArtist;
                    item.AudioTitle = metadata.Title;
                    item.AudioComposer = metadata.Composer;
                    item.AudioGenre = metadata.Genre;
                    item.AudioTrackNumber = metadata.TrackNumber;
                    item.AudioDiscNumber = metadata.DiscNumber;
                    item.AudioYear = metadata.Year;
                    item.AudioComment = metadata.Comment;
                    item.AudioLyrics = metadata.Lyrics;
                    item.AudioBitrate = metadata.EncodingBitrate;
                    item.AudioSampleRate = metadata.SampleRate;
                    item.HasEmbeddedCoverArt = metadata.HasEmbeddedCoverArt;
                }
            }

            if (MediaFileCatalog.IsImage(file.Name))
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

        private async Task TrackOutputPathAsync(string outputPath, DocumentOperationResultTargetKind targetKind)
        {
            switch (targetKind)
            {
                case DocumentOperationResultTargetKind.File:
                    await TrackFileInCacheAsync(outputPath);
                    break;
                case DocumentOperationResultTargetKind.Folder:
                    foreach (string filePath in Directory.EnumerateFiles(outputPath, "*.pdf", SearchOption.TopDirectoryOnly))
                    {
                        await TrackFileInCacheAsync(filePath);
                    }
                    break;
            }
        }

        private AudioTagEditSeed BuildAudioTagEditSeed(IReadOnlyCollection<ImageItem> items)
        {
            return new AudioTagEditSeed(
                items.Count,
                items.FirstOrDefault()?.FileName ?? "音频文件",
                GetCommonStringValue(items.Select(item => item.AudioTitle)),
                GetCommonStringValue(items.Select(item => item.AudioArtist)),
                GetCommonStringValue(items.Select(item => item.AudioAlbum)),
                GetCommonUIntValue(items.Select(item => item.AudioTrackNumber)),
                GetCommonUIntValue(items.Select(item => item.AudioYear)),
                GetCommonStringValue(items.Select(item => item.AudioAlbumArtist)),
                GetCommonStringValue(items.Select(item => item.AudioComposer)),
                GetCommonStringValue(items.Select(item => item.AudioGenre)),
                GetCommonUIntValue(items.Select(item => item.AudioDiscNumber)),
                GetCommonStringValue(items.Select(item => item.AudioComment)),
                GetCommonStringValue(items.Select(item => item.AudioLyrics)),
                GetCommonBoolValue(items.Select(item => item.HasEmbeddedCoverArt)));
        }

        private List<ImageItem> OrderItemsByVisibleSequence(IEnumerable<ImageItem> items)
        {
            var orderMap = _lastVisibleItems
                .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath))
                .Select((item, index) => new { item.ImagePath, Index = index })
                .ToDictionary(entry => entry.ImagePath!, entry => entry.Index, StringComparer.OrdinalIgnoreCase);

            return items
                .OrderBy(item => orderMap.TryGetValue(item.ImagePath ?? string.Empty, out int index) ? index : int.MaxValue)
                .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private AudioPlaylistEntry CreateAudioPlaylistEntry(ImageItem item, string sourcePath)
        {
            string title = item.AudioTitle?.Trim() ?? string.Empty;
            string artist = item.AudioArtist?.Trim() ?? item.AudioAlbumArtist?.Trim() ?? string.Empty;
            string displayName = !string.IsNullOrWhiteSpace(title)
                ? (!string.IsNullOrWhiteSpace(artist) ? $"{artist} - {title}" : title)
                : Path.GetFileNameWithoutExtension(item.FileName ?? sourcePath);

            return new AudioPlaylistEntry(sourcePath, displayName, item.AudioDuration);
        }

        private AudioCatalogExportEntry CreateAudioCatalogExportEntry(ImageItem item)
        {
            return new AudioCatalogExportEntry(
                item.FileName ?? string.Empty,
                item.AudioTitle,
                item.AudioArtist,
                item.AudioAlbum,
                item.AudioAlbumArtist,
                item.AudioComposer,
                item.AudioGenre,
                item.AudioTrackNumber,
                item.AudioDiscNumber,
                item.AudioYear,
                item.AudioDuration,
                item.AudioBitrate,
                item.AudioSampleRate,
                item.HasEmbeddedCoverArt,
                item.ImagePath ?? string.Empty);
        }

        private static string BuildSharedTagInput(IEnumerable<ImageItem> items)
        {
            var materialized = items.ToList();
            if (materialized.Count == 0)
            {
                return string.Empty;
            }

            var commonTags = new HashSet<string>(
                materialized.First().CustomTags ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in materialized.Skip(1))
            {
                commonTags.IntersectWith(item.CustomTags ?? Array.Empty<string>());
                if (commonTags.Count == 0)
                {
                    break;
                }
            }

            return commonTags.Count == 0
                ? string.Empty
                : string.Join(", ", commonTags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
        }

        private static bool TryFindCatalogRow(
            StorageFile file,
            IReadOnlyDictionary<string, AudioCatalogImportRow> pathLookup,
            IReadOnlyDictionary<string, AudioCatalogImportRow> fileNameLookup,
            out AudioCatalogImportRow row)
        {
            if (pathLookup.TryGetValue(file.Path, out row!))
            {
                return true;
            }

            return fileNameLookup.TryGetValue(file.Name, out row!);
        }

        private static bool TryBuildAudioTagRequestFromCatalogRow(AudioCatalogImportRow row, out AudioTagEditRequest request)
        {
            request = new AudioTagEditRequest(
                row.HasTitle,
                row.Title,
                row.HasArtist,
                row.Artist,
                row.HasAlbum,
                row.Album,
                row.HasTrackNumber,
                row.TrackNumber,
                row.HasYear,
                row.Year,
                row.HasAlbumArtist,
                row.AlbumArtist,
                row.HasComposer,
                row.Composer,
                row.HasGenre,
                row.Genre,
                row.HasDiscNumber,
                row.DiscNumber);

            return request.HasChanges;
        }

        private string ResolveAudioPlaylistOutputDirectory(string fallbackSourcePath)
        {
            if (_currentFolder != null && !string.IsNullOrWhiteSpace(_currentFolder.Path))
            {
                return _currentFolder.Path;
            }

            string? directory = Path.GetDirectoryName(fallbackSourcePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }

            throw new InvalidOperationException("无法确定播放列表导出目录。");
        }

        private void AudioPreviewService_StateChanged(object? sender, AudioPreviewState state)
        {
            _dispatcherQueue.TryEnqueue(() => ApplyAudioPreviewState(state));
        }

        private void AudioPreviewService_PlaybackEnded(object? sender, EventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                switch (_audioPreviewLoopMode)
                {
                    case AudioPreviewLoopMode.One:
                        _audioPreviewService.Seek(TimeSpan.Zero);
                        _audioPreviewService.Play();
                        break;
                    case AudioPreviewLoopMode.All:
                        _ = NavigateAudioPreviewAsync(1, autoPlay: true, allowWrap: true);
                        break;
                    default:
                        _ = NavigateAudioPreviewAsync(1, autoPlay: true, allowWrap: false);
                        break;
                }
            });
        }

        private void ApplyAudioPreviewState(AudioPreviewState state)
        {
            CanControlAudioPreview = state.HasSource;
            AudioPreviewPlayPauseGlyph = state.IsPlaying ? "\uE769" : "\uE768";
            AudioPreviewPlayPauseText = state.IsPlaying ? "暂停" : "播放";
            AudioPreviewDurationText = AudioPreviewService.FormatTimestamp(state.Duration);
            AudioPreviewPositionText = AudioPreviewService.FormatTimestamp(state.Position);
            AudioPreviewSeekMaximum = Math.Max(1, state.Duration.TotalSeconds);

            if (!_isAudioPreviewSeeking)
            {
                AudioPreviewSeekValue = Math.Min(AudioPreviewSeekMaximum, Math.Max(0, state.Position.TotalSeconds));
            }

            UpdateAudioPreviewNavigationState();
        }

        private void ResetAudioPreviewToIdle(int selectedCount = 0)
        {
            IsAudioPreviewVisible = IsTypeAudio;
            CanControlAudioPreview = false;
            CanGoToPreviousAudioPreview = false;
            CanGoToNextAudioPreview = false;
            AudioPreviewQueueText = "- / -";
            AudioPreviewPlayPauseGlyph = "\uE768";
            AudioPreviewPlayPauseText = "播放";
            AudioPreviewPositionText = "00:00";
            AudioPreviewDurationText = "00:00";
            AudioPreviewSeekValue = 0;
            AudioPreviewSeekMaximum = 1;

            if (selectedCount > 1)
            {
                AudioPreviewTitleText = $"已选择 {selectedCount} 个音频";
                AudioPreviewSubtitleText = "预览播放器一次只绑定 1 个音频，请改为单选。";
                return;
            }

            AudioPreviewTitleText = "选择单个音频以开始预览";
            AudioPreviewSubtitleText = "支持播放、暂停、跳转和定位。";
        }

        private List<ImageItem> GetVisibleAudioItems()
        {
            return _lastVisibleItems
                .Where(item => MediaFileCatalog.IsAudio(item.FileName) && !string.IsNullOrWhiteSpace(item.ImagePath))
                .ToList();
        }

        private void UpdateAudioPreviewNavigationState()
        {
            var playlist = GetVisibleAudioItems();
            if (playlist.Count == 0 || string.IsNullOrWhiteSpace(_audioPreviewLoadedPath))
            {
                CanGoToPreviousAudioPreview = false;
                CanGoToNextAudioPreview = false;
                AudioPreviewQueueText = playlist.Count == 0 ? "- / -" : "未定位";
                return;
            }

            int currentIndex = playlist.FindIndex(item =>
                string.Equals(item.ImagePath, _audioPreviewLoadedPath, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                CanGoToPreviousAudioPreview = false;
                CanGoToNextAudioPreview = false;
                AudioPreviewQueueText = $"0 / {playlist.Count}";
                return;
            }

            bool hasMultipleItems = playlist.Count > 1;
            CanGoToPreviousAudioPreview = currentIndex > 0 || (_audioPreviewLoopMode == AudioPreviewLoopMode.All && hasMultipleItems);
            CanGoToNextAudioPreview = currentIndex < playlist.Count - 1 || (_audioPreviewLoopMode == AudioPreviewLoopMode.All && hasMultipleItems);
            AudioPreviewQueueText = $"{currentIndex + 1} / {playlist.Count}";
        }

        private void UpdateAudioPreviewSelectionInfo(ImageItem item)
        {
            AudioPreviewTitleText = item.AudioTitle?.Trim() is { Length: > 0 } title
                ? title
                : item.FileName ?? "音频文件";
            AudioPreviewSubtitleText = BuildAudioPreviewSubtitle(item);
        }

        private static string BuildAudioPreviewSubtitle(ImageItem item)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(item.AudioArtist))
            {
                parts.Add(item.AudioArtist!.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(item.AudioAlbumArtist))
            {
                parts.Add(item.AudioAlbumArtist!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(item.AudioAlbum))
            {
                parts.Add(item.AudioAlbum!.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(item.AudioGenre))
            {
                parts.Add(item.AudioGenre!.Trim());
            }

            if (item.AudioYear > 0)
            {
                parts.Add(item.AudioYear.ToString());
            }

            if (item.AudioBitrate > 0)
            {
                parts.Add(item.AudioBitrateString);
            }

            if (item.AudioSampleRate > 0)
            {
                parts.Add(item.AudioSampleRateString);
            }

            return parts.Count > 0
                ? string.Join(" · ", parts)
                : "已加载音频预览。";
        }

        private static string? GetCommonStringValue(IEnumerable<string?> values)
        {
            var distinctValues = values
                .Select(value => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return distinctValues.Count == 1
                ? string.IsNullOrWhiteSpace(distinctValues[0]) ? null : distinctValues[0]
                : null;
        }

        private static uint? GetCommonUIntValue(IEnumerable<uint> values)
        {
            var distinctValues = values
                .Distinct()
                .ToList();

            return distinctValues.Count == 1 && distinctValues[0] > 0
                ? distinctValues[0]
                : null;
        }

        private static bool GetCommonBoolValue(IEnumerable<bool> values)
        {
            var distinctValues = values
                .Distinct()
                .ToList();

            return distinctValues.Count == 1 && distinctValues[0];
        }

        private Dictionary<string, HashSet<string>> BuildDirectoryNameReservations()
        {
            lock (_cachedAllItems)
            {
                return _cachedAllItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath) && !string.IsNullOrWhiteSpace(item.FileName))
                    .GroupBy(item => GetDirectoryPath(item.ImagePath!))
                    .ToDictionary(
                        group => group.Key,
                        group => new HashSet<string>(group.Select(item => item.FileName!), StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        private void ReleaseOriginalNames(Dictionary<string, HashSet<string>> reservations, IEnumerable<ImageItem> items)
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

        private string ReserveUniqueName(
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

        private void RemoveGhostFiles(IEnumerable<string> ghostPaths)
        {
            var pathSet = ghostPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

            _ = _mediaTagService.RemoveTagsAsync(pathSet);
            RefreshViewFromCache();
        }

        private string BuildTimestampBaseName(DateTimeOffset timestamp)
        {
            return timestamp.TimeOfDay == TimeSpan.Zero
                ? timestamp.ToString("yyyy-MM-dd")
                : timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        private string GetCurrentMediaTypeDisplayName()
        {
            return CurrentMediaType switch
            {
                "Image" => "图片",
                "Audio" => "音频",
                "Doc" => "文档",
                _ => "全部文件"
            };
        }

        private void RaiseMediaTypeStateChanged()
        {
            OnPropertyChanged(nameof(IsTypeAll));
            OnPropertyChanged(nameof(IsTypeImage));
            OnPropertyChanged(nameof(IsTypeAudio));
            OnPropertyChanged(nameof(IsTypeDoc));
            OnPropertyChanged(nameof(EmptyStateText));
            OnPropertyChanged(nameof(ContextModeText));
            OnPropertyChanged(nameof(TabStatusText));
            OnPropertyChanged(nameof(EmptyStateIconGlyph));
        }

        private void SetBusy(bool busy, string text = "", double value = 0, double max = 100)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsBusy = busy;
                StatusMainText = busy ? text : "READY";
                StatusDetailText = string.Empty;
                IsProgressVisible = busy;
                ProgressValue = value;
                ProgressMax = max;
            });
        }

        private static string GetDirectoryPath(string path)
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }

        private static string BuildSiblingPath(string originalPath, string fileName)
        {
            return Path.Combine(GetDirectoryPath(originalPath), fileName);
        }

        private void SetDocumentRetryContext(DocumentRetryContext? retryContext)
        {
            _lastDocumentRetryContext = retryContext;
            OnPropertyChanged(nameof(CanRetryFailedDocumentItems));
        }

        private bool IsPathUnderCurrentFolder(string filePath)
        {
            if (_currentFolder == null || string.IsNullOrWhiteSpace(_currentFolder.Path))
            {
                return false;
            }

            string rootPath = Path.GetFullPath(_currentFolder.Path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string candidatePath = Path.GetFullPath(filePath);

            return candidatePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record AudioBatchOperationOptions(
            string BusyText,
            string OperationName,
            string DialogTitle,
            string QueueReadyDetailText,
            Func<int, int, ImageItem, string> BuildQueueDetailText,
            string ErrorLogContext,
            Func<AudioTagUpdateResult, DocumentConversionBatchItemStatus>? ResolveFailureStatus = null,
            DocumentOperationResultTargetKind SuccessResultTargetKind = DocumentOperationResultTargetKind.None);

        private sealed record FileBatchOperationOptions(
            string BusyText,
            string OperationName,
            string DialogTitle,
            string QueueLabel,
            string QueueReadyDetailText,
            Func<int, int, ImageItem, string> BuildQueueDetailText,
            Action<string, string> SetQueueState,
            string ErrorLogContext);

        private sealed record RenameCandidate(
            StorageFile File,
            string OriginalPath,
            string OriginalName,
            string BaseName);

        private enum DocumentRetryKind
        {
            ConvertTarget,
            ExtractPdfPages
        }

        private sealed record DocumentRetryContext(
            DocumentRetryKind Kind,
            DocumentConversionTarget? Target,
            string? PageSelectionText);
    }
}
