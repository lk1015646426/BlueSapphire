using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace BlueSapphire.ViewModels
{
    // 批量重命名分部：命令入口、时间解析、预览构建与执行。
    public partial class MediaManagerViewModel
    {
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
                int processed = 0;

                await Parallel.ForEachAsync(
                    items,
                    new ParallelOptions { MaxDegreeOfParallelism = 8 },
                    async (item, _) =>
                    {
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
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Rename_Analyze ({FileName})", item.FileName ?? item.ImagePath);
                    }
                    finally
                    {
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
                });
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

        private static string BuildTimestampBaseName(DateTimeOffset timestamp)
        {
            return timestamp.TimeOfDay == TimeSpan.Zero
                ? timestamp.ToString("yyyy-MM-dd")
                : timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        private sealed record RenameCandidate(StorageFile File, string OriginalPath, string OriginalName, string BaseName);
    }
}
