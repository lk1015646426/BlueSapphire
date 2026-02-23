using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace BlueSapphire.Services
{
    public class MediaDeduplicationService
    {
        /// <summary>
        /// 执行三级去重扫描算法，并通过 IProgress 汇报进度
        /// </summary>
        public async Task<List<List<StorageFile>>> FindDuplicatesAsync(
            StorageFolder folder,
            IProgress<(double Value, string Message, string Detail)> progress,
            CancellationToken token)
        {
            progress?.Report((0, "正在初始化扫描...", ""));

            // 1. 获取支持的文件
            var fileExtensions = new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp", ".heic", ".mp4", ".mov", ".avi", ".wmv", ".mkv" };
            var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, fileExtensions);
            var allFiles = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

            if (allFiles.Count < 2) return new List<List<StorageFile>>();

            // 2. 第一级：按大小分组 (Tier 1) - ✅ 彻底拥抱 WinRT 异步并发标准
            progress?.Report((0, "阶段 1/3: 按大小分组...", ""));

            var concurrentDict = new ConcurrentDictionary<ulong, ConcurrentBag<StorageFile>>();
            int processed = 0;
            int totalFiles = allFiles.Count;

            // 使用 CPU 逻辑核心数 x 2 作为最优并发量
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            var sizeTasks = allFiles.Select(async file =>
            {
                if (token.IsCancellationRequested) return;

                await semaphore.WaitAsync(token);
                try
                {
                    // ✅ 核心修复：完全摒弃 System.IO.FileInfo，使用现代标准异步获取属性
                    // 完美支持 MTP 手机设备、云盘文件和局域网共享文件夹
                    var props = await file.GetBasicPropertiesAsync();
                    ulong size = props.Size;

                    if (size > 0)
                    {
                        var bag = concurrentDict.GetOrAdd(size, _ => new ConcurrentBag<StorageFile>());
                        bag.Add(file);
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
                    if (current % 200 == 0)
                    {
                        progress?.Report((current, "阶段 1/3: 按大小分组...", $"{current} / {totalFiles}"));
                    }
                }
            });

            await Task.WhenAll(sizeTasks);

            if (token.IsCancellationRequested) return new List<List<StorageFile>>();

            // 将并发字典中数量大于 1 的分组提取出来
            var sizeGroups = concurrentDict.Values
                .Where(b => b.Count > 1)
                .Select(b => b.ToList())
                .ToList();

            int totalSuspects = sizeGroups.Sum(g => g.Count);
            if (totalSuspects == 0) return new List<List<StorageFile>>();

            // 3. 第二级与第三级：哈希深度校验 (Tier 2 & 3)
            progress?.Report((0, "正在深度校验 (Tier 2/3)...", $"0 / {totalSuspects}"));
            var finalDuplicates = await Task.Run(async () =>
            {
                var result = new List<List<StorageFile>>();
                int processedCount = 0;

                foreach (var group in sizeGroups)
                {
                    if (token.IsCancellationRequested) break;

                    // Tier 2: 快速头尾哈希
                    var quickHashGroups = new Dictionary<string, List<StorageFile>>();
                    foreach (var file in group)
                    {
                        string qHash = await MediaScanService.ComputeQuickHeaderFooterHashAsync(file);
                        if (!string.IsNullOrEmpty(qHash))
                        {
                            if (!quickHashGroups.ContainsKey(qHash)) quickHashGroups[qHash] = new List<StorageFile>();
                            quickHashGroups[qHash].Add(file);
                        }

                        processedCount++;
                        if (processedCount % 10 == 0)
                            progress?.Report((processedCount, "正在深度校验 (Tier 2/3)...", $"{processedCount} / {totalSuspects}"));
                    }

                    // Tier 3: 全量 MD5 校验
                    foreach (var quickGroup in quickHashGroups.Values.Where(g => g.Count > 1))
                    {
                        var md5Groups = new Dictionary<string, List<StorageFile>>();
                        foreach (var file in quickGroup)
                        {
                            string fullHash = await MediaScanService.ComputeMD5Async(file);
                            if (!string.IsNullOrEmpty(fullHash))
                            {
                                if (!md5Groups.ContainsKey(fullHash)) md5Groups[fullHash] = new List<StorageFile>();
                                md5Groups[fullHash].Add(file);
                            }
                        }

                        foreach (var finalGroup in md5Groups.Values.Where(g => g.Count > 1))
                        {
                            result.Add(finalGroup);
                        }
                    }
                }
                return result;
            });

            return finalDuplicates;
        }
    }
}