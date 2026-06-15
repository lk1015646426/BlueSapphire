using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BlueSapphire.Services
{
    public enum ImageConversionTarget
    {
        Jpeg,
        Png,
        Bmp
    }

    public readonly record struct ImageCropFrame(uint X, uint Y, uint Width, uint Height);

    public sealed record ImageProcessResult(string SourcePath, string? OutputPath, bool Success, string Message)
    {
        public static ImageProcessResult Succeeded(string sourcePath, string outputPath, string message)
        {
            return new ImageProcessResult(sourcePath, outputPath, true, message);
        }

        public static ImageProcessResult Failed(string sourcePath, string message)
        {
            return new ImageProcessResult(sourcePath, null, false, message);
        }
    }

    public class ImageProcessingService
    {
        public bool CanProcess(string? fileName) => MediaFileCatalog.IsImage(fileName);

        public string GetTargetExtension(ImageConversionTarget target)
        {
            return target switch
            {
                ImageConversionTarget.Jpeg => ".jpg",
                ImageConversionTarget.Png => ".png",
                ImageConversionTarget.Bmp => ".bmp",
                _ => string.Empty
            };
        }

        public string GetTargetDisplayName(ImageConversionTarget target)
        {
            return target switch
            {
                ImageConversionTarget.Jpeg => "JPEG",
                ImageConversionTarget.Png => "PNG",
                ImageConversionTarget.Bmp => "BMP",
                _ => target.ToString()
            };
        }

        public async Task<ImageProcessResult> ConvertAsync(string sourcePath, FormatConvertOptions options, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            string targetExtension = GetTargetExtension(options.TargetFormat);
            if (string.Equals(Path.GetExtension(sourcePath), targetExtension, StringComparison.OrdinalIgnoreCase) && options.Quality >= 0.95)
            {
                // return ImageProcessResult.Failed(sourcePath, "源文件已经是目标格式。");
            }

            string outputPath = BuildOutputPath(sourcePath, string.Empty, targetExtension);
            return await TranscodeAsync(
                sourcePath,
                outputPath,
                GetEncoderId(options.TargetFormat),
                transform: null,
                quality: options.TargetFormat == ImageConversionTarget.Jpeg ? options.Quality : null,
                successMessage: $"已转换为 {options.TargetFormat}。",
                cancellationToken);
        }

        public async Task<ImageProcessResult> ProcessAdvancedAsync(string sourcePath, AdvancedEditOptions options, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            try
            {
                using var sourceStream = await OpenReadStreamAsync(sourcePath);
                var decoder = await BitmapDecoder.CreateAsync(sourceStream);
                var transform = new BitmapTransform();

                // 1. Crop
                if (options.IsCropEnabled)
                {
                    if (options.UseExactCrop)
                    {
                        uint x = options.ExactCropX;
                        uint y = options.ExactCropY;
                        uint w = options.ExactCropWidth;
                        uint h = options.ExactCropHeight;

                        if (x + w > decoder.PixelWidth) w = decoder.PixelWidth > x ? decoder.PixelWidth - x : 1;
                        if (y + h > decoder.PixelHeight) h = decoder.PixelHeight > y ? decoder.PixelHeight - y : 1;

                        transform.Bounds = new BitmapBounds
                        {
                            X = x,
                            Y = y,
                            Width = Math.Max(1, w),
                            Height = Math.Max(1, h)
                        };
                    }
                    else if (options.CropAspectRatio > 0)
                    {
                        var cropFrame = CalculateCenteredCropFrame(decoder.PixelWidth, decoder.PixelHeight, options.CropAspectRatio);
                        transform.Bounds = new BitmapBounds
                        {
                            X = cropFrame.X,
                            Y = cropFrame.Y,
                            Width = cropFrame.Width,
                            Height = cropFrame.Height
                        };
                    }
                }

                // 2. Resize
                uint currentWidth = transform.Bounds.Width > 0 ? transform.Bounds.Width : decoder.PixelWidth;
                uint currentHeight = transform.Bounds.Height > 0 ? transform.Bounds.Height : decoder.PixelHeight;
                uint targetWidth = options.TargetWidth;
                uint targetHeight = options.TargetHeight;

                if (targetWidth > 0 || targetHeight > 0)
                {
                    if (options.KeepAspectRatio && targetWidth > 0 && targetHeight > 0)
                    {
                        double sourceRatio = currentWidth / (double)currentHeight;
                        double targetRatio = targetWidth / (double)targetHeight;
                        if (sourceRatio > targetRatio)
                        {
                            targetHeight = Math.Max(1u, (uint)Math.Round(targetWidth / sourceRatio));
                        }
                        else
                        {
                            targetWidth = Math.Max(1u, (uint)Math.Round(targetHeight * sourceRatio));
                        }
                    }

                    if (targetWidth == 0) targetWidth = currentWidth;
                    if (targetHeight == 0) targetHeight = currentHeight;

                    transform.ScaledWidth = targetWidth;
                    transform.ScaledHeight = targetHeight;
                    transform.InterpolationMode = BitmapInterpolationMode.Fant;
                }

                var (encoderId, extension, displayName) = ResolvePreferredEditableFormat(sourcePath);
                
                // If Target Size is enabled, we MUST use JPEG.
                if (options.IsTargetSizeEnabled)
                {
                    encoderId = BitmapEncoder.JpegEncoderId;
                    extension = ".jpg";
                    displayName = "JPEG";
                }

                string outputPath = BuildOutputPath(sourcePath, "_edited", extension);

                if (options.IsTargetSizeEnabled && options.TargetMaxFileSizeBytes > 0 && encoderId == BitmapEncoder.JpegEncoderId)
                {
                    // Advanced: Binary search for target size range
                    return await EncodeWithTargetSizeAsync(
                        sourcePath, outputPath, decoder, encoderId, transform, options.TargetMinFileSizeBytes, options.TargetMaxFileSizeBytes, cancellationToken);
                }
                else
                {
                    // Standard transcode
                    return await TranscodeAsync(
                        sourcePath,
                        outputPath,
                        encoderId,
                        transform,
                        quality: encoderId == BitmapEncoder.JpegEncoderId ? 0.92d : null,
                        successMessage: $"已完成图片编辑（{displayName}）。",
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        private static async Task<ImageProcessResult> EncodeWithTargetSizeAsync(
            string sourcePath,
            string outputPath,
            BitmapDecoder decoder,
            Guid encoderId,
            BitmapTransform transform,
            long targetMinBytes,
            long targetMaxBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                var pixelProvider = await decoder.GetPixelDataAsync(
                    decoder.BitmapPixelFormat,
                    BitmapAlphaMode.Ignore, // JPEG ignores alpha
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                byte[] pixels = pixelProvider.DetachPixelData();
                uint width = transform.ScaledWidth > 0 ? transform.ScaledWidth : (transform.Bounds.Width > 0 ? transform.Bounds.Width : decoder.PixelWidth);
                uint height = transform.ScaledHeight > 0 ? transform.ScaledHeight : (transform.Bounds.Height > 0 ? transform.Bounds.Height : decoder.PixelHeight);

                double minQuality = 0.01;
                double maxQuality = 1.0;
                double bestQuality = 0.8;
                byte[]? bestBytes = null;
                long targetMidBytes = (targetMinBytes + targetMaxBytes) / 2;

                int maxIterations = 8;
                for (int i = 0; i < maxIterations; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    double currentQuality = (minQuality + maxQuality) / 2.0;
                    
                    using var memoryStream = new InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(encoderId, memoryStream);
                    var propertySet = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue((float)currentQuality, PropertyType.Single) } };
                    await encoder.BitmapProperties.SetPropertiesAsync(propertySet);

                    encoder.SetPixelData(
                        decoder.BitmapPixelFormat,
                        BitmapAlphaMode.Ignore,
                        width, height,
                        decoder.DpiX, decoder.DpiY,
                        pixels);

                    await encoder.FlushAsync();
                    
                    long currentSize = (long)memoryStream.Size;
                    
                    using (var reader = new DataReader(memoryStream.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync((uint)currentSize);
                        bestBytes = new byte[currentSize];
                        reader.ReadBytes(bestBytes);
                    }
                    bestQuality = currentQuality;

                    // If we fall inside the requested range, stop searching immediately
                    if (currentSize >= targetMinBytes && currentSize <= targetMaxBytes)
                    {
                        break;
                    }

                    if (currentSize > targetMaxBytes)
                    {
                        maxQuality = currentQuality;
                    }
                    else if (currentSize < targetMinBytes)
                    {
                        minQuality = currentQuality;
                    }
                }

                if (bestBytes != null)
                {
                    var outputFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
                    var outputFile = await outputFolder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.FailIfExists);
                    using var destinationStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
                    using var dataWriter = new DataWriter(destinationStream);
                    dataWriter.WriteBytes(bestBytes);
                    await dataWriter.StoreAsync();
                    
                    return ImageProcessResult.Succeeded(sourcePath, outputPath, $"已编辑并压缩至 ~{bestBytes.Length / 1024}KB (质量 {bestQuality:P0})。");
                }
                
                return ImageProcessResult.Failed(sourcePath, "无法编码图片以满足目标大小。");
            }
            catch (Exception ex)
            {
                return ImageProcessResult.Failed(sourcePath, $"目标大小压缩失败: {ex.Message}");
            }
        }

        public async Task<ImageProcessResult> EnhanceAsync(string sourcePath, EnhanceOptions options, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            try
            {
                using var sourceStream = await OpenReadStreamAsync(sourcePath);
                var decoder = await BitmapDecoder.CreateAsync(sourceStream);
                var settings = new ImageEnhancementSettings(1d, options.Brightness, options.Contrast, options.Saturation, options.Sharpness);

                var transform = new BitmapTransform
                {
                    InterpolationMode = BitmapInterpolationMode.Fant
                };

                var pixelProvider = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                byte[] pixels = pixelProvider.DetachPixelData();
                byte[] enhancedPixels = EnhancePixels(pixels, decoder.PixelWidth, decoder.PixelHeight, settings);

                var (encoderId, extension, displayName) = ResolvePreferredEditableFormat(sourcePath);
                string outputPath = BuildOutputPath(sourcePath, "_enhanced", extension);

                return await EncodePixelBufferAsync(
                    sourcePath,
                    outputPath,
                    enhancedPixels,
                    decoder.PixelWidth,
                    decoder.PixelHeight,
                    decoder.DpiX,
                    decoder.DpiY,
                    encoderId,
                    $"已完成图片增强（{displayName}）。",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        public string BuildOutputPath(string sourcePath, string suffix, string extension)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string normalizedExtension = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
            string outputPath = Path.Combine(directory, $"{baseName}{suffix}{normalizedExtension}");

            if (!File.Exists(outputPath))
            {
                return outputPath;
            }

            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(directory, $"{baseName}{suffix}_{counter:D2}{normalizedExtension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
                counter++;
            }
        }

        public static ImageCropFrame CalculateCenteredCropFrame(uint sourceWidth, uint sourceHeight, double aspectRatio)
        {
            if (sourceWidth == 0 || sourceHeight == 0 || aspectRatio <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(aspectRatio), "图片尺寸和裁剪比例必须有效。");
            }

            double sourceRatio = sourceWidth / (double)sourceHeight;
            if (sourceRatio > aspectRatio)
            {
                uint cropWidth = Math.Min(sourceWidth, Math.Max(1u, (uint)Math.Floor(sourceHeight * aspectRatio)));
                uint x = (sourceWidth - cropWidth) / 2;
                return new ImageCropFrame(x, 0, cropWidth, sourceHeight);
            }

            uint cropHeight = Math.Min(sourceHeight, Math.Max(1u, (uint)Math.Floor(sourceWidth / aspectRatio)));
            uint y = (sourceHeight - cropHeight) / 2;
            return new ImageCropFrame(0, y, sourceWidth, cropHeight);
        }

        private static bool ValidateSourcePath(string sourcePath, out ImageProcessResult? result)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                result = ImageProcessResult.Failed(sourcePath, "源文件不存在。");
                return false;
            }

            if (!MediaFileCatalog.IsImage(sourcePath))
            {
                result = ImageProcessResult.Failed(sourcePath, "当前文件不是受支持的图片格式。");
                return false;
            }

            result = null;
            return true;
        }

        private static Guid GetEncoderId(ImageConversionTarget target)
        {
            return target switch
            {
                ImageConversionTarget.Jpeg => BitmapEncoder.JpegEncoderId,
                ImageConversionTarget.Png => BitmapEncoder.PngEncoderId,
                ImageConversionTarget.Bmp => BitmapEncoder.BmpEncoderId,
                _ => BitmapEncoder.JpegEncoderId
            };
        }

        private static (Guid EncoderId, string Extension, string DisplayName) ResolvePreferredEditableFormat(string sourcePath)
        {
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => (BitmapEncoder.JpegEncoderId, ".jpg", "JPEG"),
                ".png" => (BitmapEncoder.PngEncoderId, ".png", "PNG"),
                ".bmp" => (BitmapEncoder.BmpEncoderId, ".bmp", "BMP"),
                _ => (BitmapEncoder.JpegEncoderId, ".jpg", "JPEG")
            };
        }

        private static async Task<IRandomAccessStream> OpenReadStreamAsync(string sourcePath)
        {
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
            return await sourceFile.OpenReadAsync();
        }

        private static byte[] EnhancePixels(byte[] sourcePixels, uint width, uint height, ImageEnhancementSettings settings)
        {
            byte[] adjustedPixels = (byte[])sourcePixels.Clone();
            ApplyToneAdjustments(adjustedPixels, settings);
            return settings.SharpenAmount <= 0d
                ? adjustedPixels
                : ApplySharpen(adjustedPixels, width, height, settings.SharpenAmount);
        }

        private static void ApplyToneAdjustments(byte[] pixels, ImageEnhancementSettings settings)
        {
            var (low, high) = CalculateLuminanceWindow(pixels);
            double range = Math.Max(1d, high - low);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                double blue = StretchChannel(pixels[i], low, range);
                double green = StretchChannel(pixels[i + 1], low, range);
                double red = StretchChannel(pixels[i + 2], low, range);

                ApplyToneCurve(ref red, ref green, ref blue, settings);

                pixels[i] = ClampToByte(blue);
                pixels[i + 1] = ClampToByte(green);
                pixels[i + 2] = ClampToByte(red);
            }
        }

        private static (double Low, double High) CalculateLuminanceWindow(byte[] pixels)
        {
            int[] histogram = new int[256];
            int totalPixels = pixels.Length / 4;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                int luminance = (int)Math.Round(
                    pixels[i + 2] * 0.2126d +
                    pixels[i + 1] * 0.7152d +
                    pixels[i] * 0.0722d,
                    MidpointRounding.AwayFromZero);
                histogram[Math.Clamp(luminance, 0, 255)]++;
            }

            int lowerThreshold = Math.Max(1, (int)Math.Round(totalPixels * 0.01d, MidpointRounding.AwayFromZero));
            int upperThreshold = Math.Max(1, (int)Math.Round(totalPixels * 0.99d, MidpointRounding.AwayFromZero));

            int cumulative = 0;
            int low = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                cumulative += histogram[i];
                if (cumulative >= lowerThreshold)
                {
                    low = i;
                    break;
                }
            }

            cumulative = 0;
            int high = 255;
            for (int i = 0; i < histogram.Length; i++)
            {
                cumulative += histogram[i];
                if (cumulative >= upperThreshold)
                {
                    high = i;
                    break;
                }
            }

            return high <= low ? (0d, 255d) : (low, high);
        }

        private static double StretchChannel(byte value, double low, double range)
        {
            return Math.Clamp((value - low) * 255d / range, 0d, 255d);
        }

        private static void ApplyToneCurve(
            ref double red,
            ref double green,
            ref double blue,
            ImageEnhancementSettings settings)
        {
            red = ApplyContrastAndBrightness(red, settings.ContrastFactor, settings.BrightnessOffset);
            green = ApplyContrastAndBrightness(green, settings.ContrastFactor, settings.BrightnessOffset);
            blue = ApplyContrastAndBrightness(blue, settings.ContrastFactor, settings.BrightnessOffset);

            double gray = red * 0.2126d + green * 0.7152d + blue * 0.0722d;
            red = gray + (red - gray) * settings.SaturationFactor;
            green = gray + (green - gray) * settings.SaturationFactor;
            blue = gray + (blue - gray) * settings.SaturationFactor;
        }

        private static double ApplyContrastAndBrightness(double value, double contrastFactor, double brightnessOffset)
        {
            double normalized = value / 255d;
            double adjusted = ((normalized - 0.5d) * contrastFactor) + 0.5d + brightnessOffset;
            return Math.Clamp(adjusted * 255d, 0d, 255d);
        }

        private static byte[] ApplySharpen(byte[] pixels, uint width, uint height, double amount)
        {
            if (width < 3 || height < 3)
            {
                return pixels;
            }

            byte[] output = (byte[])pixels.Clone();
            int stride = checked((int)width * 4);

            for (int y = 1; y < height - 1; y++)
            {
                int rowOffset = checked((int)y * stride);
                for (int x = 1; x < width - 1; x++)
                {
                    int index = rowOffset + checked((int)x * 4);
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double center = pixels[index + channel];
                        double north = pixels[index - stride + channel];
                        double south = pixels[index + stride + channel];
                        double west = pixels[index - 4 + channel];
                        double east = pixels[index + 4 + channel];
                        double sharpened = center * (1d + 4d * amount) - amount * (north + south + west + east);
                        output[index + channel] = ClampToByte(sharpened);
                    }

                    output[index + 3] = pixels[index + 3];
                }
            }

            return output;
        }

        private static byte ClampToByte(double value)
        {
            return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        }

        private static async Task<ImageProcessResult> EncodePixelBufferAsync(
            string sourcePath,
            string outputPath,
            byte[] pixels,
            uint width,
            uint height,
            double dpiX,
            double dpiY,
            Guid encoderId,
            string successMessage,
            CancellationToken cancellationToken)
        {
            StorageFile? outputFile = null;

            try
            {
                var outputFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
                outputFile = await outputFolder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.FailIfExists);
                using var destinationStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(encoderId, destinationStream);

                if (encoderId == BitmapEncoder.JpegEncoderId)
                {
                    var propertySet = new BitmapPropertySet
                    {
                        { "ImageQuality", new BitmapTypedValue(0.94f, PropertyType.Single) }
                    };
                    await encoder.BitmapProperties.SetPropertiesAsync(propertySet);
                }

                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    encoderId == BitmapEncoder.JpegEncoderId ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Premultiplied,
                    width,
                    height,
                    dpiX,
                    dpiY,
                    pixels);

                await encoder.FlushAsync().AsTask(cancellationToken);
                return ImageProcessResult.Succeeded(sourcePath, outputPath, successMessage);
            }
            catch (OperationCanceledException)
            {
                if (outputFile != null) await TryDeleteAsync(outputFile);
                return ImageProcessResult.Failed(sourcePath, "图片处理已取消。");
            }
            catch (Exception ex)
            {
                if (outputFile != null) await TryDeleteAsync(outputFile);
                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        private static async Task<ImageProcessResult> TranscodeAsync(
            string sourcePath,
            string outputPath,
            Guid encoderId,
            BitmapTransform? transform,
            double? quality,
            string successMessage,
            CancellationToken cancellationToken)
        {
            StorageFile? outputFile = null;

            try
            {
                var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
                var outputFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
                outputFile = await outputFolder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.FailIfExists);

                using var sourceStream = await sourceFile.OpenReadAsync();
                using var destinationStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
                var decoder = await BitmapDecoder.CreateAsync(sourceStream);
                var encoder = await BitmapEncoder.CreateAsync(encoderId, destinationStream);

                if (quality.HasValue)
                {
                    var propertySet = new BitmapPropertySet
                    {
                        { "ImageQuality", new BitmapTypedValue((float)quality.Value, PropertyType.Single) }
                    };
                    await encoder.BitmapProperties.SetPropertiesAsync(propertySet);
                }

                BitmapTransform effectiveTransform = transform ?? new BitmapTransform();
                var pixelProvider = await decoder.GetPixelDataAsync(
                    decoder.BitmapPixelFormat,
                    encoderId == BitmapEncoder.JpegEncoderId ? BitmapAlphaMode.Ignore : decoder.BitmapAlphaMode,
                    effectiveTransform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                byte[] pixels = pixelProvider.DetachPixelData();
                uint width = effectiveTransform.ScaledWidth > 0
                    ? effectiveTransform.ScaledWidth
                    : effectiveTransform.Bounds.Width > 0 ? effectiveTransform.Bounds.Width : decoder.PixelWidth;
                uint height = effectiveTransform.ScaledHeight > 0
                    ? effectiveTransform.ScaledHeight
                    : effectiveTransform.Bounds.Height > 0 ? effectiveTransform.Bounds.Height : decoder.PixelHeight;

                encoder.SetPixelData(
                    decoder.BitmapPixelFormat,
                    encoderId == BitmapEncoder.JpegEncoderId ? BitmapAlphaMode.Ignore : decoder.BitmapAlphaMode,
                    width,
                    height,
                    decoder.DpiX,
                    decoder.DpiY,
                    pixels);

                await encoder.FlushAsync().AsTask(cancellationToken);
                return ImageProcessResult.Succeeded(sourcePath, outputPath, successMessage);
            }
            catch (OperationCanceledException)
            {
                if (outputFile != null) await TryDeleteAsync(outputFile);
                return ImageProcessResult.Failed(sourcePath, "图片处理已取消。");
            }
            catch (Exception ex)
            {
                if (outputFile != null) await TryDeleteAsync(outputFile);
                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        private static async Task TryDeleteAsync(StorageFile file)
        {
            try { await file.DeleteAsync(); } catch { }
        }

        private readonly record struct ImageEnhancementSettings(
            double ScaleFactor,
            double BrightnessOffset,
            double ContrastFactor,
            double SaturationFactor,
            double SharpenAmount);
    }
}
