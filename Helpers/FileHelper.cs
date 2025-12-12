using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Graphics.Imaging; // 必须引用：用于图像处理
using Windows.Storage;
using Windows.Storage.Streams;

namespace BlueSapphire.Helpers
{
    public static class FileHelper
    {
        // 1. 传统 MD5 计算 (用于视频或非图片文件)
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

        // 2. 新增：计算图片感知哈希 (dHash - 差异哈希算法)
        // 返回一个 ulong (64位整数) 作为图片的"视觉指纹"
        public static async Task<ulong?> ComputeDHashAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenReadAsync();

                // 创建解码器
                var decoder = await BitmapDecoder.CreateAsync(stream);

                // 核心技巧：直接将图片缩放到 9x8 的极小尺寸
                // 9x8 是为了产生 8 行，每行 8 个差异值 (9个像素产生8个间隔)
                var transform = new BitmapTransform
                {
                    ScaledWidth = 9,
                    ScaledHeight = 8,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };

                // 获取像素数据 (强制转为 8位灰度 Gray8)
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Gray8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage
                );

                var pixels = pixelData.DetachPixelData();
                ulong hash = 0;
                int bitIndex = 0;

                // 计算哈希：对比每行相邻像素的亮度
                // 8 行
                for (int y = 0; y < 8; y++)
                {
                    // 每行比较 8 次
                    for (int x = 0; x < 8; x++)
                    {
                        // 灰度模式下，数组索引即亮度值
                        var left = pixels[y * 9 + x];
                        var right = pixels[y * 9 + x + 1];

                        // 如果左边比右边亮，该位记为 1，否则为 0
                        if (left > right)
                        {
                            hash |= (1UL << bitIndex);
                        }
                        bitIndex++;
                    }
                }

                return hash;
            }
            catch
            {
                // 如果不是图片或文件损坏，返回 null
                return null;
            }
        }

        // 3. 新增：计算汉明距离 (比较两个指纹的相似度)
        // 距离越小，图片越相似。0 表示完全一样。
        public static int CalculateHammingDistance(ulong hash1, ulong hash2)
        {
            // 异或运算找出不同的位
            ulong xor = hash1 ^ hash2;
            int distance = 0;

            // 统计有多少个 1 (即有多少位不同)
            while (xor > 0)
            {
                distance += (int)(xor & 1);
                xor >>= 1;
            }
            return distance;
        }
    }
}