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
            await FileLock.WaitAsync();
            try
            {
                await PersistLogsAsync(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DevLog_Save");
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

            string json = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<DevLogItem>();
            }

            return JsonSerializer.Deserialize<List<DevLogItem>>(json) ?? new List<DevLogItem>();
        }

        private async Task PersistLogsAsync(List<DevLogItem> logs)
        {
            logs ??= new List<DevLogItem>();

            string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            string tempFilePath = DataFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFilePath, json);
            File.Move(tempFilePath, DataFilePath, true);

#if DEBUG
            try
            {
                var projAssetPath = TryGetProjectAssetPath();
                if (projAssetPath != null)
                {
                    File.Copy(DataFilePath, projAssetPath, true);
                    _logger.LogInformation($"Dev logs auto-synced to source directory: {projAssetPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-sync dev logs to source directory.");
            }
#endif
        }

        private string? TryGetProjectAssetPath()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && Directory.Exists(dir))
            {
                if (File.Exists(Path.Combine(dir, "BlueSapphire.csproj")))
                {
                    return Path.Combine(dir, "Assets", FileName);
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
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
