using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BlueSapphire.Helpers
{
    // 简化的结果类
    public class MediaAnalysisResult
    {
        public ulong? VisualHash { get; set; }
        public bool IsSuccess { get; set; }
    }

    public static class FileHelper
    {
        // 1. 计算 MD5 (用于精确去重)
        public static async Task<string> ComputeMD5Async(StorageFile file)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using var stream = await file.OpenStreamForReadAsync();
                    using var md5 = MD5.Create();
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        // 2. 计算视觉哈希 (内置 dHash 算法，无需外部库)
        public static async Task<ulong?> ComputeVisualHashAsync(StorageFile file)
        {
            // 【关键修复】显式指定 Task.Run 的泛型类型为 <ulong?>，避免推断错误
            return await Task.Run<ulong?>(async () =>
            {
                try
                {
                    // 1. 获取缩略图流 (WinUI 原生支持视频/图片)
                    // 使用 32px 尺寸，足够计算 9x8 的 dHash
                    using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32);

                    if (thumbnail == null) return null;

                    using var stream = thumbnail.AsStreamForRead();

                    // 2. 加载图片 (L8 = 8位灰度图，省去转灰度步骤)
                    using var image = await Image.LoadAsync<L8>(stream);

                    // 3. 预处理：强制缩放到 9x8 像素
                    image.Mutate(x => x.Resize(9, 8));

                    // 4. dHash 核心算法：比较相邻像素亮度
                    ulong hash = 0;
                    int bitIndex = 0;

                    // 遍历每一行
                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 8; x++)
                        {
                            var leftPixel = image[x, y];
                            var rightPixel = image[x + 1, y];

                            // 如果左边比右边亮，该位设为 1
                            if (leftPixel.PackedValue > rightPixel.PackedValue)
                            {
                                hash |= (1UL << bitIndex);
                            }
                            bitIndex++;
                        }
                    }

                    // 【关键修复】明确转换为 nullable ulong
                    return (ulong?)hash;
                }
                catch
                {
                    return null;
                }
            });
        }

        // 3. 计算汉明距离 (比较两个哈希有多少位不同)
        public static int CalculateHammingDistance(ulong hash1, ulong hash2)
        {
            ulong xor = hash1 ^ hash2;
            int distance = 0;
            while (xor > 0)
            {
                distance += (int)(xor & 1);
                xor >>= 1;
            }
            return distance;
        }
    }
}