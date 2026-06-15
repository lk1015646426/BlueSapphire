using BlueSapphire.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public class MediaDeduplicationService
    {
        // ========================= Exact Duplicate Detection (unchanged) =========================

        public async Task<List<List<StorageFile>>> FindDuplicatesAsync(
            StorageFolder folder,
            IProgress<(double Value, string Message, string Detail)> progress,
            CancellationToken token)
        {
            progress?.Report((0, "正在初始化扫描...", string.Empty));

            var allFiles = await folder.CreateFileQueryWithOptions(MediaFileCatalog.CreateAllMediaQueryOptions()).GetFilesAsync();
            if (allFiles.Count < 2)
            {
                return new List<List<StorageFile>>();
            }

            progress?.Report((0, "阶段 1/3: 按大小分组...", string.Empty));

            var groupedBySize = new ConcurrentDictionary<ulong, ConcurrentBag<StorageFile>>();
            int processed = 0;
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            var sizeTasks = allFiles.Select(async file =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await semaphore.WaitAsync(token);
                try
                {
                    var props = await file.GetBasicPropertiesAsync();
                    if (props.Size > 0)
                    {
                        groupedBySize.GetOrAdd(props.Size, _ => new ConcurrentBag<StorageFile>()).Add(file);
                    }
                }
                catch (Exception ex)
                {
                    MatrixLogService.LogError($"Dedupe_GetSize ({file.Name})", ex);
                }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref processed);
                    if (current % 200 == 0 || current == allFiles.Count)
                    {
                        double percent = allFiles.Count > 0 ? (double)current / allFiles.Count * 100 : 0;
                        progress?.Report((percent, "阶段 1/3: 按大小分组...", $"{current} / {allFiles.Count}"));
                    }
                }
            });

            await Task.WhenAll(sizeTasks);
            if (token.IsCancellationRequested)
            {
                return new List<List<StorageFile>>();
            }

            var sizeGroups = groupedBySize.Values
                .Where(group => group.Count > 1)
                .Select(group => group.ToList())
                .ToList();

            int totalSuspects = sizeGroups.Sum(group => group.Count);
            if (totalSuspects == 0)
            {
                return new List<List<StorageFile>>();
            }

            progress?.Report((0, "正在深度校验 (Tier 2/3)...", $"0 / {totalSuspects}"));

            var result = new List<List<StorageFile>>();
            int scanned = 0;

            foreach (var sizeGroup in sizeGroups)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var groupedByQuickHash = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in sizeGroup)
                {
                    string quickHash = await MediaScanService.ComputeQuickHeaderFooterHashAsync(file);
                    if (!string.IsNullOrWhiteSpace(quickHash))
                    {
                        if (!groupedByQuickHash.TryGetValue(quickHash, out var quickGroup))
                        {
                            quickGroup = new List<StorageFile>();
                            groupedByQuickHash[quickHash] = quickGroup;
                        }

                        quickGroup.Add(file);
                    }

                    scanned++;
                    if (scanned % 10 == 0 || scanned == totalSuspects)
                    {
                        double percent = totalSuspects > 0 ? (double)scanned / totalSuspects * 100 : 0;
                        progress?.Report((percent, "正在深度校验 (Tier 2/3)...", $"{scanned} / {totalSuspects}"));
                    }
                }

                foreach (var quickGroup in groupedByQuickHash.Values.Where(group => group.Count > 1))
                {
                    var groupedByMd5 = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in quickGroup)
                    {
                        string fullHash = await MediaScanService.ComputeMD5Async(file);
                        if (!string.IsNullOrWhiteSpace(fullHash))
                        {
                            if (!groupedByMd5.TryGetValue(fullHash, out var md5Group))
                            {
                                md5Group = new List<StorageFile>();
                                groupedByMd5[fullHash] = md5Group;
                            }

                            md5Group.Add(file);
                        }
                    }

                    result.AddRange(groupedByMd5.Values.Where(group => group.Count > 1));
                }
            }

            return result;
        }

        // ========================= Similar Image Detection (full rewrite) =========================

        /// <summary>
        /// Detects visually similar images using a two-phase approach:
        /// <para>
        /// Phase 1: Extract 64-bit dHash fingerprints for all images in parallel.
        /// Uses EXIF thumbnail fast path (~1-5ms/file for JPEG) with full-decode fallback.
        /// </para>
        /// <para>
        /// Phase 2: Cluster images by Hamming distance (≤5 bits = similar).
        /// Groups are sorted by file size descending (largest/highest-quality first).
        /// </para>
        /// </summary>
        public async Task<List<List<StorageFile>>> FindSimilarImagesAsync(
            StorageFolder folder,
            IProgress<(double Value, string Message, string Detail)> progress,
            CancellationToken token)
        {
            progress?.Report((0, "正在枚举图片文件...", string.Empty));

            var allFiles = await folder.CreateFileQueryWithOptions(
                MediaFileCatalog.CreateImageQueryOptions()).GetFilesAsync();

            if (allFiles.Count < 2)
            {
                return new List<List<StorageFile>>();
            }

            // ========== Phase 1: Parallel dHash extraction ==========
            progress?.Report((0, "阶段 1/2: 极速提取视觉指纹...", $"0 / {allFiles.Count}"));

            var hashResults = new ConcurrentBag<(StorageFile File, ulong Hash, ulong Size)>();
            int processed = 0;
            int total = allFiles.Count;

            await Parallel.ForEachAsync(
                allFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount),
                    CancellationToken = token
                },
                async (file, ct) =>
                {
                    try
                    {
                        var hash = await MediaScanService.ComputeDHashAsync(file.Path);
                        if (hash.HasValue)
                        {
                            // Use pure .NET FileInfo instead of WinRT GetBasicPropertiesAsync
                            ulong fileSize = 0;
                            try { fileSize = (ulong)new FileInfo(file.Path).Length; } catch { }
                            hashResults.Add((file, hash.Value, fileSize));
                        }
                    }
                    catch (Exception ex)
                    {
                        MatrixLogService.LogError($"dHash ({file.Name})", ex);
                    }
                    finally
                    {
                        int cur = Interlocked.Increment(ref processed);
                        if (cur % 50 == 0 || cur == total)
                        {
                            progress?.Report(((double)cur / total * 100,
                                "阶段 1/2: 极速提取视觉指纹...",
                                $"{cur} / {total}"));
                        }
                    }
                });

            if (token.IsCancellationRequested)
            {
                return new List<List<StorageFile>>();
            }

            // ========== Phase 2: Hamming distance clustering ==========
            progress?.Report((95, "阶段 2/2: 聚类分析...", "正在匹配相似照片"));

            var items = hashResults.ToList();
            var visited = new bool[items.Count];
            var result = new List<List<StorageFile>>();

            for (int i = 0; i < items.Count; i++)
            {
                if (visited[i]) continue;
                visited[i] = true;

                var group = new List<(StorageFile File, ulong Size)>
                {
                    (items[i].File, items[i].Size)
                };

                for (int j = i + 1; j < items.Count; j++)
                {
                    if (visited[j]) continue;

                    if (MediaScanService.HammingDistance(items[i].Hash, items[j].Hash) <= 5)
                    {
                        group.Add((items[j].File, items[j].Size));
                        visited[j] = true;
                    }
                }

                if (group.Count > 1)
                {
                    // Largest file first (highest quality / resolution)
                    result.Add(group
                        .OrderByDescending(g => g.Size)
                        .Select(g => g.File)
                        .ToList());
                }
            }

            return result;
        }
    }
}
