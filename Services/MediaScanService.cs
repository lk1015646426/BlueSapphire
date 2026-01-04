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

        public class MediaFile
        {
            public required StorageFile File { get; set; }
            public ulong FileSize { get; set; }
            public required string Md5Hash { get; set; }
            public ulong? VisualHash { get; set; }
        }

        // --- 核心方法 (Static) ---

        /// <summary>
        /// [新增] 快速头尾哈希比对 (用于三级比对法的第二步)
        /// 读取前4KB和后4KB，能过滤99%的大小相同但内容不同的文件
        /// </summary>
        public static async Task<string> ComputeQuickHeaderFooterHashAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                long length = stream.Length;
                int bufferSize = 4096; // 4KB
                byte[] buffer = new byte[bufferSize * 2]; // 8KB
                int bytesRead = 0;

                if (length <= bufferSize * 2)
                {
                    // 如果文件很小，直接读完
                    bytesRead = await stream.ReadAsync(buffer, 0, (int)length);
                }
                else
                {
                    // 读取头部
                    await stream.ReadAsync(buffer, 0, bufferSize);
                    // 跳转到尾部
                    stream.Seek(-bufferSize, SeekOrigin.End);
                    // 读取尾部
                    await stream.ReadAsync(buffer, bufferSize, bufferSize);
                    bytesRead = bufferSize * 2;
                }

                using var md5 = MD5.Create();
                var hashBytes = md5.ComputeHash(buffer, 0, bytesRead);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// [核心] 计算图片的感知哈希 (pHash)
        /// </summary>
        public static async Task<ulong?> ComputePHashAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var transform = new BitmapTransform
                {
                    ScaledWidth = 32,
                    ScaledHeight = 32,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage
                );

                var bytes = pixelData.DetachPixelData();
                var grays = new List<byte>();

                for (int i = 0; i < bytes.Length; i += 4)
                {
                    byte b = bytes[i];
                    byte g = bytes[i + 1];
                    byte r = bytes[i + 2];
                    var gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    grays.Add(gray);
                }

                double average = grays.Average(b => b);
                ulong hash = 0;

                for (int i = 0; i < 64 && i < grays.Count; i++)
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
                return null;
            }
        }

        /// <summary>
        /// 计算文件的全量 MD5 (精确查重用)
        /// </summary>
        public static async Task<string> ComputeMD5Async(StorageFile file)
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