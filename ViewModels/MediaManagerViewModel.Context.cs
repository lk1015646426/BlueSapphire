using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using Windows.Storage;

namespace BlueSapphire.ViewModels
{
    // IMediaWorkbenchContext 实现分部：向 Media/MediaRenameViewModel 与 MediaOperationsViewModel
    // 暴露共享会话状态（视图交互、busy/进度、取消机制、图片缓存），显式接口实现保持主 VM 公共面干净。
    public partial class MediaManagerViewModel : IMediaWorkbenchContext
    {
        IMediaViewInteraction IMediaWorkbenchContext.View => _view;

        void IMediaWorkbenchContext.SetBusy(bool busy, string text, double value, double max) => SetBusy(busy, text, value, max);

        void IMediaWorkbenchContext.SetImageQueueState(string statusText, string detailText) => SetImageQueueState(statusText, detailText);

        void IMediaWorkbenchContext.RunOnUi(Action action) => RunOnUi(action);

        void IMediaWorkbenchContext.ReportProgress(double value, double max, string? mainText, string? detailText)
        {
            RunOnUi(() =>
            {
                ProgressValue = value;
                ProgressMax = max;
                if (mainText != null)
                {
                    StatusMainText = mainText;
                }

                if (detailText != null)
                {
                    StatusDetailText = detailText;
                }
            });
        }

        List<ImageItem> IMediaWorkbenchContext.ExtractSelectedItems(IList<object>? selectedItems) => ExtractSelectedItems(selectedItems);

        Task<StorageFile?> IMediaWorkbenchContext.TryGetStorageFileAsync(string? path) => TryGetStorageFileAsync(path);

        CancellationTokenSource IMediaWorkbenchContext.BeginCancelableOperation() => BeginCancelableOperation();

        void IMediaWorkbenchContext.EndCancelableOperation(CancellationTokenSource operation) => EndCancelableOperation(operation);

        void IMediaWorkbenchContext.SetCancelAvailable(bool available) => CanCancelOperation = available;

        Task IMediaWorkbenchContext.RefreshViewFromCacheAsync() => RefreshViewFromCacheAsync();

        Task IMediaWorkbenchContext.RemoveGhostFilesAsync(IEnumerable<string> ghostPaths, bool refreshView) => RemoveGhostFilesAsync(ghostPaths, refreshView);

        Task IMediaWorkbenchContext.TrackOutputPathAsync(string outputPath) => TrackOutputPathAsync(outputPath);

        IReadOnlyList<ImageItem> IMediaWorkbenchContext.GetCachedItemsSnapshot()
        {
            lock (_cachedAllItems)
            {
                return _cachedAllItems.ToList();
            }
        }

        void IMediaWorkbenchContext.RemoveCachedItemsByPaths(IEnumerable<string> paths)
        {
            var pathSet = paths
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
        }

        void IMediaWorkbenchContext.ApplyRenameToCache(IReadOnlyList<(string OriginalPath, string NewPath, string NewName)> renames)
        {
            lock (_cachedAllItems)
            {
                foreach (var renamed in renames)
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
        }

        void IMediaWorkbenchContext.CacheImageOperationSummary(string summary)
        {
            _lastImageOperationSummary = summary;
            OnPropertyChanged(nameof(HasImageOperationResults));
            OnPropertyChanged(nameof(LastImageOperationSummaryText));
        }
    }
}
