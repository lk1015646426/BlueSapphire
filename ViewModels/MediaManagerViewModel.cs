using System;
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
    // 主分部：依赖与服务、可绑定状态、导航与排序命令、重复扫描、可取消操作基建与共享工具。
    // 批量重命名见 MediaManagerViewModel.Rename.cs；图片批处理见 MediaManagerViewModel.Operations.cs；
    // 图片库加载与视图刷新见 MediaManagerViewModel.Library.cs。
    public partial class MediaManagerViewModel : ObservableObject
    {
        private readonly MediaRenameService _renameService;
        private readonly MediaDeduplicationService _deduplicationService;
        private readonly NativeFileService _nativeFileService;
        private readonly ImageProcessingService _imageProcessingService;
        private readonly ImageMetadataService _imageMetadataService;
        private readonly MediaTagService _mediaTagService;
        private readonly ILogger<MediaManagerViewModel> _logger;
        private readonly AISharedContextService? _sharedContext;
        private readonly List<ImageItem> _cachedAllItems = new();

        private IMediaViewInteraction _view = null!;
        private DispatcherQueue? _dispatcherQueue;
        private StorageFolder? _currentFolder;
        private readonly object _cancellationSync = new();
        private CancellationTokenSource? _globalCts;
        private readonly CancellationTokenSource _metadataCts = new();
        private List<ImageItem> _lastVisibleItems = new();
        private string? _lastImageOperationSummary;
        private CancellationTokenSource? _filterDebounceCts;
        private long _viewRefreshVersion;

        private IncrementalLoadingCollection<ImageItem>? _images;
        public IncrementalLoadingCollection<ImageItem>? Images
        {
            get => _images;
            set => SetProperty(ref _images, value);
        }

        private string _statusMainText = "就绪";
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

        private string _pathText = "尚未选择文件夹";
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
        private bool _hasVisibleImages;
        public bool HasVisibleImages
        {
            get => _hasVisibleImages;
            private set
            {
                if (SetProperty(ref _hasVisibleImages, value))
                {
                    OnPropertyChanged(nameof(HasNoFilterResults));
                }
            }
        }
        public bool HasNoFilterResults => HasImages && !HasVisibleImages;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    ScheduleFilterRefresh();
                }
            }
        }

        private string _tagFilterMode = "All";
        public string TagFilterMode
        {
            get => _tagFilterMode;
            set
            {
                if (SetProperty(ref _tagFilterMode, value ?? "All"))
                {
                    ScheduleFilterRefresh();
                }
            }
        }

        private bool _canCancelOperation;
        public bool CanCancelOperation
        {
            get => _canCancelOperation;
            private set => SetProperty(ref _canCancelOperation, value);
        }
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
        public string TabStatusText => "图片库";
        public string EmptyStateIconGlyph => "\uE8B9";

        public MediaManagerViewModel(
            MediaRenameService renameService,
            MediaDeduplicationService deduplicationService,
            NativeFileService nativeFileService,
            ImageProcessingService imageProcessingService,
            ImageMetadataService imageMetadataService,
            MediaTagService mediaTagService,
            ILogger<MediaManagerViewModel> logger,
            AISharedContextService? sharedContext = null)
        {
            _renameService = renameService;
            _deduplicationService = deduplicationService;
            _nativeFileService = nativeFileService;
            _imageProcessingService = imageProcessingService;
            _imageMetadataService = imageMetadataService;
            _mediaTagService = mediaTagService;
            _logger = logger;
            _sharedContext = sharedContext;
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
            _sharedContext?.SetCurrentMediaFolder(folder.Path);
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
                    _sharedContext?.SetCurrentMediaFolder(_currentFolder.Path);
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
        private void CancelOperation()
        {
            var cts = _globalCts;
            if (cts != null && !cts.IsCancellationRequested)
            {
                StatusDetailText = "正在取消操作...";
                // CTS 可能已被替换/释放，取消尽力而为。
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
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

            CancellationTokenSource operationCts = BeginCancelableOperation();
            var token = operationCts.Token;
            CanCancelOperation = true;

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

                bool isSimilarScan = string.Equals(mode, "Similar", StringComparison.OrdinalIgnoreCase);
                var filesToDelete = await _view.ShowDuplicateResultsAsync(
                    finalDuplicates,
                    isSimilarScan);
                if (filesToDelete.Count > 0)
                {
                    await PerformDeleteFilesAsync(filesToDelete);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SetBusy(false);
                StatusMainText = "扫描已取消";
                StatusDetailText = string.Empty;
            }
            catch (Exception ex)
            {
                SetBusy(false);
                _logger.LogError(ex, "Scan_Duplicates_Critical");
                await _view.ShowTipAsync($"扫描中断: {ex.Message}");
            }
            finally
            {
                CanCancelOperation = false;
                EndCancelableOperation(operationCts);
            }
        }

        private void ScheduleFilterRefresh()
        {
            CancellationTokenSource next = new();
            lock (_cancellationSync)
            {
                _filterDebounceCts?.Cancel();
                _filterDebounceCts = next;
            }
            _ = RefreshFilterAfterDelayAsync(next);
        }

        public void CancelPendingOperations()
        {
            Interlocked.Increment(ref _viewRefreshVersion);
            lock (_cancellationSync)
            {
                _globalCts?.Cancel();
                _globalCts = null;
                _filterDebounceCts?.Cancel();
                _filterDebounceCts = null;
            }
            _metadataCts.Cancel();

            lock (_cachedAllItems)
            {
                foreach (ImageItem item in _cachedAllItems)
                {
                    item.CancelLoad();
                }
            }
        }

        private CancellationTokenSource BeginCancelableOperation()
        {
            var next = new CancellationTokenSource();
            lock (_cancellationSync)
            {
                _globalCts?.Cancel();
                _globalCts = next;
            }
            return next;
        }

        private void EndCancelableOperation(CancellationTokenSource operation)
        {
            lock (_cancellationSync)
            {
                if (ReferenceEquals(_globalCts, operation))
                {
                    _globalCts = null;
                }
            }
            operation.Dispose();
        }

        private async Task RefreshFilterAfterDelayAsync(CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(180, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    await RefreshViewFromCacheAsync(showBusy: false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_cancellationSync)
                {
                    if (ReferenceEquals(_filterDebounceCts, cts))
                    {
                        _filterDebounceCts = null;
                    }
                }
                cts.Dispose();
            }
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

        private void SetBusy(bool busy, string text = "", double value = 0, double max = 100)
        {
            RunOnUi(() =>
            {
                IsBusy = busy;
                IsProgressVisible = busy;
                ProgressValue = value;
                ProgressMax = max;
                StatusMainText = busy ? text : "就绪";
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
    }
}
