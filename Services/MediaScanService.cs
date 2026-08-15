using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BlueSapphire.Services
{
    /// <summary>
    /// 2145 位感知指纹：细粒度横向 dHash 1056 位（33×33 网格）+ 结构图 1089 位
    /// （每个采样格亮度是否高于全图亮度中点，编码"暗区/亮区的形状与位置"）。
    /// 64 位（9×8）下所有 UI 截图塌缩成相同粗布局，实测产生数百对误报；
    /// 256 位（17×16）无法区分"同版式不同文字"的截图；
    /// 纯横向 1024 位仍漏掉"垂直结构不同"的截图（完整登录页 vs 加载中的黑块页，
    /// 都是"白底+居中黑块"，横向采样下文字被抹平，距离仅 13）；
    /// 纵向 dHash（无论细/粗粒度）对同屏重截的细微内容波动过敏感，真重复距离被推到 57。
    /// 结构图用中点亮度阈值直接编码暗区形状：黑块页的暗区是一整块矩形，
    /// 登录页的暗区是按钮+文字行，两者结构图差异显著；同屏重截结构图几乎一致。
    /// </summary>
    public readonly struct PerceptualHash : IEquatable<PerceptualHash>
    {
        public const int WordCount = 34; // 34 × 64 = 2176 ≥ 2145 bits

        private readonly ulong[] _words;

        public PerceptualHash(ulong[] words)
        {
            if (words is null) throw new ArgumentNullException(nameof(words));
            if (words.Length != WordCount)
                throw new ArgumentException($"Requires exactly {WordCount} words.", nameof(words));
            _words = words;
        }

        public ulong this[int index] => _words[index];

        public bool Equals(PerceptualHash other) => _words.AsSpan().SequenceEqual(other._words);

        public override bool Equals(object? obj) => obj is PerceptualHash other && Equals(other);

        public override int GetHashCode()
        {
            ulong h = 14695981039346656037;
            foreach (ulong w in _words)
            {
                h ^= w;
                h *= 1099511628211;
            }

            return (int)(h ^ (h >> 32));
        }

        public static bool operator ==(PerceptualHash left, PerceptualHash right) => left.Equals(right);

        public static bool operator !=(PerceptualHash left, PerceptualHash right) => !left.Equals(right);
    }

    public static class MediaScanService
    {
        // ========================= Constants =========================

        private const int DHashWidth = 33;
        private const int DHashHeight = 33;
        private const int ExifReadSize = 65536; // 64KB — enough for any EXIF header

        // ========================= Exact Dedup: Quick Header/Footer Hash =========================

        public static async Task<string> ComputeQuickHeaderFooterHashAsync(
            StorageFile file,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                const int chunkSize = 4096;
                byte[] buffer = new byte[chunkSize * 2];
                int bytesRead;

                if (stream.Length <= buffer.Length)
                {
                    bytesRead = await ReadExactlyAsync(
                        stream,
                        buffer,
                        0,
                        (int)stream.Length,
                        cancellationToken);
                }
                else
                {
                    await ReadExactlyAsync(stream, buffer, 0, chunkSize, cancellationToken);
                    stream.Seek(-chunkSize, SeekOrigin.End);
                    await ReadExactlyAsync(stream, buffer, chunkSize, chunkSize, cancellationToken);
                    bytesRead = buffer.Length;
                }

                byte[] hashBytes = SHA256.HashData(buffer.AsSpan(0, bytesRead));
                return ConvertToHex(hashBytes);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return string.Empty;
            }
        }

        // ========================= Exact Dedup: Full SHA-256 =========================

        public static async Task<string> ComputeSHA256Async(
            StorageFile file,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                using var sha256 = SHA256.Create();
                byte[] hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
                return ConvertToHex(hashBytes);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return string.Empty;
            }
        }

        // ========================= Similar Image Detection: dHash =========================

        /// <summary>
        /// Computes a 256-bit difference hash (dHash) for perceptual image similarity.
        /// <para>
        /// Fast path (~1-5ms): Extracts the EXIF thumbnail embedded in JPEG files using pure
        /// FileStream IO (no WinRT Shell overhead), then decodes the tiny thumbnail (~10-30KB).
        /// </para>
        /// <para>
        /// Slow path (~50-200ms): For non-JPEG or files without EXIF thumbnails, falls back
        /// to direct file decode via BitmapDecoder (still bypasses Shell thumbnail service).
        /// </para>
        /// </summary>
        public static async Task<PerceptualHash?> ComputeDHashAsync(string filePath)
        {
            try
            {
                // Fast path: extract EXIF thumbnail with pure .NET IO (no WinRT at all)
                byte[]? thumbBytes = ExtractExifThumbnail(filePath);

                IRandomAccessStream decodeStream;
                if (thumbBytes != null && thumbBytes.Length >= 100)
                {
                    // Decode the tiny EXIF thumbnail JPEG — extremely fast
                    decodeStream = await CreateMemoryStreamAsync(thumbBytes);
                }
                else
                {
                    // Fallback: direct file decode (still avoids Shell thumbnail service)
                    var sf = await StorageFile.GetFileFromPathAsync(filePath);
                    decodeStream = await sf.OpenAsync(FileAccessMode.Read);
                }

                try
                {
                    var decoder = await BitmapDecoder.CreateAsync(decodeStream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = DHashWidth,   // 33 pixels wide
                        ScaledHeight = DHashHeight, // 33 pixels tall
                        // Linear 区域平均：单点采样的哈希对细微位移和稀疏细节（文字、线条）不稳定，
                        // 且会让截图类"大面积平坦+少量细节"图片逃过平坦检测。
                        InterpolationMode = BitmapInterpolationMode.Linear
                    };

                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        transform,
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    return BuildDHash(pixelData.DetachPixelData());
                }
                finally
                {
                    decodeStream.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Computes a 2145-bit fingerprint from a 33×33 BGRA8 buffer:
        /// 1056 fine horizontal dHash bits (left vs right neighbor) + 1089 structure-map bits
        /// (whether each sampled cell is brighter than the image's luminance midpoint,
        /// encoding the shape and position of dark/light regions).
        /// <para>
        /// Returns null when the downsampled image is too flat (near-zero luminance contrast)
        /// or degenerates to all-0/all-1 bits: such fingerprints cannot distinguish content,
        /// and treating them as valid is the main source of "obviously different images
        /// reported as similar" (e.g. screenshots, solid-color or white-background photos
        /// all collapse to a near-zero hash).
        /// </para>
        /// </summary>
        internal static PerceptualHash? BuildDHash(byte[] pixels)
        {
            const int minContrastForDHash = 3200; // ≈5% of the 65535 full luminance scale
            const int hBits = (DHashWidth - 1) * DHashHeight; // 32×33 = 1056 horizontal bits

            // ---- Pass 1: luminance grid + contrast range ----
            Span<int> lum = stackalloc int[DHashWidth * DHashHeight];
            int minLum = int.MaxValue;
            int maxLum = int.MinValue;
            for (int y = 0; y < DHashHeight; y++)
            {
                for (int x = 0; x < DHashWidth; x++)
                {
                    int offset = (y * DHashWidth + x) * 4;
                    // Integer luminance: 77R + 150G + 29B ≈ 0.299R + 0.587G + 0.114B (×256)
                    int l = 77 * pixels[offset + 2] + 150 * pixels[offset + 1] + 29 * pixels[offset];
                    lum[y * DHashWidth + x] = l;
                    if (l < minLum) minLum = l;
                    if (l > maxLum) maxLum = l;
                }
            }

            // Flat image (solid color, screenshot, white-background shot): no usable structure.
            if (maxLum - minLum < minContrastForDHash)
            {
                return null;
            }

            // ---- Pass 2: fine horizontal difference bits ----
            var words = new ulong[PerceptualHash.WordCount];
            void SetBit(int bit) => words[bit >> 6] |= 1UL << (bit & 63);

            int horizontalOnes = 0;
            for (int y = 0; y < DHashHeight; y++)
            {
                for (int x = 0; x < DHashWidth - 1; x++)
                {
                    if (lum[y * DHashWidth + x] > lum[y * DHashWidth + x + 1])
                    {
                        SetBit(y * (DHashWidth - 1) + x);
                        horizontalOnes++;
                    }
                }
            }

            // Pure horizontal/vertical gradient: all difference bits identical (all-0 or all-1);
            // any two such images would collide regardless of the structure map.
            if (horizontalOnes == 0 || horizontalOnes == hBits)
            {
                return null;
            }

            // ---- Pass 3: structure map bits (dark-region shape) ----
            // Otsu 自适应阈值分离"前景元素"与"背景"：白底页自动落在白背景与浅灰文字
            // 之间（结构图=文字行形状），深色页落在暗背景与亮卡片之间（结构图=布局）。
            // 固定中点阈值会让白底页结构图几乎全 1，完全丧失区分力（实测不同白底页 d=29）。
            int otsu = ComputeOtsuThreshold(lum);
            for (int y = 0; y < DHashHeight; y++)
            {
                for (int x = 0; x < DHashWidth; x++)
                {
                    if (lum[y * DHashWidth + x] > otsu)
                    {
                        SetBit(hBits + y * DHashWidth + x);
                    }
                }
            }

            var hash = new PerceptualHash(words);

            return hash;
        }

        // ========================= Hamming Distance =========================

        public static int HammingDistance(PerceptualHash hash1, PerceptualHash hash2)
        {
            int distance = 0;
            for (int i = 0; i < PerceptualHash.WordCount; i++)
            {
                distance += BitOperations.PopCount(hash1[i] ^ hash2[i]);
            }

            return distance;
        }

        // ========================= Otsu Threshold =========================

        /// <summary>
        /// Otsu 大津法：在采样亮度网格上找最优二值化阈值，最大化前景/背景类间方差。
        /// 用于结构图分量：自动适配白底页（阈值落在白背景与浅灰文字之间）与深色页
        /// （阈值落在暗背景与亮卡片之间），无需人工标定。
        /// </summary>
        internal static int ComputeOtsuThreshold(Span<int> lum)
        {
            const int bins = 256;
            Span<int> hist = stackalloc int[bins];
            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (int v in lum)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (max == min) return min;

            foreach (int v in lum)
            {
                hist[(v - min) * (bins - 1) / (max - min)]++;
            }

            long total = lum.Length;
            long sumAll = 0;
            for (int i = 0; i < bins; i++) sumAll += (long)i * hist[i];

            long sumB = 0;
            long wB = 0;
            double bestVariance = -1;
            int bestBin = 0;
            for (int t = 0; t < bins; t++)
            {
                wB += hist[t];
                if (wB == 0) continue;
                long wF = total - wB;
                if (wF == 0) break;

                sumB += (long)t * hist[t];
                double meanB = (double)sumB / wB;
                double meanF = (double)(sumAll - sumB) / wF;
                double between = (double)wB * wF * (meanB - meanF) * (meanB - meanF);
                if (between > bestVariance)
                {
                    bestVariance = between;
                    bestBin = t;
                }
            }

            // 把 bin 索引映射回原始亮度刻度
            return min + bestBin * (max - min) / (bins - 1);
        }

        // ========================= EXIF Thumbnail Extraction (Pure .NET IO) =========================

        /// <summary>
        /// Reads the first 64KB of a JPEG file and extracts the embedded EXIF thumbnail.
        /// Returns null for non-JPEG files or files without an EXIF thumbnail.
        /// This method uses only System.IO — zero WinRT calls.
        /// </summary>
        private static byte[]? ExtractExifThumbnail(string filePath)
        {
            try
            {
                byte[] buf;
                int totalRead;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    int toRead = (int)Math.Min(fs.Length, ExifReadSize);
                    if (toRead < 12) return null;
                    buf = new byte[toRead];
                    totalRead = 0;
                    while (totalRead < toRead)
                    {
                        int n = fs.Read(buf, totalRead, toRead - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                    }
                }

                if (totalRead < 12) return null;

                // Verify JPEG SOI marker
                if (buf[0] != 0xFF || buf[1] != 0xD8)
                    return null;

                // Scan JPEG markers to find APP1 (EXIF)
                int pos = 2;
                while (pos + 4 <= totalRead)
                {
                    if (buf[pos] != 0xFF) return null;
                    byte marker = buf[pos + 1];

                    if (marker == 0xE1) // APP1 — EXIF data
                    {
                        int segLen = (buf[pos + 2] << 8) | buf[pos + 3];
                        int segData = pos + 4;
                        int segEnd = Math.Min(pos + 2 + segLen, totalRead);

                        // Verify "Exif\0\0" header
                        if (segData + 6 > segEnd) return null;
                        if (buf[segData] != 0x45 || buf[segData + 1] != 0x78 ||
                            buf[segData + 2] != 0x69 || buf[segData + 3] != 0x66 ||
                            buf[segData + 4] != 0x00 || buf[segData + 5] != 0x00)
                            return null;

                        return ParseTiffThumbnail(buf, segData + 6, segEnd);
                    }

                    // Stop at SOS (start of scan) or EOI — no more metadata
                    if (marker == 0xDA || marker == 0xD9) break;
                    // Skip padding bytes
                    if (marker == 0x00) { pos++; continue; }

                    // Skip this marker segment
                    if (pos + 3 >= totalRead) return null;
                    int len = (buf[pos + 2] << 8) | buf[pos + 3];
                    pos += 2 + len;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses a TIFF structure (inside EXIF APP1) to locate the IFD1 thumbnail data.
        /// IFD1 tags 0x0201 (JpegIFOffset) and 0x0202 (JpegIFByteCount) point to the thumbnail JPEG.
        /// </summary>
        private static byte[]? ParseTiffThumbnail(byte[] buf, int tiffStart, int limit)
        {
            if (tiffStart + 8 > limit) return null;

            // Determine byte order
            bool big = buf[tiffStart] == 0x4D && buf[tiffStart + 1] == 0x4D; // "MM" = big-endian
            bool little = buf[tiffStart] == 0x49 && buf[tiffStart + 1] == 0x49; // "II" = little-endian
            if (!big && !little) return null;

            // Verify TIFF magic number (42)
            if (R16(buf, tiffStart + 2, big) != 42) return null;

            // Read IFD0 offset
            uint ifd0Offset = R32(buf, tiffStart + 4, big);
            int ifd0Pos = tiffStart + (int)ifd0Offset;
            if (ifd0Pos + 2 > limit) return null;

            // Skip IFD0 entries to find IFD1 link
            int ifd0Count = R16(buf, ifd0Pos, big);
            int ifd1LinkPos = ifd0Pos + 2 + ifd0Count * 12;
            if (ifd1LinkPos + 4 > limit) return null;

            uint ifd1Offset = R32(buf, ifd1LinkPos, big);
            if (ifd1Offset == 0) return null;

            // Read IFD1 (thumbnail IFD)
            int ifd1Pos = tiffStart + (int)ifd1Offset;
            if (ifd1Pos + 2 > limit) return null;
            int ifd1Count = R16(buf, ifd1Pos, big);

            uint thumbOffset = 0, thumbLength = 0;
            for (int i = 0; i < ifd1Count; i++)
            {
                int entry = ifd1Pos + 2 + i * 12;
                if (entry + 12 > limit) break;

                ushort tag = R16(buf, entry, big);
                if (tag == 0x0201)      // JpegIFOffset
                    thumbOffset = R32(buf, entry + 8, big);
                else if (tag == 0x0202) // JpegIFByteCount
                    thumbLength = R32(buf, entry + 8, big);
            }

            if (thumbOffset == 0 || thumbLength == 0 || thumbLength > 500_000)
                return null;

            int absOffset = tiffStart + (int)thumbOffset;
            int absEnd = absOffset + (int)thumbLength;
            if (absOffset < 0 || absEnd > buf.Length)
                return null;

            // Verify embedded thumbnail starts with JPEG SOI
            if (buf[absOffset] != 0xFF || buf[absOffset + 1] != 0xD8)
                return null;

            var result = new byte[thumbLength];
            System.Buffer.BlockCopy(buf, absOffset, result, 0, (int)thumbLength);
            return result;
        }

        // ========================= TIFF Byte-Order Helpers =========================

        private static ushort R16(byte[] b, int o, bool big) =>
            big ? (ushort)((b[o] << 8) | b[o + 1])
                : (ushort)(b[o] | (b[o + 1] << 8));

        private static uint R32(byte[] b, int o, bool big) =>
            big ? ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3]
                : b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24);

        // ========================= Stream Utilities =========================

        private static async Task<InMemoryRandomAccessStream> CreateMemoryStreamAsync(byte[] data)
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(data);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            return stream;
        }

        internal static string ConvertToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        internal static async Task<int> ReadExactlyAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(offset + totalRead, count - totalRead),
                    cancellationToken);
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
