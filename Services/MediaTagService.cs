using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services
{
    public sealed record MediaTagUpdateResult(string SourcePath, bool Success, string Message)
    {
        public static MediaTagUpdateResult Succeeded(string sourcePath, string message)
        {
            return new MediaTagUpdateResult(sourcePath, true, message);
        }

        public static MediaTagUpdateResult Failed(string sourcePath, string message)
        {
            return new MediaTagUpdateResult(sourcePath, false, message);
        }
    }

    public sealed class MediaTagService
    {
        private const string FileName = "MediaTags.json";
        private const int MaxTagLength = 50;
        private const int MaxEntries = 50_000;
        private const long MaxStoreBytes = 10 * 1024 * 1024;
        private static readonly SemaphoreSlim FileLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly char[] TagSeparators = [',', '，', ';', '；', '|', '\r', '\n'];
        private readonly string? _dataDirectory;
        private Dictionary<string, List<string>>? _cachedStore;
        private readonly ILogger<MediaTagService> _logger;

        public MediaTagService(ILogger<MediaTagService> logger, string? dataDirectory = null)
        {
            _logger = logger;
            _dataDirectory = dataDirectory;
        }

        private string DataFilePath
        {
            get
            {
                string appFolder = string.IsNullOrWhiteSpace(_dataDirectory)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire")
                    : _dataDirectory;

                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, FileName);
            }
        }

        public IReadOnlyList<string> ParseTags(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return Array.Empty<string>();
            }

            return rawText
                .Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag[..Math.Min(tag.Length, MaxTagLength)])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetTagsAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Array.Empty<string>();
            }

            await FileLock.WaitAsync();
            try
            {
                var store = await GetStoreCoreAsync();
                return store.TryGetValue(filePath, out var tags)
                    ? tags.ToList()
                    : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaTags_Get");
                return Array.Empty<string>();
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task<MediaTagUpdateResult> ReplaceTagsAsync(string? filePath, IEnumerable<string>? tags)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return MediaTagUpdateResult.Failed(string.Empty, "文件路径无效。");
            }

            var normalizedTags = NormalizeTags(tags);

            await FileLock.WaitAsync();
            try
            {
                var store = await GetStoreCoreAsync();
                if (normalizedTags.Count == 0)
                {
                    store.Remove(filePath);
                    await SaveStoreCoreAsync();
                    return MediaTagUpdateResult.Succeeded(filePath, "已清空自定义标签。");
                }

                store[filePath] = normalizedTags;
                await SaveStoreCoreAsync();
                return MediaTagUpdateResult.Succeeded(filePath, $"已更新 {normalizedTags.Count} 个自定义标签。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaTags_Replace ({FilePath})", filePath);
                return MediaTagUpdateResult.Failed(filePath, ex.Message);
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task MoveTagsAsync(string? sourcePath, string? destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(destinationPath) ||
                string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await FileLock.WaitAsync();
            try
            {
                var store = await GetStoreCoreAsync();
                if (!store.Remove(sourcePath, out var sourceTags))
                {
                    return;
                }

                if (store.TryGetValue(destinationPath, out var existingTags))
                {
                    sourceTags = NormalizeTags(existingTags.Concat(sourceTags));
                }

                store[destinationPath] = sourceTags;
                await SaveStoreCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaTags_Move ({SourcePath} -> {DestinationPath})", sourcePath, destinationPath);
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task RemoveTagsAsync(IEnumerable<string>? filePaths)
        {
            var paths = filePaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths == null || paths.Count == 0)
            {
                return;
            }

            await FileLock.WaitAsync();
            try
            {
                var store = await GetStoreCoreAsync();
                bool changed = false;
                foreach (string path in paths)
                {
                    changed |= store.Remove(path);
                }

                if (changed)
                {
                    await SaveStoreCoreAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaTags_Remove");
            }
            finally
            {
                FileLock.Release();
            }
        }

        private static List<string> NormalizeTags(IEnumerable<string>? tags)
        {
            return tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag =>
                {
                    string trimmed = tag.Trim();
                    return trimmed[..Math.Min(trimmed.Length, MaxTagLength)];
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList()
                ?? new List<string>();
        }

        private async Task<Dictionary<string, List<string>>> GetStoreCoreAsync()
        {
            if (_cachedStore != null)
            {
                return _cachedStore;
            }

            if (!File.Exists(DataFilePath))
            {
                _cachedStore = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                return _cachedStore;
            }
            if (new FileInfo(DataFilePath).Length is <= 0 or > MaxStoreBytes)
            {
                _cachedStore = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                return _cachedStore;
            }

            string json = await File.ReadAllTextAsync(DataFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _cachedStore = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                return _cachedStore;
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);

            _cachedStore = (raw ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 32767)
                .Take(MaxEntries)
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                group => group.Key,
                group => NormalizeTags(group.First().Value),
                StringComparer.OrdinalIgnoreCase);
            return _cachedStore;
        }

        private async Task SaveStoreCoreAsync()
        {
            string json = JsonSerializer.Serialize(_cachedStore ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), JsonOptions);
            string tempFilePath = DataFilePath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempFilePath, json);
                File.Move(tempFilePath, DataFilePath, true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                }
                catch
                {
                    // 临时文件清理失败不掩盖落盘原始异常。
                }
                throw;
            }
        }
    }
}
