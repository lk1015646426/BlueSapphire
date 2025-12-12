using ImageMagick; // 必须安装 NuGet 包: Magick.NET-Q8-AnyCPU
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Helpers
{
    // 定义结果类
    public class ImageAnalysisResult
    {
        public ulong Hash { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsSuccess { get; set; }
    }

    public static class FileHelper
    {
        // 1. 计算 MD5 (用于视频/非图片)
        public static async Task<string> ComputeMD5Async(StorageFile file)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using (var stream = await file.OpenStreamForReadAsync())
                    {
                        using (var md5 = MD5.Create())
                        {
                            var hash = md5.ComputeHash(stream);
                            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        }
                    }
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        // 2. 单独计算 dHash (视觉指纹)
        public static async Task<ulong?> ComputeDHashAsync(StorageFile file)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using (var stream = await file.OpenStreamForReadAsync())
                    using (var image = new MagickImage(stream))
                    {
                        var size = new MagickGeometry(9, 8) { IgnoreAspectRatio = true };
                        image.Resize(size);
                        byte[] pixels = image.ToByteArray(MagickFormat.Gray);

                        ulong hash = 0;
                        int bitIndex = 0;

                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                var left = pixels[y * 9 + x];
                                var right = pixels[y * 9 + x + 1];
                                if (left > right) hash |= (1UL << bitIndex);
                                bitIndex++;
                            }
                        }
                        return (ulong?)hash;
                    }
                }
                catch
                {
                    return null;
                }
            });
        }

        // 3. 综合分析 (同时获取分辨率和哈希)
        public static async Task<ImageAnalysisResult> AnalyzeImageAsync(StorageFile file)
        {
            return await Task.Run(async () =>
            {
                var result = new ImageAnalysisResult { IsSuccess = false };
                try
                {
                    using (var stream = await file.OpenStreamForReadAsync())
                    using (var image = new MagickImage(stream))
                    {
                        // 【修复】强制转换为 int
                        result.Width = (int)image.Width;
                        result.Height = (int)image.Height;

                        var size = new MagickGeometry(9, 8) { IgnoreAspectRatio = true };
                        image.Resize(size);

                        byte[] pixels = image.ToByteArray(MagickFormat.Gray);
                        ulong hash = 0;
                        int bitIndex = 0;

                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                if (pixels[y * 9 + x] > pixels[y * 9 + x + 1])
                                    hash |= (1UL << bitIndex);
                                bitIndex++;
                            }
                        }
                        result.Hash = hash;
                        result.IsSuccess = true;
                    }
                }
                catch
                {
                    result.IsSuccess = false;
                }
                return result;
            });
        }

        // 4. 计算汉明距离
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