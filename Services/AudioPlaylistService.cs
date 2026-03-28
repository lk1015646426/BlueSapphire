using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed record AudioPlaylistEntry(string SourcePath, string DisplayName, TimeSpan Duration);

    public sealed record AudioPlaylistExportResult(string PlaylistPath, int EntryCount, bool UsesRelativePaths)
    {
        public string Message => UsesRelativePaths
            ? $"播放列表已导出，共 {EntryCount} 个条目（相对路径）。"
            : $"播放列表已导出，共 {EntryCount} 个条目。";
    }

    public class AudioPlaylistService
    {
        static AudioPlaylistService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public string BuildSuggestedName(string? folderPath, int itemCount)
        {
            string folderName = Path.GetFileName(folderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty);
            string baseName = string.IsNullOrWhiteSpace(folderName)
                ? $"playlist_{DateTime.Now:yyyyMMdd_HHmm}"
                : $"{folderName}_playlist_{itemCount}";

            return SanitizePlaylistName(baseName);
        }

        public string SanitizePlaylistName(string? playlistName, string fallbackName = "playlist")
        {
            string rawName = string.IsNullOrWhiteSpace(playlistName) ? fallbackName : playlistName.Trim();
            string withoutExtension = Path.GetFileNameWithoutExtension(rawName);
            string sanitized = string.Concat(withoutExtension.Select(ch =>
                Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

            sanitized = sanitized.Trim('.', ' ', '_');
            return string.IsNullOrWhiteSpace(sanitized) ? fallbackName : sanitized;
        }

        public string BuildOutputPath(string directoryPath, string? playlistName)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("必须提供有效的导出目录。", nameof(directoryPath));
            }

            Directory.CreateDirectory(directoryPath);

            string fileName = SanitizePlaylistName(playlistName) + ".m3u8";
            string outputPath = Path.Combine(directoryPath, fileName);
            if (!File.Exists(outputPath))
            {
                return outputPath;
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(directoryPath, $"{baseName}_{counter:D2}.m3u8");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }

                counter++;
            }
        }

        public async Task<AudioPlaylistExportResult> ExportAsync(
            IReadOnlyList<AudioPlaylistEntry> entries,
            string outputPath,
            bool preferRelativePaths = true,
            CancellationToken cancellationToken = default)
        {
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException("没有可导出的音频条目。");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("必须提供输出路径。", nameof(outputPath));
            }

            string playlistDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(playlistDirectory))
            {
                throw new InvalidOperationException("输出路径缺少有效目录。");
            }

            Directory.CreateDirectory(playlistDirectory);

            var lines = new List<string> { "#EXTM3U" };
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool usesRelativePaths = preferRelativePaths;
            int exportedCount = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(entry.SourcePath) || !seenPaths.Add(entry.SourcePath))
                {
                    continue;
                }

                string displayName = BuildDisplayName(entry);
                lines.Add($"#EXTINF:{FormatExtInfDuration(entry.Duration)},{displayName}");

                string pathLine = BuildPathLine(entry.SourcePath, playlistDirectory, preferRelativePaths);
                if (Path.IsPathRooted(pathLine))
                {
                    usesRelativePaths = false;
                }

                lines.Add(pathLine);
                exportedCount++;
            }

            if (exportedCount == 0)
            {
                throw new InvalidOperationException("没有可写入播放列表的有效音频条目。");
            }

            await File.WriteAllLinesAsync(
                outputPath,
                lines,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);

            return new AudioPlaylistExportResult(outputPath, exportedCount, usesRelativePaths);
        }

        public async Task<IReadOnlyList<string>> ParsePlaylistPathsAsync(
            string playlistPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playlistPath) || !File.Exists(playlistPath))
            {
                throw new FileNotFoundException("找不到播放列表文件。", playlistPath);
            }

            string[] lines = await ReadLinesWithFallbackAsync(playlistPath, cancellationToken);
            string playlistDirectory = Path.GetDirectoryName(playlistPath) ?? string.Empty;

            var resolvedPaths = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string resolvedPath = ResolveEntryPath(line, playlistDirectory);
                if (File.Exists(resolvedPath) && seenPaths.Add(resolvedPath))
                {
                    resolvedPaths.Add(resolvedPath);
                }
            }

            return resolvedPaths;
        }

        internal static string BuildPathLine(string sourcePath, string playlistDirectory, bool preferRelativePaths)
        {
            if (!preferRelativePaths)
            {
                return sourcePath;
            }

            try
            {
                string relativePath = Path.GetRelativePath(playlistDirectory, sourcePath);
                if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                {
                    return sourcePath;
                }

                return relativePath.Replace(Path.DirectorySeparatorChar, '/');
            }
            catch
            {
                return sourcePath;
            }
        }

        internal static string ResolveEntryPath(string pathLine, string playlistDirectory)
        {
            if (Uri.TryCreate(pathLine, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            {
                return uri.LocalPath;
            }

            string normalizedPath = pathLine.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedPath))
            {
                return Path.GetFullPath(normalizedPath);
            }

            return Path.GetFullPath(Path.Combine(playlistDirectory, normalizedPath));
        }

        private static string BuildDisplayName(AudioPlaylistEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                return entry.DisplayName.Trim();
            }

            return Path.GetFileNameWithoutExtension(entry.SourcePath);
        }

        private static int FormatExtInfDuration(TimeSpan duration)
        {
            return duration > TimeSpan.Zero
                ? (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero)
                : -1;
        }

        private static async Task<string[]> ReadLinesWithFallbackAsync(string filePath, CancellationToken cancellationToken)
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string content = DecodeText(bytes);
            return content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            if (HasUtf8Bom(bytes))
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    return Encoding.GetEncoding("GB18030").GetString(bytes);
                }
                catch
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 &&
                   bytes[0] == 0xEF &&
                   bytes[1] == 0xBB &&
                   bytes[2] == 0xBF;
        }
    }
}
