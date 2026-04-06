using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerExecutionService
    {
        private readonly NativeFileService _nativeFileService;
        private readonly CleanerStateStore _stateStore;
        private readonly CleanerLockService _lockService;
        private readonly CleanerPrivilegeService _privilegeService;
        private readonly CleanerBoundaryGuard _boundaryGuard;

        public CleanerExecutionService(
            NativeFileService nativeFileService,
            CleanerStateStore stateStore,
            CleanerLockService lockService,
            CleanerPrivilegeService privilegeService,
            CleanerBoundaryGuard boundaryGuard)
        {
            _nativeFileService = nativeFileService;
            _stateStore = stateStore;
            _lockService = lockService;
            _privilegeService = privilegeService;
            _boundaryGuard = boundaryGuard;
        }

        public async Task<CleanerCleanupBatch> ExecuteAsync(
            IReadOnlyList<CleanerScanItem> items,
            CleanerScanScope scope,
            IProgress<CleanerExecutionProgress>? progress,
            CancellationToken cancellationToken)
        {
            CleanerCleanupBatch batch = new()
            {
                BatchId = DateTimeOffset.Now.ToString("yyyyMMddHHmmss"),
                CreatedAt = DateTimeOffset.Now,
                Scope = scope,
                SelectedItemCount = items.Count,
                EstimatedBytes = items.Sum(item => item.SizeBytes)
            };

            int completedItems = 0;
            foreach (CleanerScanItem item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new CleanerExecutionProgress
                {
                    StageTitle = "执行清理",
                    Detail = $"正在处理：{item.Name}",
                    ProgressValue = completedItems,
                    ProgressMax = Math.Max(1, items.Count)
                });

                IReadOnlyList<string> targets = ResolveTargets(item);
                if (targets.Count == 0)
                {
                    completedItems++;
                    continue;
                }

                foreach (string target in targets)
                {
                    CleanerCleanupEntry entry = await ExecuteTargetAsync(batch.BatchId, item, target);
                    batch.Entries.Add(entry);
                }

                completedItems++;
            }

            batch.ReleasedBytes = batch.Entries
                .Where(entry => string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .Sum(entry => entry.SizeBytes);

            List<CleanerCleanupBatch> history = (await _stateStore.LoadHistoryAsync()).ToList();
            history.Insert(0, batch);
            if (history.Count > 20)
            {
                history = history.Take(20).ToList();
            }

            await _stateStore.SaveHistoryAsync(history);

            progress?.Report(new CleanerExecutionProgress
            {
                StageTitle = "执行清理",
                Detail = "清理完成",
                ProgressValue = items.Count,
                ProgressMax = Math.Max(1, items.Count)
            });

            return batch;
        }

        public async Task<CleanerRestoreSummary> RestoreLatestAsync(CancellationToken cancellationToken)
        {
            List<CleanerCleanupBatch> history = (await _stateStore.LoadHistoryAsync()).ToList();
            CleanerCleanupBatch? batch = history.FirstOrDefault(candidate =>
                candidate.Entries.Any(entry => entry.CanRestore && !entry.Restored));

            if (batch == null)
            {
                return new CleanerRestoreSummary
                {
                    Message = "没有可恢复的隔离批次。"
                };
            }

            CleanerRestoreSummary summary = new();
            foreach (CleanerCleanupEntry entry in batch.Entries.Where(entry => entry.CanRestore && !entry.Restored))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RestoreEntryCoreAsync(entry, summary);
            }

            await _stateStore.SaveHistoryAsync(history);

            summary.Message = summary.RestoredCount > 0
                ? $"已恢复 {summary.RestoredCount} 项，回写 {CleanerSizeFormatter.Format(summary.RestoredBytes)}。"
                : "没有项目被成功恢复。";

            return summary;
        }

        public async Task<CleanerRestoreSummary> RestoreEntryAsync(string batchId, string entryId, CancellationToken cancellationToken)
        {
            List<CleanerCleanupBatch> history = (await _stateStore.LoadHistoryAsync()).ToList();
            CleanerCleanupBatch? batch = history.FirstOrDefault(candidate => string.Equals(candidate.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
            CleanerCleanupEntry? entry = batch?.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.EntryId, entryId, StringComparison.OrdinalIgnoreCase));

            if (entry == null || !entry.CanRestoreEntry)
            {
                return new CleanerRestoreSummary
                {
                    Message = "没有找到可恢复的清理条目。"
                };
            }

            CleanerRestoreSummary summary = new();
            cancellationToken.ThrowIfCancellationRequested();
            await RestoreEntryCoreAsync(entry, summary);
            await _stateStore.SaveHistoryAsync(history);

            summary.Message = summary.RestoredCount > 0
                ? $"已恢复 {entry.ItemName}，回写 {CleanerSizeFormatter.Format(summary.RestoredBytes)}。"
                : $"未能恢复 {entry.ItemName}。";

            return summary;
        }

        public async Task<CleanerCleanupBatch?> RetryFailedEntriesAsync(
            string batchId,
            IProgress<CleanerExecutionProgress>? progress,
            CancellationToken cancellationToken)
        {
            List<CleanerCleanupBatch> history = (await _stateStore.LoadHistoryAsync()).ToList();
            CleanerCleanupBatch? batch = history.FirstOrDefault(candidate => string.Equals(candidate.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
            if (batch == null)
            {
                return null;
            }

            List<CleanerCleanupEntry> retryEntries = batch.Entries
                .Where(entry => entry.CanRetryEntry)
                .ToList();

            int processed = 0;
            foreach (CleanerCleanupEntry entry in retryEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CleanerExecutionProgress
                {
                    StageTitle = "重试失败项",
                    Detail = $"正在重试：{entry.ItemName}",
                    ProgressValue = processed,
                    ProgressMax = Math.Max(1, retryEntries.Count)
                });

                await RetryEntryCoreAsync(entry);
                processed++;
            }

            batch.ReleasedBytes = batch.Entries
                .Where(entry => string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .Sum(entry => entry.SizeBytes);

            await _stateStore.SaveHistoryAsync(history);
            return batch;
        }

        private async Task<CleanerCleanupEntry> ExecuteTargetAsync(string batchId, CleanerScanItem item, string targetPath)
        {
            CleanerCleanupEntry entry = new()
            {
                EntryId = Guid.NewGuid().ToString("N"),
                ItemId = item.ObjectId,
                RuleId = item.RuleId,
                ItemName = item.Name,
                Category = item.Category,
                OriginalPath = targetPath,
                SizeBytes = CalculateSize(targetPath),
                ExecutionMode = item.ExecutionMode,
                RiskLevel = item.RiskLevel,
                RequiresElevation = item.RequiresElevation,
                BoundaryRoots = item.BoundaryRoots.ToList(),
                Status = "Pending",
                CanRestore = item.ExecutionMode == CleanerExecutionMode.Quarantine,
                LockedByProcesses = item.LockedByProcesses.ToList()
            };

            try
            {
                CleanerBoundaryValidationResult boundary = _boundaryGuard.Validate(item, targetPath, _privilegeService.IsElevated);
                if (!boundary.IsAllowed)
                {
                    entry.Status = "Failed";
                    entry.FailureReason = boundary.FailureReason;
                    entry.ErrorMessage = boundary.Message;
                    return entry;
                }

                if (CleanerPathSafety.IsReparsePoint(targetPath))
                {
                    entry.Status = "Failed";
                    entry.FailureReason = CleanerFailureReason.ReparsePointSkipped;
                    entry.ErrorMessage = "检测到符号链接或 Junction，已跳过以避免跨目录误删。";
                    return entry;
                }

                switch (item.ExecutionMode)
                {
                    case CleanerExecutionMode.Recycle:
                        entry.Status = await _nativeFileService.MoveToRecycleBinAsync(targetPath) ? "Completed" : "Failed";
                        if (entry.Status != "Completed")
                        {
                            entry.FailureReason = CleanerFailureReason.Unknown;
                            entry.ErrorMessage = "移动到回收站失败。";
                        }

                        break;
                    case CleanerExecutionMode.Quarantine:
                        string destination = BuildQuarantinePath(batchId, targetPath);
                        await MovePathAsync(targetPath, destination);
                        entry.BackupPath = destination;
                        entry.Status = "Completed";
                        break;
                    case CleanerExecutionMode.Permanent:
                        DeletePath(targetPath);
                        entry.Status = "Completed";
                        entry.CanRestore = false;
                        break;
                    default:
                        entry.Status = "Skipped";
                        break;
                }
            }
            catch (Exception ex)
            {
                entry.Status = "Failed";
                entry.FailureReason = ClassifyFailure(ex);
                entry.ErrorMessage = ex.Message;
                if (entry.FailureReason == CleanerFailureReason.InUse && entry.LockedByProcesses.Count == 0)
                {
                    entry.LockedByProcesses = ResolveLockingProcesses(targetPath).ToList();
                }
            }

            return entry;
        }

        private async Task RetryEntryCoreAsync(CleanerCleanupEntry entry)
        {
            entry.ErrorMessage = string.Empty;
            entry.FailureReason = CleanerFailureReason.None;
            entry.LockedByProcesses.Clear();
            entry.Status = "Pending";
            entry.BackupPath = string.Empty;

            try
            {
                CleanerBoundaryValidationResult boundary = _boundaryGuard.Validate(
                    new CleanerScanItem
                    {
                        Path = entry.OriginalPath,
                        RequiresElevation = entry.RequiresElevation,
                        BoundaryRoots = entry.BoundaryRoots,
                        ExecutionMode = entry.ExecutionMode
                    },
                    entry.OriginalPath,
                    _privilegeService.IsElevated);

                if (!boundary.IsAllowed)
                {
                    entry.Status = "Failed";
                    entry.FailureReason = boundary.FailureReason;
                    entry.ErrorMessage = boundary.Message;
                    return;
                }

                if (!Exists(entry.OriginalPath))
                {
                    entry.Status = "Failed";
                    entry.FailureReason = CleanerFailureReason.NotFound;
                    entry.ErrorMessage = "原始目标当前不存在，无法再次执行清理。";
                    return;
                }

                switch (entry.ExecutionMode)
                {
                    case CleanerExecutionMode.Recycle:
                        entry.Status = await _nativeFileService.MoveToRecycleBinAsync(entry.OriginalPath) ? "Completed" : "Failed";
                        if (entry.Status != "Completed")
                        {
                            entry.FailureReason = CleanerFailureReason.Unknown;
                            entry.ErrorMessage = "移动到回收站失败。";
                        }

                        break;
                    case CleanerExecutionMode.Quarantine:
                        string retryBatchFolder = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
                        string destination = BuildQuarantinePath(retryBatchFolder, entry.OriginalPath);
                        await MovePathAsync(entry.OriginalPath, destination);
                        entry.BackupPath = destination;
                        entry.Status = "Completed";
                        break;
                    case CleanerExecutionMode.Permanent:
                        DeletePath(entry.OriginalPath);
                        entry.Status = "Completed";
                        entry.CanRestore = false;
                        break;
                    default:
                        entry.Status = "Skipped";
                        break;
                }
            }
            catch (Exception ex)
            {
                entry.Status = "Failed";
                entry.FailureReason = ClassifyFailure(ex);
                entry.ErrorMessage = ex.Message;
                if (entry.FailureReason == CleanerFailureReason.InUse)
                {
                    entry.LockedByProcesses = ResolveLockingProcesses(entry.OriginalPath).ToList();
                }
            }
        }

        private static async Task RestoreEntryCoreAsync(CleanerCleanupEntry entry, CleanerRestoreSummary summary)
        {
            try
            {
                if (!Exists(entry.BackupPath))
                {
                    entry.Status = "RestoreMissing";
                    entry.ErrorMessage = "隔离区原文件已不存在。";
                    summary.FailedCount++;
                    return;
                }

                string targetPath = ResolveRestorePath(entry.OriginalPath);
                await MovePathAsync(entry.BackupPath, targetPath);

                entry.Restored = true;
                entry.RestoredAt = DateTimeOffset.Now;
                entry.RestoredPath = targetPath;
                entry.Status = "Restored";
                summary.RestoredCount++;
                summary.RestoredBytes += entry.SizeBytes;
            }
            catch (Exception ex)
            {
                entry.ErrorMessage = ex.Message;
                entry.Status = "RestoreFailed";
                summary.FailedCount++;
            }
        }

        private IReadOnlyList<string> ResolveTargets(CleanerScanItem item)
        {
            if (item.ExecutionMode == CleanerExecutionMode.None || item.ViewOnly)
            {
                return Array.Empty<string>();
            }

            if (item.TargetPaths.Count > 0)
            {
                return item.TargetPaths
                    .Where(Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            try
            {
                return item.ScanKind switch
                {
                    CleanerScanKind.DirectoryContents => Directory.Exists(item.Path)
                        ? Directory.EnumerateFileSystemEntries(item.Path).ToList()
                        : Array.Empty<string>(),
                    CleanerScanKind.FilesByPattern => ResolvePatternTargets(item),
                    CleanerScanKind.Directory => Directory.Exists(item.Path)
                        ? new[] { item.Path }
                        : Array.Empty<string>(),
                    CleanerScanKind.File => File.Exists(item.Path)
                        ? new[] { item.Path }
                        : Array.Empty<string>(),
                    _ => Array.Empty<string>()
                };
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IReadOnlyList<string> ResolvePatternTargets(CleanerScanItem item)
        {
            if (!Directory.Exists(item.Path))
            {
                return Array.Empty<string>();
            }

            return CleanerPathSafety.EnumerateFilesSafely(
                    item.Path,
                    item.IncludePatterns,
                    item.IncludeSubdirectories,
                    Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string BuildQuarantinePath(string batchId, string targetPath)
        {
            string batchFolder = Path.Combine(_stateStore.QuarantineRootPath, batchId);
            Directory.CreateDirectory(batchFolder);

            string name = Path.GetFileName(targetPath);
            string safeName = string.IsNullOrWhiteSpace(name) ? "entry" : name;
            string destination = Path.Combine(batchFolder, safeName);
            if (!Exists(destination))
            {
                return destination;
            }

            string stem = Path.GetFileNameWithoutExtension(safeName);
            string extension = Path.GetExtension(safeName);
            int counter = 1;
            while (Exists(destination))
            {
                destination = Path.Combine(batchFolder, $"{stem}_{counter:D2}{extension}");
                counter++;
            }

            return destination;
        }

        private static async Task MovePathAsync(string sourcePath, string destinationPath)
        {
            string? parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (File.Exists(sourcePath))
            {
                try
                {
                    File.Move(sourcePath, destinationPath);
                }
                catch (IOException)
                {
                    File.Copy(sourcePath, destinationPath, true);
                    File.Delete(sourcePath);
                }

                return;
            }

            if (Directory.Exists(sourcePath))
            {
                if (CleanerPathSafety.IsReparsePoint(sourcePath))
                {
                    throw new IOException("检测到符号链接或 Junction，已终止该目录移动。");
                }

                try
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                catch (IOException ex) when (!CleanerPathSafety.IsLockConflict(ex))
                {
                    CopyDirectorySafely(sourcePath, destinationPath);
                    DeleteDirectorySafely(sourcePath);
                }

                await Task.CompletedTask;
            }
        }

        private static void CopyDirectorySafely(string sourcePath, string destinationPath)
        {
            if (CleanerPathSafety.IsReparsePoint(sourcePath))
            {
                throw new IOException("检测到符号链接或 Junction，已终止该目录复制。");
            }

            Directory.CreateDirectory(destinationPath);

            foreach (string file in CleanerPathSafety.SafeEnumerateFiles(sourcePath))
            {
                string destinationFile = Path.Combine(destinationPath, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
            }

            foreach (string directory in CleanerPathSafety.SafeEnumerateDirectories(sourcePath))
            {
                if (CleanerPathSafety.IsReparsePoint(directory))
                {
                    continue;
                }

                string destinationDirectory = Path.Combine(destinationPath, Path.GetFileName(directory));
                CopyDirectorySafely(directory, destinationDirectory);
            }
        }

        private static void DeletePath(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return;
            }

            if (Directory.Exists(path))
            {
                DeleteDirectorySafely(path);
            }
        }

        private static void DeleteDirectorySafely(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            if (CleanerPathSafety.IsReparsePoint(path))
            {
                Directory.Delete(path, false);
                return;
            }

            foreach (string file in CleanerPathSafety.SafeEnumerateFiles(path))
            {
                File.Delete(file);
            }

            foreach (string directory in CleanerPathSafety.SafeEnumerateDirectories(path))
            {
                if (CleanerPathSafety.IsReparsePoint(directory))
                {
                    Directory.Delete(directory, false);
                    continue;
                }

                DeleteDirectorySafely(directory);
            }

            Directory.Delete(path, false);
        }

        private IReadOnlyList<string> ResolveLockingProcesses(string targetPath)
        {
            if (File.Exists(targetPath))
            {
                return _lockService.GetLockingProcesses(new[] { targetPath });
            }

            if (!Directory.Exists(targetPath))
            {
                return Array.Empty<string>();
            }

            List<string> probeFiles = new();
            try
            {
                probeFiles.AddRange(CleanerPathSafety.EnumerateFilesSafely(targetPath, ["*"], recursive: true, Array.Empty<string>()).Take(8));
            }
            catch
            {
            }

            return _lockService.GetLockingProcesses(probeFiles);
        }

        private static CleanerFailureReason ClassifyFailure(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => CleanerFailureReason.AccessDenied,
                FileNotFoundException => CleanerFailureReason.NotFound,
                DirectoryNotFoundException => CleanerFailureReason.NotFound,
                IOException ioEx when CleanerPathSafety.IsLockConflict(ioEx) => CleanerFailureReason.InUse,
                _ => CleanerFailureReason.Unknown
            };
        }

        private static string ResolveRestorePath(string originalPath)
        {
            if (!Exists(originalPath))
            {
                return originalPath;
            }

            string directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            string candidate = Path.Combine(directory, $"{fileName} (restored){extension}");
            int counter = 1;

            while (Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{fileName} (restored {counter}){extension}");
                counter++;
            }

            return candidate;
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static long CalculateSize(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return new FileInfo(path).Length;
                }

                if (!Directory.Exists(path))
                {
                    return 0;
                }

                long total = 0;
                foreach (string file in CleanerPathSafety.EnumerateFilesSafely(path, ["*"], recursive: true, Array.Empty<string>()))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                    }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }
    }
}
