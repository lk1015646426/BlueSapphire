using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;

namespace BlueSapphire.Services
{
    public class MediaScanService
    {
        // pHash 的指纹大小 (8x8)
        private const int HashSize = 8;

        // 扫描结果的数据结构
        public class MediaFile
        {
            // 强制要求初始化时赋值，解决了 CS8618
            public required StorageFile File { get; set; }
            public ulong FileSize { get; set; }
            // 同上
            public required string Md5Hash { get; set; }
            public ulong? VisualHash { get; set; }
        }

        /// <summary>
        /// [核心] 计算图片的感知哈希 (pHash)
        /// 使用 Windows 原生 API，0 依赖，速度快
        /// </summary>
        public async Task<ulong?> ComputePHashAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenAsync(FileAccessMode.Read);

                // 1. 创建解码器 (自动识别 JPG, PNG, HEIC 等)
                var decoder = await BitmapDecoder.CreateAsync(stream);

                // 2. 预处理：强制缩放到 32x32 (为了性能和算法要求)
                // 虽然最终只需要 8x8，但先缩放到 32x32 进行平滑处理效果更好
                var transform = new BitmapTransform
                {
                    ScaledWidth = 32,
                    ScaledHeight = 32,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };

                // 3. 获取像素数据 (BGRA8 格式)
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage
                );

                var bytes = pixelData.DetachPixelData();
                var grays = new List<byte>();

                // 4. 转灰度 (B, G, R, A)
                for (int i = 0; i < bytes.Length; i += 4)
                {
                    byte b = bytes[i];
                    byte g = bytes[i + 1];
                    byte r = bytes[i + 2];
                    // 心理学灰度公式: 0.299R + 0.587G + 0.114B
                    var gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    grays.Add(gray);
                }

                // 5. 计算 DCT (离散余弦变换) 的简化版 -> 均值哈希
                // 这里为了性能使用简化的均值哈希 (Average Hash)
                // 如果需要更高级的 DCT-pHash，可以在这里扩展，但均值哈希对连拍去重已足够
                double average = grays.Average(b => b);
                ulong hash = 0;

                for (int i = 0; i < 64 && i < grays.Count; i++) // 取前 8x8 区域
                {
                    if (grays[i] >= average)
                    {
                        hash |= (1UL << i);
                    }
                }

                return hash;
            }
            catch
            {
                // 如果文件损坏或无法解码，返回 null
                return null;
            }
        }

        /// <summary>
        /// 计算文件的 MD5 (精确查重用)
        /// </summary>
        public async Task<string> ComputeMD5Async(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                using var md5 = MD5.Create();
                var hashBytes = await Task.Run(() => md5.ComputeHash(stream));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 计算两个 pHash 的汉明距离 (0=完全一样, <5=极度相似, <10=相似)
        /// </summary>
        public static int HammingDistance(ulong hash1, ulong hash2)
        {
            ulong x = hash1 ^ hash2;
            int distance = 0;
            while (x > 0)
            {
                distance++;
                x &= x - 1;
            }
            return distance;
        }
    }
}