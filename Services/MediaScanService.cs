using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public static class MediaScanService
    {
        private const int SourceImageSize = 32;
        private const int HashMatrixSize = 8;

        public static async Task<string> ComputeQuickHeaderFooterHashAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                const int chunkSize = 4096;
                byte[] buffer = new byte[chunkSize * 2];
                int bytesRead;

                if (stream.Length <= buffer.Length)
                {
                    bytesRead = await ReadExactlyAsync(stream, buffer, 0, (int)stream.Length);
                }
                else
                {
                    await ReadExactlyAsync(stream, buffer, 0, chunkSize);
                    stream.Seek(-chunkSize, SeekOrigin.End);
                    await ReadExactlyAsync(stream, buffer, chunkSize, chunkSize);
                    bytesRead = buffer.Length;
                }

                using var md5 = MD5.Create();
                byte[] hashBytes = md5.ComputeHash(buffer, 0, bytesRead);
                return ConvertToHex(hashBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static async Task<ulong?> ComputePHashAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var transform = new BitmapTransform
                {
                    ScaledWidth = SourceImageSize,
                    ScaledHeight = SourceImageSize,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] bytes = pixelData.DetachPixelData();
                double[,] grayscale = BuildGrayscaleMatrix(bytes);
                double[,] dct = ComputeDct(grayscale);

                double[] lowFrequency = new double[HashMatrixSize * HashMatrixSize - 1];
                int index = 0;

                for (int y = 0; y < HashMatrixSize; y++)
                {
                    for (int x = 0; x < HashMatrixSize; x++)
                    {
                        if (x == 0 && y == 0)
                        {
                            continue;
                        }

                        lowFrequency[index++] = dct[y, x];
                    }
                }

                double median = lowFrequency.OrderBy(value => value).ElementAt(lowFrequency.Length / 2);
                ulong hash = 0;

                for (int bit = 0; bit < lowFrequency.Length; bit++)
                {
                    if (lowFrequency[bit] >= median)
                    {
                        hash |= 1UL << bit;
                    }
                }

                return hash;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string> ComputeMD5Async(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                using var md5 = MD5.Create();
                byte[] hashBytes = await Task.Run(() => md5.ComputeHash(stream));
                return ConvertToHex(hashBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static int HammingDistance(ulong hash1, ulong hash2)
        {
            return BitOperations.PopCount(hash1 ^ hash2);
        }

        private static double[,] BuildGrayscaleMatrix(byte[] bytes)
        {
            var grayscale = new double[SourceImageSize, SourceImageSize];

            for (int y = 0; y < SourceImageSize; y++)
            {
                for (int x = 0; x < SourceImageSize; x++)
                {
                    int pixelIndex = (y * SourceImageSize + x) * 4;
                    byte blue = bytes[pixelIndex];
                    byte green = bytes[pixelIndex + 1];
                    byte red = bytes[pixelIndex + 2];
                    grayscale[y, x] = (0.299 * red) + (0.587 * green) + (0.114 * blue);
                }
            }

            return grayscale;
        }

        private static double[,] ComputeDct(double[,] input)
        {
            var output = new double[SourceImageSize, SourceImageSize];

            for (int v = 0; v < SourceImageSize; v++)
            {
                for (int u = 0; u < SourceImageSize; u++)
                {
                    double sum = 0;
                    for (int y = 0; y < SourceImageSize; y++)
                    {
                        for (int x = 0; x < SourceImageSize; x++)
                        {
                            sum += input[y, x]
                                * Math.Cos(((2 * x) + 1) * u * Math.PI / (2 * SourceImageSize))
                                * Math.Cos(((2 * y) + 1) * v * Math.PI / (2 * SourceImageSize));
                        }
                    }

                    output[v, u] = GetScaleFactor(u) * GetScaleFactor(v) * sum;
                }
            }

            return output;
        }

        private static double GetScaleFactor(int index)
        {
            return index == 0
                ? Math.Sqrt(1.0 / SourceImageSize)
                : Math.Sqrt(2.0 / SourceImageSize);
        }

        private static string ConvertToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }
    }
}
