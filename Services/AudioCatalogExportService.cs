using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed record AudioCatalogExportEntry(
        string FileName,
        string? Title,
        string? Artist,
        string? Album,
        string? AlbumArtist,
        string? Composer,
        string? Genre,
        uint TrackNumber,
        uint DiscNumber,
        uint Year,
        TimeSpan Duration,
        uint Bitrate,
        uint SampleRate,
        bool HasCoverArt,
        string Path);

    public sealed record AudioCatalogExportResult(string OutputPath, int EntryCount)
    {
        public string Message => $"音频清单已导出，共 {EntryCount} 条记录。";
    }

    public sealed record AudioCatalogImportRow(
        string FileName,
        string Path,
        bool HasTitle,
        string? Title,
        bool HasArtist,
        string? Artist,
        bool HasAlbum,
        string? Album,
        bool HasAlbumArtist,
        string? AlbumArtist,
        bool HasComposer,
        string? Composer,
        bool HasGenre,
        string? Genre,
        bool HasTrackNumber,
        uint? TrackNumber,
        bool HasDiscNumber,
        uint? DiscNumber,
        bool HasYear,
        uint? Year);

    public class AudioCatalogExportService
    {
        private const string HeaderLine = "FileName,Title,Artist,Album,AlbumArtist,Composer,Genre,Track,Disc,Year,Duration,Bitrate,SampleRate,HasCoverArt,Path";

        public string BuildSuggestedName(string? folderPath, int itemCount)
        {
            string folderName = Path.GetFileName(folderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty);
            string baseName = string.IsNullOrWhiteSpace(folderName)
                ? $"audio_catalog_{DateTime.Now:yyyyMMdd_HHmm}"
                : $"{folderName}_audio_catalog_{itemCount}";

            return SanitizeBaseName(baseName);
        }

        public string BuildOutputPath(string directoryPath, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("必须提供有效的导出目录。", nameof(directoryPath));
            }

            Directory.CreateDirectory(directoryPath);

            string baseName = SanitizeBaseName(fileName);
            string outputPath = Path.Combine(directoryPath, baseName + ".csv");
            if (!File.Exists(outputPath))
            {
                return outputPath;
            }

            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(directoryPath, $"{baseName}_{counter:D2}.csv");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }

                counter++;
            }
        }

        public async Task<AudioCatalogExportResult> ExportAsync(
            IReadOnlyList<AudioCatalogExportEntry> entries,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException("没有可导出的音频记录。");
            }

            string directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("导出路径缺少有效目录。");
            }

            Directory.CreateDirectory(directory);

            var builder = new StringBuilder();
            builder.AppendLine(HeaderLine);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.AppendLine(string.Join(",",
                    Escape(entry.FileName),
                    Escape(entry.Title),
                    Escape(entry.Artist),
                    Escape(entry.Album),
                    Escape(entry.AlbumArtist),
                    Escape(entry.Composer),
                    Escape(entry.Genre),
                    entry.TrackNumber.ToString(),
                    entry.DiscNumber.ToString(),
                    entry.Year.ToString(),
                    Escape(FormatDuration(entry.Duration)),
                    entry.Bitrate.ToString(),
                    entry.SampleRate.ToString(),
                    entry.HasCoverArt ? "true" : "false",
                    Escape(entry.Path)));
            }

            await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
            return new AudioCatalogExportResult(outputPath, entries.Count);
        }

        public async Task<IReadOnlyList<AudioCatalogImportRow>> ParseImportRowsAsync(string csvPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                throw new FileNotFoundException("找不到音频清单文件。", csvPath);
            }

            string[] lines = await File.ReadAllLinesAsync(csvPath, cancellationToken);
            if (lines.Length <= 1)
            {
                return Array.Empty<AudioCatalogImportRow>();
            }

            var headerValues = ParseCsvLine(lines[0]);
            var headerIndex = headerValues
                .Select((value, index) => new { Value = value.Trim(), Index = index })
                .ToDictionary(entry => entry.Value, entry => entry.Index, StringComparer.OrdinalIgnoreCase);

            var rows = new List<AudioCatalogImportRow>();
            for (int i = 1; i < lines.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                List<string> values = ParseCsvLine(lines[i]);
                string fileName = GetFieldValue(values, headerIndex, "FileName") ?? string.Empty;
                string path = GetFieldValue(values, headerIndex, "Path") ?? string.Empty;

                rows.Add(new AudioCatalogImportRow(
                    fileName,
                    path,
                    headerIndex.ContainsKey("Title"),
                    GetFieldValue(values, headerIndex, "Title"),
                    headerIndex.ContainsKey("Artist"),
                    GetFieldValue(values, headerIndex, "Artist"),
                    headerIndex.ContainsKey("Album"),
                    GetFieldValue(values, headerIndex, "Album"),
                    headerIndex.ContainsKey("AlbumArtist"),
                    GetFieldValue(values, headerIndex, "AlbumArtist"),
                    headerIndex.ContainsKey("Composer"),
                    GetFieldValue(values, headerIndex, "Composer"),
                    headerIndex.ContainsKey("Genre"),
                    GetFieldValue(values, headerIndex, "Genre"),
                    headerIndex.ContainsKey("Track"),
                    TryParseOptionalUInt(GetFieldValue(values, headerIndex, "Track")),
                    headerIndex.ContainsKey("Disc"),
                    TryParseOptionalUInt(GetFieldValue(values, headerIndex, "Disc")),
                    headerIndex.ContainsKey("Year"),
                    TryParseOptionalUInt(GetFieldValue(values, headerIndex, "Year"))));
            }

            return rows;
        }

        private static string SanitizeBaseName(string? value)
        {
            string raw = string.IsNullOrWhiteSpace(value) ? "audio_catalog" : Path.GetFileNameWithoutExtension(value.Trim());
            string sanitized = string.Concat(raw.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            sanitized = sanitized.Trim('.', ' ', '_');
            return string.IsNullOrWhiteSpace(sanitized) ? "audio_catalog" : sanitized;
        }

        private static string Escape(string? value)
        {
            string text = value ?? string.Empty;
            if (text.Contains('"'))
            {
                text = text.Replace("\"", "\"\"");
            }

            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? $"\"{text}\""
                : text;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return string.Empty;
            }

            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss")
                : duration.ToString(@"mm\:ss");
        }

        private static string? GetFieldValue(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string fieldName)
        {
            if (!headerIndex.TryGetValue(fieldName, out int index) || index >= values.Count)
            {
                return null;
            }

            return values[index];
        }

        private static uint? TryParseOptionalUInt(string? value)
        {
            return uint.TryParse(value?.Trim(), out uint parsed) ? parsed : null;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var builder = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    values.Add(builder.ToString());
                    builder.Clear();
                    continue;
                }

                builder.Append(ch);
            }

            values.Add(builder.ToString());
            return values;
        }
    }
}
