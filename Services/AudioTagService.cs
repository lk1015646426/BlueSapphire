using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BlueSapphire.Models;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public class AudioTagService
    {
        private static readonly string[] SidecarCoverExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
        private static readonly string[] SidecarCoverBaseNames = ["cover", "folder", "front", "album"];
        private static readonly string[] SidecarLyricsExtensions = [".lrc", ".txt"];

        public const string TitlePropertyKey = "System.Title";
        public const string ArtistPropertyKey = "System.Music.Artist";
        public const string AlbumPropertyKey = "System.Music.AlbumTitle";
        public const string TrackNumberPropertyKey = "System.Music.TrackNumber";
        public const string YearPropertyKey = "System.Media.Year";
        public const string AlbumArtistPropertyKey = "System.Music.AlbumArtist";
        public const string ComposerPropertyKey = "System.Music.Composer";
        public const string GenrePropertyKey = "System.Music.Genre";
        public const string DiscNumberPropertyKey = "System.Music.DiscNumber";
        public const string CommentPropertyKey = "System.Comment";
        public const string LyricsPropertyKey = "System.Music.Lyrics";

        static AudioTagService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public IReadOnlyDictionary<string, object> BuildPropertyMap(AudioTagEditRequest request)
        {
            var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (request.ApplyTitle)
            {
                properties[TitlePropertyKey] = NormalizeText(request.Title);
            }

            if (request.ApplyArtist)
            {
                properties[ArtistPropertyKey] = BuildStringArray(request.Artist);
            }

            if (request.ApplyAlbum)
            {
                properties[AlbumPropertyKey] = NormalizeText(request.Album);
            }

            if (request.ApplyTrackNumber)
            {
                properties[TrackNumberPropertyKey] = request.TrackNumber ?? 0u;
            }

            if (request.ApplyYear)
            {
                properties[YearPropertyKey] = request.Year ?? 0u;
            }

            if (request.ApplyAlbumArtist)
            {
                properties[AlbumArtistPropertyKey] = BuildStringArray(request.AlbumArtist);
            }

            if (request.ApplyComposer)
            {
                properties[ComposerPropertyKey] = BuildStringArray(request.Composer);
            }

            if (request.ApplyGenre)
            {
                properties[GenrePropertyKey] = BuildStringArray(request.Genre);
            }

            if (request.ApplyDiscNumber)
            {
                properties[DiscNumberPropertyKey] = request.DiscNumber ?? 0u;
            }

            if (request.ApplyComment)
            {
                properties[CommentPropertyKey] = NormalizeText(request.Comment);
            }

            if (request.ApplyLyrics)
            {
                properties[LyricsPropertyKey] = NormalizeText(request.Lyrics);
            }

            return properties;
        }

        public async Task<AudioTagUpdateResult> UpdateAsync(StorageFile file, AudioTagEditRequest request)
        {
            if (!request.HasChanges)
            {
                return AudioTagUpdateResult.Failed(file.Path, "未指定要更新的标签字段。");
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(file.Path);
                    ApplyRequest(tagFile.Tag, request);
                    tagFile.Save();
                    return AudioTagUpdateResult.Succeeded(file.Path);
                }
                catch (Exception ex)
                {
                    return AudioTagUpdateResult.Failed(file.Path, ex.Message);
                }
            });
        }

        public async Task<AudioTagUpdateResult> UpdateCoverArtAsync(StorageFile audioFile, StorageFile imageFile)
        {
            if (!File.Exists(audioFile.Path) || !File.Exists(imageFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件或封面图片不存在。");
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(audioFile.Path);
                    var picture = new TagLib.Picture(imageFile.Path)
                    {
                        Type = TagLib.PictureType.FrontCover,
                        Description = "Front Cover"
                    };

                    var preservedPictures = tagFile.Tag.Pictures?
                        .Where(existing => existing != null && existing.Type != TagLib.PictureType.FrontCover)
                        .ToList()
                        ?? new List<TagLib.IPicture>();
                    preservedPictures.Insert(0, picture);
                    tagFile.Tag.Pictures = preservedPictures.ToArray();
                    tagFile.Save();

                    return AudioTagUpdateResult.Succeeded(audioFile.Path, "封面更新成功。");
                }
                catch (Exception ex)
                {
                    return AudioTagUpdateResult.Failed(audioFile.Path, ex.Message);
                }
            });
        }

        public async Task<AudioTagUpdateResult> ClearCoverArtAsync(StorageFile audioFile)
        {
            if (!File.Exists(audioFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件不存在。");
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(audioFile.Path);
                    tagFile.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                    tagFile.Save();
                    return AudioTagUpdateResult.Succeeded(audioFile.Path, "封面已清除。");
                }
                catch (Exception ex)
                {
                    return AudioTagUpdateResult.Failed(audioFile.Path, ex.Message);
                }
            });
        }

        public async Task<AudioTagUpdateResult> ImportSidecarCoverArtAsync(StorageFile audioFile)
        {
            if (!File.Exists(audioFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件不存在。");
            }

            string? sidecarPath = FindSidecarCoverArtPath(audioFile.Path);
            if (string.IsNullOrWhiteSpace(sidecarPath))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "未找到可用的同名或目录封面图片。");
            }

            var imageFile = await StorageFile.GetFileFromPathAsync(sidecarPath);
            var result = await UpdateCoverArtAsync(audioFile, imageFile);
            return result.Success
                ? result with { Message = $"已从 {Path.GetFileName(sidecarPath)} 导入封面。" }
                : result;
        }

        public async Task<AudioTagUpdateResult> ImportLyricsFromFileAsync(StorageFile audioFile, StorageFile lyricsFile)
        {
            if (!File.Exists(audioFile.Path) || !File.Exists(lyricsFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件或歌词文件不存在。");
            }

            try
            {
                string lyrics = await Task.Run(() => ReadTextFileWithFallback(lyricsFile.Path));
                return await ImportLyricsAsync(audioFile, lyrics, Path.GetFileName(lyricsFile.Path));
            }
            catch (Exception ex)
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, ex.Message);
            }
        }

        public async Task<AudioTagUpdateResult> ImportSidecarLyricsAsync(StorageFile audioFile)
        {
            if (!File.Exists(audioFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件不存在。");
            }

            string? sidecarPath = FindSidecarLyricsPath(audioFile.Path);
            if (string.IsNullOrWhiteSpace(sidecarPath))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "未找到同名歌词文件。");
            }

            var lyricsFile = await StorageFile.GetFileFromPathAsync(sidecarPath);
            return await ImportLyricsFromFileAsync(audioFile, lyricsFile);
        }

        public async Task<AudioTagUpdateResult> ExportCoverArtAsync(StorageFile audioFile)
        {
            if (!File.Exists(audioFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件不存在。");
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(audioFile.Path);
                    var picture = tagFile.Tag.Pictures?
                        .FirstOrDefault(existing => existing?.Data != null && existing.Data.Count > 0);

                    if (picture == null)
                    {
                        return AudioTagUpdateResult.Failed(audioFile.Path, "当前音频未嵌入封面。");
                    }

                    string extension = GetCoverFileExtension(picture.MimeType, picture.Filename);
                    string outputPath = BuildExportOutputPath(audioFile.Path, "_cover", extension);
                    File.WriteAllBytes(outputPath, picture.Data.Data);
                    return AudioTagUpdateResult.Succeeded(audioFile.Path, "封面导出成功。", outputPath);
                }
                catch (Exception ex)
                {
                    return AudioTagUpdateResult.Failed(audioFile.Path, ex.Message);
                }
            });
        }

        public async Task<AudioTagUpdateResult> ExportLyricsAsync(StorageFile audioFile)
        {
            if (!File.Exists(audioFile.Path))
            {
                return AudioTagUpdateResult.Failed(audioFile.Path, "音频文件不存在。");
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(audioFile.Path);
                    string lyrics = tagFile.Tag.Lyrics?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(lyrics))
                    {
                        return AudioTagUpdateResult.Failed(audioFile.Path, "当前音频未嵌入歌词。");
                    }

                    string outputPath = BuildExportOutputPath(audioFile.Path, "_lyrics", ".txt");
                    File.WriteAllText(outputPath, lyrics);
                    return AudioTagUpdateResult.Succeeded(audioFile.Path, "歌词导出成功。", outputPath);
                }
                catch (Exception ex)
                {
                    return AudioTagUpdateResult.Failed(audioFile.Path, ex.Message);
                }
            });
        }

        public string? FindSidecarCoverArtPath(string audioPath)
        {
            if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            {
                return null;
            }

            string? directory = Path.GetDirectoryName(audioPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            string baseName = Path.GetFileNameWithoutExtension(audioPath);
            foreach (string candidate in EnumerateSidecarCoverCandidates(directory, baseName))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public string? FindSidecarLyricsPath(string audioPath)
        {
            if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            {
                return null;
            }

            string? directory = Path.GetDirectoryName(audioPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            string baseName = Path.GetFileNameWithoutExtension(audioPath);
            foreach (string extension in SidecarLyricsExtensions)
            {
                string candidate = Path.Combine(directory, baseName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public string GetCoverFileExtension(string? mimeType, string? fileName)
        {
            string normalizedMimeType = mimeType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalizedMimeType.Contains("jpeg") || normalizedMimeType.Contains("jpg"))
            {
                return ".jpg";
            }

            if (normalizedMimeType.Contains("png"))
            {
                return ".png";
            }

            if (normalizedMimeType.Contains("bmp"))
            {
                return ".bmp";
            }

            if (normalizedMimeType.Contains("webp"))
            {
                return ".webp";
            }

            string extension = Path.GetExtension(fileName ?? string.Empty);
            return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension.ToLowerInvariant();
        }

        public string BuildExportOutputPath(string audioPath, string suffix, string extension)
        {
            string directory = Path.GetDirectoryName(audioPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(audioPath);
            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? ".dat"
                : extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();

            string outputPath = Path.Combine(directory, baseName + suffix + normalizedExtension);
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

        private async Task<AudioTagUpdateResult> ImportLyricsAsync(StorageFile audioFile, string? lyrics, string? sourceName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(audioFile.Path);
                    tagFile.Tag.Lyrics = NormalizeText(lyrics);
                    tagFile.Save();
                    return AudioTagUpdateResult.Succeeded(audioFile.Path, BuildLyricsImportMessage(lyrics, sourceName));
                }
                catch (Exception ex)
                {
                    return AudioTagUpdateResult.Failed(audioFile.Path, ex.Message);
                }
            });
        }

        private static void ApplyRequest(TagLib.Tag tag, AudioTagEditRequest request)
        {
            if (request.ApplyTitle)
            {
                tag.Title = NormalizeText(request.Title);
            }

            if (request.ApplyArtist)
            {
                tag.Performers = BuildStringArray(request.Artist);
            }

            if (request.ApplyAlbum)
            {
                tag.Album = NormalizeText(request.Album);
            }

            if (request.ApplyTrackNumber)
            {
                tag.Track = request.TrackNumber ?? 0u;
            }

            if (request.ApplyYear)
            {
                tag.Year = request.Year ?? 0u;
            }

            if (request.ApplyAlbumArtist)
            {
                tag.AlbumArtists = BuildStringArray(request.AlbumArtist);
            }

            if (request.ApplyComposer)
            {
                tag.Composers = BuildStringArray(request.Composer);
            }

            if (request.ApplyGenre)
            {
                tag.Genres = BuildStringArray(request.Genre);
            }

            if (request.ApplyDiscNumber)
            {
                tag.Disc = request.DiscNumber ?? 0u;
            }

            if (request.ApplyComment)
            {
                tag.Comment = NormalizeText(request.Comment);
            }

            if (request.ApplyLyrics)
            {
                tag.Lyrics = NormalizeText(request.Lyrics);
            }
        }

        private static string NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string[] BuildStringArray(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : new[] { value.Trim() };
        }

        private static IEnumerable<string> EnumerateSidecarCoverCandidates(string directory, string baseName)
        {
            foreach (string extension in SidecarCoverExtensions)
            {
                yield return Path.Combine(directory, baseName + extension);
            }

            foreach (string coverName in SidecarCoverBaseNames)
            {
                foreach (string extension in SidecarCoverExtensions)
                {
                    yield return Path.Combine(directory, coverName + extension);
                }
            }
        }

        private static string ReadTextFileWithFallback(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
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

        private static string BuildLyricsImportMessage(string? lyrics, string? sourceName)
        {
            if (string.IsNullOrWhiteSpace(lyrics))
            {
                return "歌词已清空。";
            }

            return string.IsNullOrWhiteSpace(sourceName)
                ? "歌词导入成功。"
                : $"已从 {sourceName} 导入歌词。";
        }
    }
}
