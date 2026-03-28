using BlueSapphire.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public class MediaDeduplicationService
    {
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
                        progress?.Report((current, "阶段 1/3: 按大小分组...", $"{current} / {allFiles.Count}"));
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
                        progress?.Report((scanned, "正在深度校验 (Tier 2/3)...", $"{scanned} / {totalSuspects}"));
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

        public async Task<List<List<StorageFile>>> FindSimilarImagesAsync(
            StorageFolder folder,
            IProgress<(double Value, string Message, string Detail)> progress,
            CancellationToken token)
        {
            progress?.Report((0, "正在初始化智能扫描...", "仅分析图片文件"));

            var allFiles = await folder.CreateFileQueryWithOptions(MediaFileCatalog.CreateImageQueryOptions()).GetFilesAsync();
            if (allFiles.Count < 2)
            {
                return new List<List<StorageFile>>();
            }

            progress?.Report((0, "阶段 1/2: 提取视觉指纹 (pHash)...", "计算量较大，请稍候"));

            var hashList = new ConcurrentBag<(StorageFile File, ulong Hash, ulong Size)>();
            int processed = 0;
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

            var hashTasks = allFiles.Select(async file =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await semaphore.WaitAsync(token);
                try
                {
                    var hash = await MediaScanService.ComputePHashAsync(file);
                    if (hash.HasValue)
                    {
                        var props = await file.GetBasicPropertiesAsync();
                        hashList.Add((file, hash.Value, props.Size));
                    }
                }
                catch (Exception ex)
                {
                    MatrixLogService.LogError($"pHash_Compute ({file.Name})", ex);
                }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref processed);
                    if (current % 10 == 0 || current == allFiles.Count)
                    {
                        progress?.Report((current, "阶段 1/2: 提取视觉指纹...", $"{current} / {allFiles.Count}"));
                    }
                }
            });

            await Task.WhenAll(hashTasks);
            if (token.IsCancellationRequested)
            {
                return new List<List<StorageFile>>();
            }

            progress?.Report((0, "阶段 2/2: 智能聚类与画质分析...", "正在寻找相似照片"));

            var result = new List<List<StorageFile>>();
            var items = hashList.ToList();
            var visited = new bool[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                var currentGroup = new List<(StorageFile File, ulong Size)> { (items[i].File, items[i].Size) };
                visited[i] = true;

                for (int j = i + 1; j < items.Count; j++)
                {
                    if (visited[j])
                    {
                        continue;
                    }

                    if (MediaScanService.HammingDistance(items[i].Hash, items[j].Hash) <= 5)
                    {
                        currentGroup.Add((items[j].File, items[j].Size));
                        visited[j] = true;
                    }
                }

                if (currentGroup.Count > 1)
                {
                    result.Add(currentGroup
                        .OrderByDescending(item => item.Size)
                        .Select(item => item.File)
                        .ToList());
                }
            }

            return result;
        }
    }
}
