using System;
using System.IO;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace BlueSapphire.Services.Media
{
    public sealed record ImageMetadataInfo(
        uint Width,
        uint Height,
        string FormatName,
        ushort? BitDepth,
        DateTimeOffset? DateTaken);

    public class ImageMetadataService
    {
        public async Task<ImageMetadataInfo?> TryReadAsync(StorageFile file)
        {
            if (!MediaFileCatalog.IsImage(file.Name))
            {
                return null;
            }

            try
            {
                var imageProperties = await file.Properties.GetImagePropertiesAsync();

                // Skip full file decoding (BitmapDecoder) to massively speed up loading thousands of images.
                // We sacrifice exact BitDepth detection for instant metadata retrieval.
                return new ImageMetadataInfo(
                    imageProperties.Width,
                    imageProperties.Height,
                    GetFormatDisplayName(file.Name),
                    null, // skip bit depth
                    imageProperties.DateTaken == default ? null : imageProperties.DateTaken);
            }
            catch
            {
                return null;
            }
        }

        public static string GetFormatDisplayName(string? fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "JPEG",
                ".png" => "PNG",
                ".bmp" => "BMP",
                ".gif" => "GIF",
                ".webp" => "WEBP",
                ".heic" => "HEIC",
                _ => string.IsNullOrWhiteSpace(extension) ? "图片" : extension.TrimStart('.').ToUpperInvariant()
            };
        }

        public static ushort? GetBitsPerPixel(BitmapPixelFormat pixelFormat)
        {
            return pixelFormat switch
            {
                BitmapPixelFormat.Rgba16 => 16,
                BitmapPixelFormat.Gray8 => 8,
                BitmapPixelFormat.Bgra8 => 32,
                BitmapPixelFormat.Rgba8 => 32,
                BitmapPixelFormat.Nv12 => 12,
                BitmapPixelFormat.Yuy2 => 16,
                BitmapPixelFormat.P010 => 24,
                _ => null
            };
        }
    }
}
