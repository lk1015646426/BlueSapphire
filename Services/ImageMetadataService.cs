using System;
using System.IO;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace BlueSapphire.Services
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
                using var stream = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);

                uint width = imageProperties.Width > 0 ? imageProperties.Width : decoder.PixelWidth;
                uint height = imageProperties.Height > 0 ? imageProperties.Height : decoder.PixelHeight;
                DateTimeOffset? dateTaken = imageProperties.DateTaken == default ? null : imageProperties.DateTaken;

                return new ImageMetadataInfo(
                    width,
                    height,
                    GetFormatDisplayName(file.Name),
                    GetBitsPerPixel(decoder.BitmapPixelFormat),
                    dateTaken);
            }
            catch
            {
                try
                {
                    var imageProperties = await file.Properties.GetImagePropertiesAsync();
                    return new ImageMetadataInfo(
                        imageProperties.Width,
                        imageProperties.Height,
                        GetFormatDisplayName(file.Name),
                        null,
                        imageProperties.DateTaken == default ? null : imageProperties.DateTaken);
                }
                catch
                {
                    return null;
                }
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
