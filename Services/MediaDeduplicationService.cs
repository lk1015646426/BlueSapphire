using BlueSapphire.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services
{
    public class MediaDeduplicationService
    {
        private readonly ILogger<MediaDeduplicationService> _logger;

        public MediaDeduplicationService(ILogger<MediaDeduplicationService> logger)
        {
            _logger = logger;
        }
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
            long lastReportTicks1 = 0;
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
                    _logger.LogError(ex, "Dedupe_GetSize ({FileName})", file.Name);
                }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref processed);
                    long now = Environment.TickCount64;
                    if (current == allFiles.Count || now - Interlocked.Read(ref lastReportTicks1) >= 100)
                    {
                        Interlocked.Exchange(ref lastReportTicks1, now);
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
            long lastReportTicks2 = 0;

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
                    long now = Environment.TickCount64;
                    if (scanned == totalSuspects || now - lastReportTicks2 >= 100)
                    {
                        lastReportTicks2 = now;
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
            long lastReportTicks3 = 0;

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
                        _logger.LogError(ex, "dHash ({FileName})", file.Name);
                    }
                    finally
                    {
                        int cur = Interlocked.Increment(ref processed);
                        long now = Environment.TickCount64;
                        if (cur == total || now - Interlocked.Read(ref lastReportTicks3) >= 100)
                        {
                            Interlocked.Exchange(ref lastReportTicks3, now);
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

            // ========== Phase 2: BK-Tree Spatial Index Clustering (O(N log N)) ==========
            progress?.Report((95, "阶段 2/2: 算法空间索引聚类 (BK-Tree)...", "正在构建空间索引与匹配相似照片"));

            if (hashResults.IsEmpty)
            {
                return new List<List<StorageFile>>();
            }

            BKTreeNode? root = null;
            var allNodes = new List<BKTreeNode>();

            foreach (var item in hashResults)
            {
                if (root == null)
                {
                    root = new BKTreeNode(item.File, item.Hash, item.Size);
                    allNodes.Add(root);
                }
                else
                {
                    if (root.AddWithNodeReturn(item.File, item.Hash, item.Size, out var newNode))
                    {
                        if (newNode != null)
                        {
                            allNodes.Add(newNode);
                        }
                    }
                }
            }

            var result = new List<List<StorageFile>>();

            foreach (var node in allNodes)
            {
                if (node.Visited) continue;

                var cluster = new List<(StorageFile File, ulong Size)>();
                var queue = new Queue<BKTreeNode>();
                queue.Enqueue(node);
                node.Visited = true;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cluster.AddRange(current.Items);

                    var neighbors = new List<BKTreeNode>();
                    root?.Search(current.Hash, 5, neighbors);
                    foreach (var neighbor in neighbors)
                    {
                        if (!neighbor.Visited)
                        {
                            neighbor.Visited = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (cluster.Count > 1)
                {
                    // Largest file first (highest quality / resolution)
                    result.Add(cluster
                        .OrderByDescending(g => g.Size)
                        .Select(g => g.File)
                        .ToList());
                }
            }

            return result;
        }

        private sealed class BKTreeNode
        {
            public ulong Hash;
            public bool Visited;
            public List<(StorageFile File, ulong Size)> Items = new();
            public Dictionary<int, BKTreeNode>? Children;

            public BKTreeNode(StorageFile file, ulong hash, ulong size)
            {
                Hash = hash;
                Items.Add((file, size));
            }

            public bool AddWithNodeReturn(StorageFile file, ulong hash, ulong size, out BKTreeNode? newNode)
            {
                int dist = MediaScanService.HammingDistance(Hash, hash);
                if (dist == 0)
                {
                    Items.Add((file, size));
                    newNode = null;
                    return false;
                }

                Children ??= new Dictionary<int, BKTreeNode>();
                if (Children.TryGetValue(dist, out var child))
                {
                    return child.AddWithNodeReturn(file, hash, size, out newNode);
                }
                else
                {
                    newNode = new BKTreeNode(file, hash, size);
                    Children[dist] = newNode;
                    return true;
                }
            }

            public void Search(ulong queryHash, int maxDistance, List<BKTreeNode> results)
            {
                int dist = MediaScanService.HammingDistance(Hash, queryHash);
                if (dist <= maxDistance)
                {
                    results.Add(this);
                }

                if (Children == null) return;

                int minDist = dist - maxDistance;
                int maxDist = dist + maxDistance;

                foreach (var kvp in Children)
                {
                    if (kvp.Key >= minDist && kvp.Key <= maxDist)
                    {
                        kvp.Value.Search(queryHash, maxDistance, results);
                    }
                }
            }
        }
    }
}
