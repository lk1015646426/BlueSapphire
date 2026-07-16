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
    public static class MediaScanService
    {
        // ========================= Constants =========================

        private const int DHashWidth = 9;
        private const int DHashHeight = 8;
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
        /// Computes a 64-bit difference hash (dHash) for perceptual image similarity.
        /// <para>
        /// Fast path (~1-5ms): Extracts the EXIF thumbnail embedded in JPEG files using pure
        /// FileStream IO (no WinRT Shell overhead), then decodes the tiny thumbnail (~10-30KB).
        /// </para>
        /// <para>
        /// Slow path (~50-200ms): For non-JPEG or files without EXIF thumbnails, falls back
        /// to direct file decode via BitmapDecoder (still bypasses Shell thumbnail service).
        /// </para>
        /// </summary>
        public static async Task<ulong?> ComputeDHashAsync(string filePath)
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
                        ScaledWidth = DHashWidth,   // 9 pixels wide
                        ScaledHeight = DHashHeight,  // 8 pixels tall
                        InterpolationMode = BitmapInterpolationMode.NearestNeighbor
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
        /// Computes a 64-bit difference hash from a 9×8 BGRA8 pixel buffer.
        /// Each bit represents whether the left pixel is brighter than its right neighbor.
        /// </summary>
        private static ulong BuildDHash(byte[] pixels)
        {
            ulong hash = 0;
            for (int y = 0; y < DHashHeight; y++)
            {
                for (int x = 0; x < DHashWidth - 1; x++)
                {
                    int bit = y * (DHashWidth - 1) + x;
                    int left = (y * DHashWidth + x) * 4;
                    int right = left + 4;

                    // Integer luminance: 77R + 150G + 29B ≈ 0.299R + 0.587G + 0.114B (×256)
                    int lumL = 77 * pixels[left + 2] + 150 * pixels[left + 1] + 29 * pixels[left];
                    int lumR = 77 * pixels[right + 2] + 150 * pixels[right + 1] + 29 * pixels[right];

                    if (lumL > lumR)
                    {
                        hash |= 1UL << bit;
                    }
                }
            }

            return hash;
        }

        // ========================= Hamming Distance =========================

        public static int HammingDistance(ulong hash1, ulong hash2)
        {
            return BitOperations.PopCount(hash1 ^ hash2);
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
