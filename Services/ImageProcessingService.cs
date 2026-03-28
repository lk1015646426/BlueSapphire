using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
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

    public enum ImageResizePreset
    {
        LongEdge1280,
        LongEdge1920,
        LongEdge2560
    }

    public enum ImageCropPreset
    {
        Square,
        Ratio4x3,
        Ratio16x9
    }

    public enum ImageCompressionPreset
    {
        Light,
        Balanced,
        Aggressive
    }

    public enum ImageEnhancementPreset
    {
        SmartFix,
        DetailBoost,
        LowLight
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
        private static readonly IReadOnlyDictionary<ImageCropPreset, double> CropRatioMap = new Dictionary<ImageCropPreset, double>
        {
            [ImageCropPreset.Square] = 1d,
            [ImageCropPreset.Ratio4x3] = 4d / 3d,
            [ImageCropPreset.Ratio16x9] = 16d / 9d
        };

        public bool TryParseConversionTarget(string? targetKey, out ImageConversionTarget target)
        {
            return Enum.TryParse(targetKey, ignoreCase: true, out target);
        }

        public bool TryParseResizePreset(string? presetKey, out ImageResizePreset preset)
        {
            return Enum.TryParse(presetKey, ignoreCase: true, out preset);
        }

        public bool TryParseCropPreset(string? presetKey, out ImageCropPreset preset)
        {
            return Enum.TryParse(presetKey, ignoreCase: true, out preset);
        }

        public bool TryParseCompressionPreset(string? presetKey, out ImageCompressionPreset preset)
        {
            return Enum.TryParse(presetKey, ignoreCase: true, out preset);
        }

        public bool TryParseEnhancementPreset(string? presetKey, out ImageEnhancementPreset preset)
        {
            return Enum.TryParse(presetKey, ignoreCase: true, out preset);
        }

        public string GetTargetDisplayName(ImageConversionTarget target)
        {
            return target switch
            {
                ImageConversionTarget.Jpeg => "JPEG",
                ImageConversionTarget.Png => "PNG",
                ImageConversionTarget.Bmp => "BMP",
                _ => target.ToString().ToUpperInvariant()
            };
        }

        public string GetResizeDisplayName(ImageResizePreset preset)
        {
            return $"长边 {GetResizeLongEdge(preset)}";
        }

        public string GetCropDisplayName(ImageCropPreset preset)
        {
            return preset switch
            {
                ImageCropPreset.Square => "1:1 中心裁剪",
                ImageCropPreset.Ratio4x3 => "4:3 中心裁剪",
                ImageCropPreset.Ratio16x9 => "16:9 中心裁剪",
                _ => "中心裁剪"
            };
        }

        public string GetCompressionDisplayName(ImageCompressionPreset preset)
        {
            return preset switch
            {
                ImageCompressionPreset.Light => "轻度压缩",
                ImageCompressionPreset.Balanced => "均衡压缩",
                ImageCompressionPreset.Aggressive => "高压缩",
                _ => "压缩导出"
            };
        }

        public string GetEnhancementDisplayName(ImageEnhancementPreset preset)
        {
            return preset switch
            {
                ImageEnhancementPreset.SmartFix => "智能增强",
                ImageEnhancementPreset.DetailBoost => "清晰增强",
                ImageEnhancementPreset.LowLight => "低光优化",
                _ => "图片增强"
            };
        }

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

        public uint GetResizeLongEdge(ImageResizePreset preset)
        {
            return preset switch
            {
                ImageResizePreset.LongEdge1280 => 1280,
                ImageResizePreset.LongEdge1920 => 1920,
                ImageResizePreset.LongEdge2560 => 2560,
                _ => 1280
            };
        }

        public double GetCompressionQuality(ImageCompressionPreset preset)
        {
            return preset switch
            {
                ImageCompressionPreset.Light => 0.85d,
                ImageCompressionPreset.Balanced => 0.72d,
                ImageCompressionPreset.Aggressive => 0.55d,
                _ => 0.72d
            };
        }

        public async Task<ImageProcessResult> ConvertAsync(string sourcePath, ImageConversionTarget target, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            string targetExtension = GetTargetExtension(target);
            if (string.Equals(Path.GetExtension(sourcePath), targetExtension, StringComparison.OrdinalIgnoreCase))
            {
                return ImageProcessResult.Failed(sourcePath, "源文件已经是目标格式。");
            }

            string outputPath = BuildOutputPath(sourcePath, string.Empty, targetExtension);
            return await TranscodeAsync(
                sourcePath,
                outputPath,
                GetEncoderId(target),
                transform: null,
                quality: target == ImageConversionTarget.Jpeg ? 0.92d : null,
                successMessage: $"已转换为 {GetTargetDisplayName(target)}。",
                cancellationToken);
        }

        public async Task<ImageProcessResult> ResizeAsync(string sourcePath, ImageResizePreset preset, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            try
            {
                using var sourceStream = await OpenReadStreamAsync(sourcePath);
                var decoder = await BitmapDecoder.CreateAsync(sourceStream);
                var (targetWidth, targetHeight) = CalculateResizeDimensions(decoder.PixelWidth, decoder.PixelHeight, GetResizeLongEdge(preset));
                var transform = new BitmapTransform
                {
                    ScaledWidth = targetWidth,
                    ScaledHeight = targetHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };

                var (encoderId, extension, displayName) = ResolvePreferredEditableFormat(sourcePath);
                string outputPath = BuildOutputPath(sourcePath, $"_resize_{GetResizeLongEdge(preset)}", extension);
                return await TranscodeAsync(
                    sourcePath,
                    outputPath,
                    encoderId,
                    transform,
                    quality: encoderId == BitmapEncoder.JpegEncoderId ? 0.9d : null,
                    successMessage: $"已调整尺寸为 {targetWidth}x{targetHeight}（{displayName}）。",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        public async Task<ImageProcessResult> CropAsync(string sourcePath, ImageCropPreset preset, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            try
            {
                using var sourceStream = await OpenReadStreamAsync(sourcePath);
                var decoder = await BitmapDecoder.CreateAsync(sourceStream);
                var cropFrame = CalculateCenteredCropFrame(decoder.PixelWidth, decoder.PixelHeight, CropRatioMap[preset]);
                var transform = new BitmapTransform
                {
                    Bounds = new BitmapBounds
                    {
                        X = cropFrame.X,
                        Y = cropFrame.Y,
                        Width = cropFrame.Width,
                        Height = cropFrame.Height
                    }
                };

                var (encoderId, extension, displayName) = ResolvePreferredEditableFormat(sourcePath);
                string outputPath = BuildOutputPath(sourcePath, "_crop", extension);
                return await TranscodeAsync(
                    sourcePath,
                    outputPath,
                    encoderId,
                    transform,
                    quality: encoderId == BitmapEncoder.JpegEncoderId ? 0.92d : null,
                    successMessage: $"已完成 {GetCropDisplayName(preset)}（{displayName}）。",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        public async Task<ImageProcessResult> CompressAsync(string sourcePath, ImageCompressionPreset preset, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            string outputPath = BuildOutputPath(sourcePath, "_compressed", ".jpg");
            return await TranscodeAsync(
                sourcePath,
                outputPath,
                BitmapEncoder.JpegEncoderId,
                transform: null,
                quality: GetCompressionQuality(preset),
                successMessage: $"已按 {GetCompressionDisplayName(preset)} 导出 JPEG。",
                cancellationToken);
        }

        public async Task<ImageProcessResult> EnhanceAsync(string sourcePath, ImageEnhancementPreset preset, CancellationToken cancellationToken = default)
        {
            if (!ValidateSourcePath(sourcePath, out var validationResult))
            {
                return validationResult!;
            }

            try
            {
                using var sourceStream = await OpenReadStreamAsync(sourcePath);
                var decoder = await BitmapDecoder.CreateAsync(sourceStream);
                var settings = GetEnhancementSettings(preset);
                var (targetWidth, targetHeight) = CalculateEnhancedDimensions(
                    decoder.PixelWidth,
                    decoder.PixelHeight,
                    settings.ScaleFactor);

                var transform = new BitmapTransform
                {
                    ScaledWidth = targetWidth,
                    ScaledHeight = targetHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };

                var pixelProvider = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                byte[] pixels = pixelProvider.DetachPixelData();
                byte[] enhancedPixels = EnhancePixels(pixels, targetWidth, targetHeight, settings);

                var (encoderId, extension, displayName) = ResolvePreferredEditableFormat(sourcePath);
                string outputPath = BuildOutputPath(sourcePath, "_enhanced", extension);
                string successMessage = settings.ScaleFactor > 1d
                    ? $"已完成 {GetEnhancementDisplayName(preset)}，输出 {targetWidth}x{targetHeight}（{displayName}）。"
                    : $"已完成 {GetEnhancementDisplayName(preset)}（{displayName}）。";

                return await EncodePixelBufferAsync(
                    sourcePath,
                    outputPath,
                    enhancedPixels,
                    targetWidth,
                    targetHeight,
                    decoder.DpiX,
                    decoder.DpiY,
                    encoderId,
                    successMessage,
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

        public static (uint Width, uint Height) CalculateEnhancedDimensions(uint sourceWidth, uint sourceHeight, double scaleFactor)
        {
            if (sourceWidth == 0 || sourceHeight == 0 || scaleFactor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scaleFactor), "图片尺寸和增强倍率必须有效。");
            }

            if (Math.Abs(scaleFactor - 1d) < 0.001d)
            {
                return (sourceWidth, sourceHeight);
            }

            uint width = Math.Max(1u, (uint)Math.Round(sourceWidth * scaleFactor, MidpointRounding.AwayFromZero));
            uint height = Math.Max(1u, (uint)Math.Round(sourceHeight * scaleFactor, MidpointRounding.AwayFromZero));
            return (width, height);
        }

        public static (uint Width, uint Height) CalculateResizeDimensions(uint sourceWidth, uint sourceHeight, uint longEdge)
        {
            if (sourceWidth == 0 || sourceHeight == 0 || longEdge == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(longEdge), "图片尺寸和目标长边必须大于 0。");
            }

            if (sourceWidth >= sourceHeight)
            {
                double scale = longEdge / (double)sourceWidth;
                return (longEdge, Math.Max(1u, (uint)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero)));
            }

            double portraitScale = longEdge / (double)sourceHeight;
            return (Math.Max(1u, (uint)Math.Round(sourceWidth * portraitScale, MidpointRounding.AwayFromZero)), longEdge);
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

        private static ImageEnhancementSettings GetEnhancementSettings(ImageEnhancementPreset preset)
        {
            return preset switch
            {
                ImageEnhancementPreset.SmartFix => new ImageEnhancementSettings(
                    ScaleFactor: 1d,
                    BrightnessOffset: 0.015d,
                    ContrastFactor: 1.08d,
                    SaturationFactor: 1.05d,
                    SharpenAmount: 0.18d),
                ImageEnhancementPreset.DetailBoost => new ImageEnhancementSettings(
                    ScaleFactor: 1.5d,
                    BrightnessOffset: 0.01d,
                    ContrastFactor: 1.12d,
                    SaturationFactor: 1.08d,
                    SharpenAmount: 0.30d),
                ImageEnhancementPreset.LowLight => new ImageEnhancementSettings(
                    ScaleFactor: 1d,
                    BrightnessOffset: 0.08d,
                    ContrastFactor: 1.05d,
                    SaturationFactor: 1.02d,
                    SharpenAmount: 0.14d),
                _ => new ImageEnhancementSettings(1d, 0d, 1d, 1d, 0d)
            };
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
                if (outputFile != null)
                {
                    await TryDeleteAsync(outputFile);
                }

                return ImageProcessResult.Failed(sourcePath, "图片处理已取消。");
            }
            catch (Exception ex)
            {
                if (outputFile != null)
                {
                    await TryDeleteAsync(outputFile);
                }

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
                if (outputFile != null)
                {
                    await TryDeleteAsync(outputFile);
                }

                return ImageProcessResult.Failed(sourcePath, "图片处理已取消。");
            }
            catch (Exception ex)
            {
                if (outputFile != null)
                {
                    await TryDeleteAsync(outputFile);
                }

                return ImageProcessResult.Failed(sourcePath, ex.Message);
            }
        }

        private static async Task TryDeleteAsync(StorageFile file)
        {
            try
            {
                await file.DeleteAsync();
            }
            catch
            {
            }
        }

        private readonly record struct ImageEnhancementSettings(
            double ScaleFactor,
            double BrightnessOffset,
            double ContrastFactor,
            double SaturationFactor,
            double SharpenAmount);
    }
}
