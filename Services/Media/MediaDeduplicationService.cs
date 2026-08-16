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

namespace BlueSapphire.Services.Media
{
    public class MediaDeduplicationService
    {
        /// <summary>
        /// 2145 位指纹汉明距离判定阈值。距离 ≤ 该值视为"疑似相似"（仍需人工确认）。
        /// 由 229 张真实截图人工标定：真重复（同屏重截，含动画/小幅内容变化）d=26~31，
        /// 不同页面误报最低 d=39。取 35：真重复侧留 4 位余量，距最近误报留 4 位边距，
        /// 宁可漏报不可误报。
        /// </summary>
        internal const int MaxHammingDistanceForSimilar = 35;

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
            await Parallel.ForEachAsync(
                allFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount * 2),
                    CancellationToken = token
                },
                async (file, ct) =>
                {
                try
                {
                    ct.ThrowIfCancellationRequested();
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
                    int current = Interlocked.Increment(ref processed);
                    long now = Environment.TickCount64;
                    if (current == allFiles.Count || now - Interlocked.Read(ref lastReportTicks1) >= 100)
                    {
                        Interlocked.Exchange(ref lastReportTicks1, now);
                        double percent = allFiles.Count > 0 ? (double)current / allFiles.Count * 25 : 0;
                        progress?.Report((percent, "阶段 1/3: 按大小分组...", $"{current} / {allFiles.Count}"));
                    }
                }
            });
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

            var quickCandidateGroups = new List<List<StorageFile>>();
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
                    token.ThrowIfCancellationRequested();
                    string quickHash = await MediaScanService.ComputeQuickHeaderFooterHashAsync(file, token);
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
                        double percent = totalSuspects > 0
                            ? 25 + (double)scanned / totalSuspects * 35
                            : 25;
                        progress?.Report((percent, "正在深度校验 (Tier 2/3)...", $"{scanned} / {totalSuspects}"));
                    }
                }

                quickCandidateGroups.AddRange(
                    groupedByQuickHash.Values.Where(group => group.Count > 1));
            }

            int totalFullHashes = quickCandidateGroups.Sum(group => group.Count);
            if (totalFullHashes == 0)
            {
                progress?.Report((100, "扫描完成", "未发现内容重复的文件。"));
                return new List<List<StorageFile>>();
            }

            var result = new List<List<StorageFile>>();
            int fullScanned = 0;
            long lastReportTicks3 = 0;
            foreach (var quickGroup in quickCandidateGroups)
            {
                token.ThrowIfCancellationRequested();
                var groupedBySha256 = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in quickGroup)
                {
                    token.ThrowIfCancellationRequested();
                    string fullHash = await MediaScanService.ComputeSHA256Async(file, token);
                    if (!string.IsNullOrWhiteSpace(fullHash))
                    {
                        if (!groupedBySha256.TryGetValue(fullHash, out var sha256Group))
                        {
                            sha256Group = new List<StorageFile>();
                            groupedBySha256[fullHash] = sha256Group;
                        }

                        sha256Group.Add(file);
                    }

                    fullScanned++;
                    long now = Environment.TickCount64;
                    if (fullScanned == totalFullHashes || now - lastReportTicks3 >= 100)
                    {
                        lastReportTicks3 = now;
                        double percent = 60 + (double)fullScanned / totalFullHashes * 40;
                        progress?.Report((
                            percent,
                            "阶段 3/3: 计算 SHA-256 完整内容指纹...",
                            $"{fullScanned} / {totalFullHashes}"));
                    }
                }

                result.AddRange(groupedBySha256.Values.Where(group => group.Count > 1));
            }

            progress?.Report((100, "扫描完成", $"发现 {result.Count} 组完全重复文件。"));
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
        /// Phase 2: Group images whose fingerprints are directly within the Hamming threshold.
        /// Groups are sorted by file size descending as a non-authoritative keep suggestion.
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

            var hashResults = new ConcurrentBag<(StorageFile File, PerceptualHash Hash, ulong Size)>();
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
                            // 大小读取失败按 0 处理，不阻断指纹聚类。
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
                            progress?.Report(((double)cur / total * 90,
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
                var directMatches = new List<BKTreeNode>();
                root?.Search(node.Hash, MaxHammingDistanceForSimilar, directMatches);
                foreach (var match in directMatches)
                {
                    if (!match.Visited)
                    {
                        match.Visited = true;
                        cluster.AddRange(match.Items);
                    }
                }

                if (cluster.Count > 1)
                {
                    // 文件较大只是保留建议的一项弱信号，界面会明确要求人工确认。
                    result.Add(cluster
                        .OrderByDescending(g => g.Size)
                        .Select(g => g.File)
                        .ToList());
                }
            }

            progress?.Report((100, "相似图片扫描完成", $"发现 {result.Count} 组候选，请人工确认。"));
            return result;
        }

        private sealed class BKTreeNode
        {
            public PerceptualHash Hash;
            public bool Visited;
            public List<(StorageFile File, ulong Size)> Items = new();
            public Dictionary<int, BKTreeNode>? Children;

            public BKTreeNode(StorageFile file, PerceptualHash hash, ulong size)
            {
                Hash = hash;
                Items.Add((file, size));
            }

            public bool AddWithNodeReturn(StorageFile file, PerceptualHash hash, ulong size, out BKTreeNode? newNode)
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

            public void Search(PerceptualHash queryHash, int maxDistance, List<BKTreeNode> results)
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
