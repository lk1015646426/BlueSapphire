using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services
{
    public class DevLogDataService
    {
        private readonly ILogger<DevLogDataService> _logger;
        private const string FileName = "DevMatrixLog.json";
        private const long MaxLogFileBytes = 5 * 1024 * 1024;
        private const int MaxLogEntries = 500;
        private static readonly SemaphoreSlim FileLock = new(1, 1);
        private readonly string? _rootPathOverride;
        private readonly string? _seedFilePathOverride;

        public DevLogDataService(ILogger<DevLogDataService> logger, string? rootPathOverride = null, string? seedFilePathOverride = null)
        {
            _logger = logger;
            _rootPathOverride = rootPathOverride;
            _seedFilePathOverride = seedFilePathOverride;
        }

        public string DataFilePath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_rootPathOverride))
                {
                    Directory.CreateDirectory(_rootPathOverride);
                    return Path.Combine(_rootPathOverride, FileName);
                }

                string appFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlueSapphire");

                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, FileName);
            }
        }

        public bool CanWrite => IsWritableInCurrentEnvironment();

        private string SeedFilePath => !string.IsNullOrWhiteSpace(_seedFilePathOverride)
            ? _seedFilePathOverride
            : Path.Combine(AppContext.BaseDirectory, "Assets", FileName);

        public async Task<List<DevLogItem>> LoadLogsAsync()
        {
            await FileLock.WaitAsync();
            try
            {
                await EnsureSeededAsync();

                if (!File.Exists(DataFilePath))
                {
                    return new List<DevLogItem>();
                }

                return await ReadLogsFromFileAsync(DataFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DevLog_Load");
                return new List<DevLogItem>();
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task SaveLogsAsync(List<DevLogItem> logs)
        {
            if (!CanWrite)
            {
                throw new InvalidOperationException("当前发布环境中的开发日志为只读内容。");
            }

            await FileLock.WaitAsync();
            try
            {
                await PersistLogsAsync(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DevLog_Save");
                throw;
            }
            finally
            {
                FileLock.Release();
            }
        }

        private async Task EnsureSeededAsync()
        {
            List<DevLogItem> seedLogs = await ReadLogsFromFileAsync(SeedFilePath);
            if (seedLogs.Count == 0)
            {
                return;
            }

            if (!File.Exists(DataFilePath))
            {
                await PersistLogsAsync(seedLogs);
                return;
            }

            List<DevLogItem> existingLogs = await ReadLogsFromFileAsync(DataFilePath);
            Dictionary<string, DevLogItem> logIndex = existingLogs
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) || !string.IsNullOrWhiteSpace(item.Version))
                .GroupBy(GetLogKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            bool changed = false;
            foreach (DevLogItem seedLog in seedLogs.OrderBy(item => item.Timestamp))
            {
                string key = GetLogKey(seedLog);
                if (string.IsNullOrWhiteSpace(key) || logIndex.ContainsKey(key))
                {
                    continue;
                }

                existingLogs.Add(seedLog);
                logIndex[key] = seedLog;
                changed = true;
            }

            if (changed)
            {
                await PersistLogsAsync(existingLogs);
            }
        }

        private static string GetLogKey(DevLogItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Version))
            {
                return $"version:{item.Version}";
            }

            return string.IsNullOrWhiteSpace(item.Id)
                ? string.Empty
                : $"id:{item.Id}";
        }

        private static async Task<List<DevLogItem>> ReadLogsFromFileAsync(string path)
        {
            if (!File.Exists(path))
            {
                return new List<DevLogItem>();
            }
            if (new FileInfo(path).Length is <= 0 or > MaxLogFileBytes)
            {
                return new List<DevLogItem>();
            }

            string json = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<DevLogItem>();
            }

            return JsonSerializer.Deserialize<List<DevLogItem>>(
                       json,
                       new JsonSerializerOptions { MaxDepth = 32 })?
                       .Take(MaxLogEntries)
                       .ToList()
                   ?? new List<DevLogItem>();
        }

        private async Task PersistLogsAsync(List<DevLogItem> logs)
        {
            logs ??= new List<DevLogItem>();

            List<DevLogItem> boundedLogs = logs
                .Take(MaxLogEntries)
                .Select(BoundLog)
                .ToList();
            string json = JsonSerializer.Serialize(boundedLogs, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
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
                catch { }
                throw;
            }

            try
            {
                string backupDir = Path.Combine(Path.GetDirectoryName(DataFilePath) ?? string.Empty, "LogBackups");
                Directory.CreateDirectory(backupDir);
                string backupPath = Path.Combine(backupDir, "DevMatrixLog_backup.json");
                File.Copy(DataFilePath, backupPath, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create local snapshot backup of dev logs.");
            }

        }

        private static DevLogItem BoundLog(DevLogItem source)
        {
            static string Limit(string? value, int maxLength)
            {
                string text = value ?? string.Empty;
                return text[..Math.Min(text.Length, maxLength)];
            }

            return new DevLogItem
            {
                Id = string.IsNullOrWhiteSpace(source.Id)
                    ? Guid.NewGuid().ToString("N")
                    : Limit(source.Id, 100),
                Title = Limit(source.Title, 200),
                Description = Limit(source.Description, 2000),
                FullContent = Limit(source.FullContent, 100_000),
                Version = Limit(source.Version, 50),
                UpdateLevel = Limit(source.UpdateLevel, 50),
                Status = source.Status,
                Timestamp = source.Timestamp
            };
        }

        private bool IsWritableInCurrentEnvironment()
        {
            if (!string.IsNullOrWhiteSpace(_rootPathOverride))
            {
                return true;
            }

            string? envOverride = Environment.GetEnvironmentVariable("BLUESAPPHIRE_DEVLOG_EDIT");
            if (string.Equals(envOverride, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(envOverride, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Debugger.IsAttached)
            {
                return true;
            }

#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
