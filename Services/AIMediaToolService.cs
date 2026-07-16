using BlueSapphire.Helpers;
using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class AIMediaToolService
    {
        private const int MaxFiles = 20_000;
        private const long LargeFileThreshold = 20L * 1024L * 1024L;
        private readonly AITaskCenterService _taskCenter;
        private readonly AISharedContextService _sharedContext;
        private readonly NativeFileService _nativeFileService;
        private readonly MediaTagService _mediaTagService;

        public AIMediaToolService(
            AITaskCenterService taskCenter,
            AISharedContextService sharedContext,
            NativeFileService nativeFileService,
            MediaTagService mediaTagService)
        {
            _taskCenter = taskCenter;
            _sharedContext = sharedContext;
            _nativeFileService = nativeFileService;
            _mediaTagService = mediaTagService;
        }

        public async Task<AIMediaAnalysisContext> AnalyzeFolderAsync(
            string folderPath,
            bool recursive,
            CancellationToken cancellationToken)
        {
            string normalizedFolder = NormalizeFolder(folderPath);
            string idempotencyKey =
                $"media.analyze:{normalizedFolder}:{recursive}:{Directory.GetLastWriteTimeUtc(normalizedFolder):O}";
            using AITaskLease task = _taskCenter.Begin(
                "media.analyze",
                "媒体目录分析",
                $"正在分析 {Path.GetFileName(normalizedFolder)}",
                idempotencyKey);
            if (task.IsDuplicate)
            {
                AIMediaAnalysisContext? cached = _sharedContext.GetMediaAnalysis(TimeSpan.FromMinutes(10));
                if (cached != null &&
                    string.Equals(cached.FolderPath, normalizedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return cached;
                }
                throw new InvalidOperationException("相同媒体分析任务正在执行，请在任务中心查看进度。");
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                task.Token);
            try
            {
                SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                List<FileInfo> files = await Task.Run(() =>
                    Directory.EnumerateFiles(normalizedFolder, "*", searchOption)
                        .Where(MediaFileCatalog.IsImage)
                        .Take(MaxFiles + 1)
                        .Select(path => new FileInfo(path))
                        .Where(file => file.Exists)
                        .ToList(), linked.Token);

                if (files.Count > MaxFiles)
                {
                    throw new InvalidOperationException($"目录中的图片超过 {MaxFiles:N0} 张，请缩小扫描范围。");
                }

                _taskCenter.Report(task.TaskId, 10, "媒体目录分析", $"已发现 {files.Count:N0} 张图片");
                var duplicateGroups = new List<List<string>>();
                List<IGrouping<long, FileInfo>> sizeGroups = files
                    .GroupBy(file => file.Length)
                    .Where(group => group.Count() > 1)
                    .ToList();
                int hashedFiles = 0;
                int hashFileTotal = Math.Max(1, sizeGroups.Sum(group => group.Count()));

                foreach (IGrouping<long, FileInfo> sizeGroup in sizeGroups)
                {
                    var hashes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    foreach (FileInfo file in sizeGroup)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        string hash = await ComputeSha256Async(file.FullName, linked.Token);
                        if (!hashes.TryGetValue(hash, out List<string>? paths))
                        {
                            paths = new List<string>();
                            hashes[hash] = paths;
                        }
                        paths.Add(file.FullName);
                        hashedFiles++;
                        _taskCenter.Report(
                            task.TaskId,
                            10 + (hashedFiles / (double)hashFileTotal * 80),
                            "正在校验重复图片",
                            $"{hashedFiles:N0}/{hashFileTotal:N0}");
                    }
                    duplicateGroups.AddRange(hashes.Values.Where(paths => paths.Count > 1));
                }

                Dictionary<string, int> formatCounts = files
                    .GroupBy(file => file.Extension.TrimStart('.').ToUpperInvariant())
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
                int lowResolutionCount = await CountLowResolutionCandidatesAsync(files, linked.Token);
                var context = new AIMediaAnalysisContext
                {
                    FolderPath = normalizedFolder,
                    FileCount = files.Count,
                    TotalBytes = files.Sum(file => file.Length),
                    ExactDuplicateGroupCount = duplicateGroups.Count,
                    SimilarCandidateGroupCount = 0,
                    LargeFileCount = files.Count(file => file.Length >= LargeFileThreshold),
                    LowResolutionCount = lowResolutionCount,
                    FormatCounts = formatCounts,
                    ExactDuplicateGroups = duplicateGroups
                };
                _sharedContext.SetMediaAnalysis(context);
                _taskCenter.Complete(
                    task.TaskId,
                    $"分析完成：{files.Count:N0} 张图片，{duplicateGroups.Count:N0} 组完全重复候选。");
                return context;
            }
            catch (OperationCanceledException)
            {
                _taskCenter.MarkCancelled(task.TaskId);
                throw;
            }
            catch (Exception ex)
            {
                _taskCenter.Fail(task.TaskId, ex.Message);
                throw;
            }
        }

        public AIMediaOrganizationPreview BuildOrganizationPreview(
            string folderPath,
            bool recursive)
        {
            string normalizedFolder = NormalizeFolder(folderPath);
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            List<AIMediaMovePreview> moves = Directory
                .EnumerateFiles(normalizedFolder, "*", option)
                .Where(MediaFileCatalog.IsImage)
                .Take(MaxFiles)
                .Select(path =>
                {
                    DateTime createdAt = File.GetCreationTime(path);
                    string destination = Path.Combine(
                        normalizedFolder,
                        createdAt.ToString("yyyy"),
                        createdAt.ToString("MM"),
                        Path.GetFileName(path));
                    return new AIMediaMovePreview
                    {
                        SourcePath = path,
                        DestinationPath = destination,
                        Reason = "按文件创建年月归档"
                    };
                })
                .Where(item => !string.Equals(
                    item.SourcePath,
                    item.DestinationPath,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var preview = new AIMediaOrganizationPreview
            {
                FolderPath = normalizedFolder,
                Moves = moves
            };
            _sharedContext.SetMediaOrganizationPreview(preview);
            return preview;
        }

        public async Task<(int Success, int Failed, int Skipped)> ExecuteOrganizationPreviewAsync(
            CancellationToken cancellationToken)
        {
            AIMediaOrganizationPreview? preview =
                _sharedContext.GetMediaOrganizationPreview(TimeSpan.FromMinutes(30));
            if (preview == null)
            {
                throw new InvalidOperationException("媒体整理预览已过期，请重新生成。");
            }

            using AITaskLease task = _taskCenter.Begin(
                "media.organize",
                "媒体归档",
                $"按年月整理 {preview.Moves.Count:N0} 张图片",
                $"media.organize:{preview.CreatedAt:O}:{preview.FolderPath}");
            if (task.IsDuplicate)
            {
                throw new InvalidOperationException("相同的媒体归档任务已经执行或正在执行。");
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                task.Token);
            int success = 0;
            int failed = 0;
            int skipped = 0;
            try
            {
                for (int index = 0; index < preview.Moves.Count; index++)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    AIMediaMovePreview move = preview.Moves[index];
                    if (!File.Exists(move.SourcePath))
                    {
                        skipped++;
                        continue;
                    }
                    if (File.Exists(move.DestinationPath))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        string? directory = Path.GetDirectoryName(move.DestinationPath);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.Move(move.SourcePath, move.DestinationPath);
                        await _mediaTagService.MoveTagsAsync(move.SourcePath, move.DestinationPath);
                        success++;
                    }
                    catch
                    {
                        failed++;
                    }

                    _taskCenter.Report(
                        task.TaskId,
                        (index + 1) / (double)Math.Max(1, preview.Moves.Count) * 100,
                        "正在整理媒体",
                        $"{index + 1:N0}/{preview.Moves.Count:N0}");
                }

                _taskCenter.Complete(
                    task.TaskId,
                    $"整理完成：成功 {success:N0}，失败 {failed:N0}，跳过 {skipped:N0}。");
                return (success, failed, skipped);
            }
            catch (OperationCanceledException)
            {
                _taskCenter.MarkCancelled(task.TaskId);
                throw;
            }
            catch (Exception ex)
            {
                _taskCenter.Fail(task.TaskId, ex.Message);
                throw;
            }
        }

        public IReadOnlyList<string> BuildExactDuplicateDeletionPreview(string keepStrategy)
        {
            AIMediaAnalysisContext? context = _sharedContext.GetMediaAnalysis(TimeSpan.FromMinutes(30));
            if (context == null)
            {
                return Array.Empty<string>();
            }

            return context.ExactDuplicateGroups
                .SelectMany(group =>
                {
                    IEnumerable<string> ordered = string.Equals(
                        keepStrategy,
                        "oldest",
                        StringComparison.OrdinalIgnoreCase)
                        ? group.OrderBy(File.GetLastWriteTimeUtc)
                        : group.OrderByDescending(File.GetLastWriteTimeUtc);
                    return ordered.Skip(1);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<(int Success, int Failed)> DeleteExactDuplicateCandidatesAsync(
            IReadOnlyList<string> approvedPaths,
            CancellationToken cancellationToken)
        {
            AIMediaAnalysisContext? context = _sharedContext.GetMediaAnalysis(TimeSpan.FromMinutes(30));
            if (context == null)
            {
                throw new InvalidOperationException("媒体分析结果已过期，请重新分析。");
            }

            HashSet<string> allowed = context.ExactDuplicateGroups
                .SelectMany(group => group)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<string> targets = approvedPaths
                .Where(path => allowed.Contains(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targets.Count == 0)
            {
                return (0, 0);
            }

            using AITaskLease task = _taskCenter.Begin(
                "media.duplicate-cleanup",
                "重复图片清理",
                $"将 {targets.Count:N0} 张已确认图片移入回收站",
                $"media.delete:{string.Join("|", targets.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))}");
            if (task.IsDuplicate)
            {
                throw new InvalidOperationException("相同的重复图片清理任务已经执行或正在执行。");
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                task.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                List<string> successPaths = await _nativeFileService.MoveToRecycleBinBatchAsync(targets);
                int failed = targets.Count - successPaths.Count;
                _taskCenter.Complete(
                    task.TaskId,
                    $"已移入回收站 {successPaths.Count:N0} 张，失败 {failed:N0} 张。");
                return (successPaths.Count, failed);
            }
            catch (OperationCanceledException)
            {
                _taskCenter.MarkCancelled(task.TaskId);
                throw;
            }
            catch (Exception ex)
            {
                _taskCenter.Fail(task.TaskId, ex.Message);
                throw;
            }
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private static async Task<int> CountLowResolutionCandidatesAsync(
            IReadOnlyList<FileInfo> files,
            CancellationToken cancellationToken)
        {
            int count = 0;
            foreach (FileInfo file in files.Take(500))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Windows.Storage.StorageFile storageFile =
                        await Windows.Storage.StorageFile.GetFileFromPathAsync(file.FullName);
                    Windows.Storage.FileProperties.ImageProperties properties =
                        await storageFile.Properties.GetImagePropertiesAsync();
                    if (properties.Width > 0 &&
                        properties.Height > 0 &&
                        properties.Width * properties.Height < 640 * 480)
                    {
                        count++;
                    }
                }
                catch
                {
                }
            }
            return count;
        }

        private static string NormalizeFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("必须提供媒体目录。", nameof(folderPath));
            }
            string normalized = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"')));
            if (!Directory.Exists(normalized))
            {
                throw new DirectoryNotFoundException("媒体目录不存在或当前不可访问。");
            }
            return normalized;
        }
    }
}
