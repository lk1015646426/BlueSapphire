using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            // 2. 第一级：按大小分组 (Tier 1)
            progress?.Report((0, "阶段 1/3: 按大小分组...", ""));
            var sizeGroups = await Task.Run(() =>
            {
                var dict = new Dictionary<ulong, List<StorageFile>>();
                int processed = 0;
                foreach (var file in allFiles)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        // 使用我们第一阶段优化过的极速 FileInfo
                        ulong size = (ulong)new System.IO.FileInfo(file.Path).Length;
                        if (size > 0)
                        {
                            if (!dict.ContainsKey(size)) dict[size] = new List<StorageFile>();
                            dict[size].Add(file);
                        }
                    }
                    catch { }

                    if (++processed % 200 == 0)
                        progress?.Report((processed, "阶段 1/3: 按大小分组...", ""));
                }
                return dict.Values.Where(g => g.Count > 1).ToList();
            });

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