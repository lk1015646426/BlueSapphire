using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace BlueSapphire.ViewModels.Media
{
    // 图片批处理操作子 VM：删除、定位、标签、格式转换、高级编辑、增强与批处理执行核心。
    // 共享状态（busy/进度/取消机制/缓存/视图交互）经 IMediaWorkbenchContext 由主 VM 提供。
    public partial class MediaOperationsViewModel
    {
        private readonly NativeFileService _nativeFileService;
        private readonly ImageProcessingService _imageProcessingService;
        private readonly MediaTagService _mediaTagService;
        private readonly ILogger _logger;
        private readonly IMediaWorkbenchContext _context;

        public MediaOperationsViewModel(
            NativeFileService nativeFileService,
            ImageProcessingService imageProcessingService,
            MediaTagService mediaTagService,
            ILogger logger,
            IMediaWorkbenchContext context)
        {
            _nativeFileService = nativeFileService;
            _imageProcessingService = imageProcessingService;
            _mediaTagService = mediaTagService;
            _logger = logger;
            _context = context;
        }

        [RelayCommand]
        private async Task DeleteSelected(IList<object> selectedItems)
        {
            var items = _context.ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                return;
            }

            bool confirm = await _context.View.ShowDeleteConfirmationAsync(items.Count);
            if (!confirm)
            {
                return;
            }

            var files = new List<StorageFile>();
            var ghostPaths = new List<string>();

            foreach (var item in items)
            {
                var file = await _context.TryGetStorageFileAsync(item.ImagePath);
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

            await _context.RemoveGhostFilesAsync(ghostPaths);
            if (ghostPaths.Count > 0)
            {
                await _context.View.ShowTipAsync($"已自动清理 {ghostPaths.Count} 个在外部被删除的失效文件。");
            }

            if (files.Count > 0)
            {
                await PerformDeleteFilesAsync(files);
            }
        }

        [RelayCommand]
        private async Task OpenSelectedLocation(IList<object> selectedItems)
        {
            var items = _context.ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _context.View.ShowTipAsync("请先选择要打开位置的图片。");
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

                if (System.IO.File.Exists(item.ImagePath))
                {
                    validPaths.Add(item.ImagePath);
                }
                else
                {
                    ghostPaths.Add(item.ImagePath);
                }
            }

            await _context.RemoveGhostFilesAsync(ghostPaths);

            if (validPaths.Count == 0)
            {
                await _context.View.ShowTipAsync("未找到可打开的有效图片，已自动清理失效项。");
                return;
            }

            if (validPaths.Count == 1)
            {
                bool success = await _nativeFileService.RevealInExplorerAsync(validPaths[0]);
                await _context.View.ShowTipAsync(success ? "已定位到图片所在位置。" : "打开位置失败，请检查资源管理器是否可用。");
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

            await _context.View.ShowTipAsync(openedCount == 0
                ? "打开位置失败，请检查资源管理器是否可用。"
                : folderPaths.Count > 3
                    ? $"已打开 {openedCount} 个所在文件夹。为避免刷屏，仅打开前 3 个不同目录。"
                    : $"已打开 {openedCount} 个所在文件夹。");
        }

        [RelayCommand]
        private async Task EditSelectedMediaTags(IList<object> selectedItems)
        {
            var items = _context.ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _context.View.ShowTipAsync("请先选择要打标签的图片。");
                return;
            }

            string defaultText = BuildSharedTagInput(items);
            string? tagInput = await _context.View.ShowInputPromptAsync(
                "管理自定义标签",
                $"将为所选 {items.Count} 张图片写入自定义标签；使用逗号分隔，留空表示清空。",
                defaultText);

            if (tagInput == null)
            {
                return;
            }

            var tags = _mediaTagService.ParseTags(tagInput);
            _context.SetImageQueueState("图片队列：准备中", $"已加入 {items.Count} 张图片，准备更新自定义标签。");
            _context.SetBusy(true, "正在更新自定义标签...", 0, items.Count);

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

                _context.ReportProgress(
                    i + 1,
                    items.Count,
                    $"正在更新自定义标签... {i + 1}/{items.Count}",
                    item.FileName ?? item.ImagePath);
                _context.SetImageQueueState("图片队列：处理中", $"正在更新标签：{item.FileName}");

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

            _context.SetBusy(false);
            _context.SetImageQueueState("图片队列：已完成", $"自定义标签更新完成。成功 {successCount}，失败 {failCount}。");
            await _context.View.ShowTipAsync($"自定义标签更新完成。\n成功: {successCount} 张\n失败: {failCount} 张");
        }

        [RelayCommand]
        private async Task ShowLastImageOperationResults()
        {
            await _context.View.ShowTipAsync(_context.LastImageOperationSummaryText);
        }

        [RelayCommand]
        private async Task OpenFormatConvertDialog(IList<object> selectedItems)
        {
            var items = _context.ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _context.View.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var filePaths = items.Select(x => x.ImagePath).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList();
            var options = await _context.View.ShowFormatConvertDialogAsync(filePaths);
            if (options == null) return;

            string targetName = _imageProcessingService.GetTargetDisplayName(options.TargetFormat);
            string targetExtension = _imageProcessingService.GetTargetExtension(options.TargetFormat);

            var filteredItems = items
                .Where(item => !string.Equals(System.IO.Path.GetExtension(item.FileName), targetExtension, StringComparison.OrdinalIgnoreCase) || options.Quality < 0.95)
                .ToList();

            if (filteredItems.Count == 0)
            {
                await _context.View.ShowTipAsync("源文件已经是目标格式，无需转换。");
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
            var items = _context.ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _context.View.ShowTipAsync("请先选择要处理的图片。");
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
                await _context.View.ShowTipAsync("所选文件中没有受支持的图片格式。");
                return;
            }

            var options = await _context.View.ShowAdvancedEditorDialogAsync(previewPaths);
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
            var items = _context.ExtractSelectedItems(selectedItems);
            if (items.Count == 0)
            {
                await _context.View.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var options = await _context.View.ShowEnhanceDialogAsync(items.Count == 1 ? items[0].ImagePath : null);
            if (options == null) return;

            await RunImageOperationAndPresentAsync(
                items,
                $"正在增强图片...",
                $"自动增强",
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
                await _context.View.ShowTipAsync("请先选择要处理的图片。");
                return;
            }

            var result = await RunImageBatchOperationAsync(
                items,
                busyText,
                operationName,
                queueReadyText,
                buildQueueDetailText,
                processAsync);

            _context.CacheImageOperationSummary(result.SummaryText);
            await _context.View.ShowTipAsync(result.SummaryText);
        }

        private async Task<ImageOperationBatchResult> RunImageBatchOperationAsync(
            List<ImageItem> items,
            string busyText,
            string operationName,
            string queueReadyText,
            Func<int, int, ImageItem, string> buildQueueDetailText,
            Func<string, CancellationToken, Task<ImageProcessResult>> processAsync)
        {
            CancellationTokenSource operationCts = _context.BeginCancelableOperation();
            var token = operationCts.Token;
            _context.SetCancelAvailable(true);

            _context.SetImageQueueState("图片队列：准备中", queueReadyText);
            _context.SetBusy(true, busyText, 0, items.Count);

            int successCount = 0;
            int failCount = 0;
            int skippedCount = 0;
            int processedCount = 0;
            var messages = new List<string>();
            var ghostPaths = new List<string>();

            try
            {
                await Parallel.ForEachAsync(
                    items,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Min(2, Math.Max(1, Environment.ProcessorCount)),
                        CancellationToken = token
                    },
                    async (item, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();

                        int current = Interlocked.Increment(ref processedCount);
                        _context.ReportProgress(current, items.Count, $"{busyText} {current}/{items.Count}", item.FileName ?? string.Empty);
                        _context.SetImageQueueState($"图片队列：{current}/{items.Count}", buildQueueDetailText(current, items.Count, item));

                        var file = await _context.TryGetStorageFileAsync(item.ImagePath);
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

                        ct.ThrowIfCancellationRequested();

                        ImageProcessResult result;
                        try
                        {
                            result = await processAsync(file.Path, ct);
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
                                await _context.TrackOutputPathAsync(result.OutputPath);
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
                    });
            }
            catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
            {
                // Cancelled, message handled below
            }
            finally
            {
                _context.SetCancelAvailable(false);
                _context.SetBusy(false);
                _context.EndCancelableOperation(operationCts);
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

            await _context.RemoveGhostFilesAsync(ghostPaths, refreshView: false);
            await _context.RefreshViewFromCacheAsync();

            string summary = BuildOperationSummary(operationName, successCount, failCount, skippedCount, messages);
            _context.SetImageQueueState("图片队列：已完成", summary.Replace(Environment.NewLine, " "));
            return new ImageOperationBatchResult(summary, successCount, failCount, skippedCount);
        }

        // 供主 VM 的重复文件扫描流程复用（扫描确认后删除选中重复项）。
        public async Task PerformDeleteFilesAsync(List<StorageFile> files)
        {
            _context.SetBusy(true, "正在移至回收站...", 0, files.Count);

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
                _context.ReportProgress(
                    current,
                    files.Count,
                    $"正在移至回收站... ({current}/{files.Count})",
                    current == files.Count ? "处理完成" : chunk.Last().Name);
            }

            await _mediaTagService.RemoveTagsAsync(deletedPaths);
            _context.RemoveCachedItemsByPaths(deletedPaths);

            _context.SetBusy(false);
            await _context.RefreshViewFromCacheAsync();

            await _context.View.ShowTipAsync($"删除完成。\n成功移至回收站: {success} 个\n失败: {fail} 个");
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

        private static string GetDirectoryPath(string? path)
        {
            return System.IO.Path.GetDirectoryName(path ?? string.Empty) ?? string.Empty;
        }

        private sealed record ImageOperationBatchResult(string SummaryText, int SuccessCount, int FailCount, int SkippedCount);
    }
}
