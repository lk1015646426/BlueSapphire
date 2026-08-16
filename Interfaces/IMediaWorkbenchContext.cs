using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;
using Windows.Storage;

namespace BlueSapphire.Interfaces
{
    /// <summary>
    /// 媒体工作台子 ViewModel 访问主 VM 共享会话状态（视图交互、busy/进度、取消机制、图片缓存）的契约。
    /// 由 <see cref="ViewModels.MediaManagerViewModel"/> 实现，供 MediaRenameViewModel / MediaOperationsViewModel 消费。
    /// </summary>
    public interface IMediaWorkbenchContext
    {
        /// <summary>宿主视图交互（提示、对话框、选择器）。</summary>
        IMediaViewInteraction View { get; }

        /// <summary>最近一次图片批处理结果摘要文本。</summary>
        string LastImageOperationSummaryText { get; }

        /// <summary>设置全局 busy 状态与进度条（内部调度到 UI 线程）。</summary>
        void SetBusy(bool busy, string text = "", double value = 0, double max = 100);

        /// <summary>设置图片队列状态文本（内部调度到 UI 线程）。</summary>
        void SetImageQueueState(string statusText, string detailText);

        /// <summary>在 UI 线程执行指定操作。</summary>
        void RunOnUi(Action action);

        /// <summary>在 UI 线程上批量更新进度条与状态文本；传 null 表示不改对应文本。</summary>
        void ReportProgress(double value, double max, string? mainText = null, string? detailText = null);

        /// <summary>从选中项集合提取有效图片项。</summary>
        List<ImageItem> ExtractSelectedItems(IList<object>? selectedItems);

        /// <summary>按路径解析 StorageFile；不存在或不可访问时返回 null。</summary>
        Task<StorageFile?> TryGetStorageFileAsync(string? path);

        /// <summary>开始一个可取消操作，返回其 CTS（会取消并替换上一个全局 CTS）。</summary>
        CancellationTokenSource BeginCancelableOperation();

        /// <summary>结束可取消操作并释放 CTS。</summary>
        void EndCancelableOperation(CancellationTokenSource operation);

        /// <summary>设置“取消操作”按钮可用性。</summary>
        void SetCancelAvailable(bool available);

        /// <summary>依据当前搜索/标签过滤与排序规则，从缓存刷新图片视图。</summary>
        Task RefreshViewFromCacheAsync();

        /// <summary>从缓存移除幽灵文件（磁盘上已不存在），并同步清理标签库。</summary>
        Task RemoveGhostFilesAsync(IEnumerable<string> ghostPaths, bool refreshView = true);

        /// <summary>将图片批处理输出文件登记进缓存（仅当位于当前文件夹内）。</summary>
        Task TrackOutputPathAsync(string outputPath);

        /// <summary>获取图片缓存快照（锁内拷贝）。</summary>
        IReadOnlyList<ImageItem> GetCachedItemsSnapshot();

        /// <summary>按路径集合从缓存移除条目。</summary>
        void RemoveCachedItemsByPaths(IEnumerable<string> paths);

        /// <summary>把重命名结果（原路径 → 新路径/新文件名）应用回缓存。</summary>
        void ApplyRenameToCache(IReadOnlyList<(string OriginalPath, string NewPath, string NewName)> renames);

        /// <summary>缓存最近一次图片批处理摘要并通知绑定更新。</summary>
        void CacheImageOperationSummary(string summary);
    }
}
