using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public enum AudioConversionTarget
    {
        Mp3,
        Wav,
        M4a
    }

    public class AudioConversionService
    {
        public bool TryParseTarget(string? targetKey, out AudioConversionTarget target)
        {
            return Enum.TryParse(targetKey, ignoreCase: true, out target);
        }

        public bool CanConvertToTarget(string? fileName, AudioConversionTarget target)
        {
            return GetSourceKind(fileName) != AudioSourceKind.Unsupported &&
                   GetTargetExtension(target).Length > 0;
        }

        public bool CanTrim(string? fileName)
        {
            return GetTrimOutputExtension(fileName).Length > 0;
        }

        public string GetTargetDisplayName(AudioConversionTarget target)
        {
            return target switch
            {
                AudioConversionTarget.Mp3 => "MP3",
                AudioConversionTarget.Wav => "WAV",
                AudioConversionTarget.M4a => "M4A",
                _ => target.ToString().ToUpperInvariant()
            };
        }

        public string GetTargetExtension(AudioConversionTarget target)
        {
            return target switch
            {
                AudioConversionTarget.Mp3 => ".mp3",
                AudioConversionTarget.Wav => ".wav",
                AudioConversionTarget.M4a => ".m4a",
                _ => string.Empty
            };
        }

        public string GetTrimOutputExtension(string? fileName)
        {
            return GetTrimProfile(fileName).Extension;
        }

        public async Task<AudioConversionResult> ConvertAsync(
            string sourcePath,
            AudioConversionTarget target,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return AudioConversionResult.Failed(sourcePath, "源文件不存在。");
            }

            string targetExtension = GetTargetExtension(target);
            if (string.Equals(Path.GetExtension(sourcePath), targetExtension, StringComparison.OrdinalIgnoreCase))
            {
                return AudioConversionResult.Failed(sourcePath, "源文件已经是目标格式。");
            }

            if (GetSourceKind(sourcePath) == AudioSourceKind.Unsupported)
            {
                return AudioConversionResult.Failed(sourcePath, "当前文件不是可转换的音频格式。");
            }

            string outputPath = BuildOutputPath(sourcePath, string.Empty, targetExtension);
            var profile = CreateProfile(target);

            return await RunTranscodeAsync(
                sourcePath,
                outputPath,
                profile,
                trimRequest: null,
                successMessage: "转换成功。",
                cancellationToken);
        }

        public async Task<AudioConversionResult> TrimAsync(
            string sourcePath,
            AudioTrimRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return AudioConversionResult.Failed(sourcePath, "源文件不存在。");
            }

            if (!CanTrim(sourcePath))
            {
                return AudioConversionResult.Failed(sourcePath, "当前音频格式暂不支持直接裁剪。");
            }

            string trimExtension = GetTrimOutputExtension(sourcePath);
            string outputPath = BuildOutputPath(sourcePath, "_trim", trimExtension);
            var trimProfile = GetTrimProfile(sourcePath);

            if (trimProfile.Profile == null)
            {
                return AudioConversionResult.Failed(sourcePath, "当前音频格式暂不支持直接裁剪。");
            }

            StorageFile? sourceFile = null;
            try
            {
                sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
                var musicProperties = await sourceFile.Properties.GetMusicPropertiesAsync();
                if (musicProperties.Duration > TimeSpan.Zero && request.EndTime > musicProperties.Duration)
                {
                    return AudioConversionResult.Failed(
                        sourcePath,
                        $"结束时间超出音频时长（{FormatDuration(musicProperties.Duration)}）。");
                }
            }
            catch
            {
                // 读取时长失败时保持裁剪流程，由系统转码阶段继续校验。
            }

            string successMessage = trimProfile.Extension.Equals(Path.GetExtension(sourcePath), StringComparison.OrdinalIgnoreCase)
                ? $"裁剪成功，范围：{request.RangeText}。"
                : $"裁剪成功，已导出为 {trimProfile.DisplayName}，范围：{request.RangeText}。";

            return await RunTranscodeAsync(
                sourcePath,
                outputPath,
                trimProfile.Profile,
                request,
                successMessage,
                cancellationToken);
        }

        private static MediaEncodingProfile CreateProfile(AudioConversionTarget target)
        {
            return target switch
            {
                AudioConversionTarget.Mp3 => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
                AudioConversionTarget.Wav => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
                AudioConversionTarget.M4a => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
                _ => throw new InvalidOperationException("未知的音频目标格式。")
            };
        }

        private static (string Extension, string DisplayName, MediaEncodingProfile? Profile) GetTrimProfile(string? fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

            return extension switch
            {
                ".mp3" => (".mp3", "MP3", MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High)),
                ".wav" => (".wav", "WAV", MediaEncodingProfile.CreateWav(AudioEncodingQuality.High)),
                ".m4a" => (".m4a", "M4A", MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High)),
                ".aac" => (".m4a", "M4A", MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High)),
                ".flac" => (".wav", "WAV", MediaEncodingProfile.CreateWav(AudioEncodingQuality.High)),
                _ => (string.Empty, string.Empty, null)
            };
        }

        private static async Task<AudioConversionResult> RunTranscodeAsync(
            string sourcePath,
            string outputPath,
            MediaEncodingProfile profile,
            AudioTrimRequest? trimRequest,
            string successMessage,
            CancellationToken cancellationToken)
        {
            StorageFile? destinationFile = null;

            try
            {
                var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
                destinationFile = await folder.CreateFileAsync(
                    Path.GetFileName(outputPath),
                    CreationCollisionOption.FailIfExists);

                var transcoder = new MediaTranscoder
                {
                    AlwaysReencode = true
                };

                if (trimRequest != null)
                {
                    transcoder.TrimStartTime = trimRequest.StartTime;
                    transcoder.TrimStopTime = trimRequest.EndTime;
                }

                var prepared = await transcoder.PrepareFileTranscodeAsync(sourceFile, destinationFile, profile);

                if (!prepared.CanTranscode)
                {
                    await TryDeleteAsync(destinationFile);
                    return AudioConversionResult.Failed(sourcePath, $"系统不支持此音频处理：{prepared.FailureReason}");
                }

                await prepared.TranscodeAsync().AsTask(cancellationToken);
                return AudioConversionResult.Succeeded(sourcePath, outputPath, successMessage);
            }
            catch (OperationCanceledException)
            {
                if (destinationFile != null)
                {
                    await TryDeleteAsync(destinationFile);
                }

                return AudioConversionResult.Failed(sourcePath, "处理已取消。");
            }
            catch (Exception ex)
            {
                if (destinationFile != null)
                {
                    await TryDeleteAsync(destinationFile);
                }

                return AudioConversionResult.Failed(sourcePath, ex.Message);
            }
        }

        private static string BuildOutputPath(string sourcePath, string suffix, string targetExtension)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(directory, $"{baseName}{suffix}{targetExtension}");

            if (!File.Exists(outputPath))
            {
                return outputPath;
            }

            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(directory, $"{baseName}{suffix}_{counter:D2}{targetExtension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }

                counter++;
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

        private static AudioSourceKind GetSourceKind(string? fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

            return extension switch
            {
                ".mp3" => AudioSourceKind.Supported,
                ".wav" => AudioSourceKind.Supported,
                ".aac" => AudioSourceKind.Supported,
                ".m4a" => AudioSourceKind.Supported,
                ".flac" => AudioSourceKind.Supported,
                _ => AudioSourceKind.Unsupported
            };
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss")
                : duration.ToString(@"mm\:ss");
        }

        private enum AudioSourceKind
        {
            Unsupported,
            Supported
        }
    }

    public sealed record AudioConversionResult(string SourcePath, string? OutputPath, bool Success, string Message)
    {
        public static AudioConversionResult Succeeded(string sourcePath, string outputPath, string message = "转换成功。")
        {
            return new AudioConversionResult(sourcePath, outputPath, true, message);
        }

        public static AudioConversionResult Failed(string sourcePath, string message)
        {
            return new AudioConversionResult(sourcePath, null, false, message);
        }
    }
}
