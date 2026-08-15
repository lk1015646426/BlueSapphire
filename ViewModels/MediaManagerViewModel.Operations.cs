using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace BlueSapphire.ViewModels
{
    // 图片批处理操作分部：删除、定位、标签、格式转换、高级编辑、增强与批处理执行核心。
    public partial class MediaManagerViewModel
    {
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
            CancellationTokenSource operationCts = BeginCancelableOperation();
            var token = operationCts.Token;
            CanCancelOperation = true;

            SetImageQueueState("图片队列：准备中", queueReadyText);
            SetBusy(true, busyText, 0, items.Count);

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
                    });
            }
            catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
            {
                // Cancelled, message handled below
            }
            finally
            {
                CanCancelOperation = false;
                SetBusy(false);
                EndCancelableOperation(operationCts);
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

        private sealed record ImageOperationBatchResult(string SummaryText, int SuccessCount, int FailCount, int SkippedCount);
    }
}
