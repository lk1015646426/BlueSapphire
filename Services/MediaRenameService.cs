using BlueSapphire.Helpers;
using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public enum AudioRenamePattern
    {
        Title,
        ArtistTitle,
        TrackTitle,
        AlbumTrackTitle
    }

    public class MediaRenameService
    {
        private static readonly Regex FullTimeSeparatedPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})[-_.\s年](?<month>0?[1-9]|1[0-2])[-_.\s月](?<day>0?[1-9]|[12]\d|3[01])[日号]?[-_.\sT]+(?<hour>[01]\d|2[0-3])[-_:.]?(?<minute>[0-5]\d)(?:[-_:.]?(?<second>[0-5]\d))?(?!\d)", RegexOptions.Compiled);
        private static readonly Regex FullTimeCompactPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12]\d|3[01])[-_.\sT]?(?<hour>[01]\d|2[0-3])(?<minute>[0-5]\d)(?<second>[0-5]\d)?(?!\d)", RegexOptions.Compiled);
        private static readonly Regex DateOnlySeparatedPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})[-_.\s年](?<month>0?[1-9]|1[0-2])[-_.\s月](?<day>0?[1-9]|[12]\d|3[01])[日号]?(?!\d)", RegexOptions.Compiled);
        private static readonly Regex DateOnlyCompactPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.Compiled);
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public bool HasUsableTimestamp(DateTimeOffset value)
        {
            return value != DateTimeOffset.MinValue && value.Year >= 1900;
        }

        public async Task<DateTimeOffset> ResolveBestTimestampAsync(StorageFile file)
        {
            var metadataTimestamp = await TryGetMetadataTimestampAsync(file);
            if (HasUsableTimestamp(metadataTimestamp))
            {
                return metadataTimestamp;
            }

            var parsedTimestamp = await SmartParseDateAsync(file);
            if (HasUsableTimestamp(parsedTimestamp))
            {
                return parsedTimestamp;
            }

            return file.DateCreated;
        }

        /// <summary>
        /// 智能解析文件时间（支持正则提取 1900-2099 年份）
        /// </summary>
        public Task<DateTimeOffset> SmartParseDateAsync(StorageFile file)
        {
            return Task.FromResult(ParseTimestampFromFileName(file.Name));
        }

        public DateTimeOffset ParseTimestampFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return DateTimeOffset.MinValue;
            }

            foreach (var pattern in new[] { FullTimeSeparatedPattern, FullTimeCompactPattern })
            {
                var match = pattern.Match(fileName);
                if (match.Success)
                {
                    return ParseRegexMatch(match);
                }
            }

            foreach (var pattern in new[] { DateOnlySeparatedPattern, DateOnlyCompactPattern })
            {
                var match = pattern.Match(fileName);
                if (match.Success)
                {
                    return ParseRegexMatch(match).Date;
                }
            }

            return DateTimeOffset.MinValue;
        }

        public bool TryParseAudioRenamePattern(string? patternKey, out AudioRenamePattern pattern)
        {
            return Enum.TryParse(patternKey, ignoreCase: true, out pattern);
        }

        public string GetAudioRenamePatternDisplayName(AudioRenamePattern pattern)
        {
            return pattern switch
            {
                AudioRenamePattern.Title => "标题",
                AudioRenamePattern.ArtistTitle => "艺术家 - 标题",
                AudioRenamePattern.TrackTitle => "曲序 - 标题",
                AudioRenamePattern.AlbumTrackTitle => "专辑 - 曲序 - 标题",
                _ => pattern.ToString()
            };
        }

        public bool TryBuildAudioMetadataBaseName(
            AudioMetadataInfo metadata,
            AudioRenamePattern pattern,
            out string baseName)
        {
            string title = NormalizeFileNameSegment(metadata.Title);
            if (string.IsNullOrWhiteSpace(title))
            {
                baseName = string.Empty;
                return false;
            }

            string artist = NormalizeFileNameSegment(metadata.Artist ?? metadata.AlbumArtist);
            string album = NormalizeFileNameSegment(metadata.Album);
            string track = metadata.TrackNumber > 0 ? metadata.TrackNumber.ToString("D2") : string.Empty;

            baseName = pattern switch
            {
                AudioRenamePattern.Title => title,
                AudioRenamePattern.ArtistTitle => JoinSegments(artist, title),
                AudioRenamePattern.TrackTitle => JoinSegments(track, title),
                AudioRenamePattern.AlbumTrackTitle => JoinSegments(album, track, title),
                _ => title
            };

            baseName = NormalizeFileNameSegment(baseName);
            return !string.IsNullOrWhiteSpace(baseName);
        }

        public bool TryBuildAudioTagRequestFromFileName(
            string? fileName,
            AudioRenamePattern pattern,
            out AudioTagEditRequest request)
        {
            request = new AudioTagEditRequest(
                ApplyTitle: false,
                Title: null,
                ApplyArtist: false,
                Artist: null,
                ApplyAlbum: false,
                Album: null,
                ApplyTrackNumber: false,
                TrackNumber: null,
                ApplyYear: false,
                Year: null);

            string baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            string normalizedBaseName = NormalizeMetadataSegment(baseName);
            if (string.IsNullOrWhiteSpace(normalizedBaseName))
            {
                return false;
            }

            string[] segments = SplitAudioFileNameSegments(normalizedBaseName);
            switch (pattern)
            {
                case AudioRenamePattern.Title:
                    request = request with
                    {
                        ApplyTitle = true,
                        Title = normalizedBaseName
                    };
                    return true;
                case AudioRenamePattern.ArtistTitle:
                    if (segments.Length < 2)
                    {
                        return false;
                    }

                    string artist = NormalizeMetadataSegment(segments[0]);
                    string artistTitle = JoinMetadataSegments(segments.Skip(1));
                    if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(artistTitle))
                    {
                        return false;
                    }

                    request = request with
                    {
                        ApplyTitle = true,
                        Title = artistTitle,
                        ApplyArtist = true,
                        Artist = artist
                    };
                    return true;
                case AudioRenamePattern.TrackTitle:
                    if (segments.Length < 2 || !TryParseTrackNumberSegment(segments[0], out uint trackNumber))
                    {
                        return false;
                    }

                    string trackTitle = JoinMetadataSegments(segments.Skip(1));
                    if (string.IsNullOrWhiteSpace(trackTitle))
                    {
                        return false;
                    }

                    request = request with
                    {
                        ApplyTitle = true,
                        Title = trackTitle,
                        ApplyTrackNumber = true,
                        TrackNumber = trackNumber
                    };
                    return true;
                case AudioRenamePattern.AlbumTrackTitle:
                    if (segments.Length < 3 || !TryParseTrackNumberSegment(segments[1], out uint albumTrackNumber))
                    {
                        return false;
                    }

                    string album = NormalizeMetadataSegment(segments[0]);
                    string albumTrackTitle = JoinMetadataSegments(segments.Skip(2));
                    if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(albumTrackTitle))
                    {
                        return false;
                    }

                    request = request with
                    {
                        ApplyTitle = true,
                        Title = albumTrackTitle,
                        ApplyAlbum = true,
                        Album = album,
                        ApplyTrackNumber = true,
                        TrackNumber = albumTrackNumber
                    };
                    return true;
                default:
                    return false;
            }
        }

        private async Task<DateTimeOffset> TryGetMetadataTimestampAsync(StorageFile file)
        {
            if (!MediaFileCatalog.IsImage(file.Name))
            {
                return DateTimeOffset.MinValue;
            }

            try
            {
                var imageProperties = await file.Properties.GetImagePropertiesAsync();
                return imageProperties.DateTaken;
            }
            catch
            {
                return DateTimeOffset.MinValue;
            }
        }

        private DateTime ParseRegexMatch(Match match)
        {
            try
            {
                int year = int.Parse(match.Groups["year"].Value);
                int month = int.Parse(match.Groups["month"].Value);
                int day = int.Parse(match.Groups["day"].Value);
                int hour = 0;
                int minute = 0;
                int second = 0;

                if (match.Groups["hour"].Success)
                {
                    hour = int.Parse(match.Groups["hour"].Value);
                }

                if (match.Groups["minute"].Success)
                {
                    minute = int.Parse(match.Groups["minute"].Value);
                }

                if (match.Groups["second"].Success)
                {
                    second = int.Parse(match.Groups["second"].Value);
                }

                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
        }

        private static string JoinSegments(params string[] segments)
        {
            return string.Join(" - ", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string[] SplitAudioFileNameSegments(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return Array.Empty<string>();
            }

            return Regex.Split(baseName, @"\s*-\s*")
                .Select(NormalizeMetadataSegment)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray()!;
        }

        private static string JoinMetadataSegments(IEnumerable<string> segments)
        {
            return string.Join(" - ", segments
                .Select(NormalizeMetadataSegment)
                .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string NormalizeMetadataSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace('_', ' ');
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized.Trim('-', '.', ' ');
        }

        private static bool TryParseTrackNumberSegment(string? value, out uint trackNumber)
        {
            trackNumber = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(value, @"\d+");
            return match.Success && uint.TryParse(match.Value, out trackNumber) && trackNumber > 0;
        }

        private static string NormalizeFileNameSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string sanitized = string.Join(" ", value.Split(InvalidFileNameChars, StringSplitOptions.RemoveEmptyEntries));
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            return sanitized.Trim('.', ' ');
        }
    }
}
