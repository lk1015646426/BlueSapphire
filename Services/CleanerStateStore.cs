using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerStateStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _rootPath;
        
        private SemaphoreSlim GetLock(string path) => _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        public CleanerStateStore(string? rootPath = null)
        {
            _rootPath = rootPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueSapphire",
                "CleanerAssistant");

            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(QuarantineRootPath);
            Directory.CreateDirectory(RulePackDirectoryPath);
        }

        public string RootPath => _rootPath;
        public string QuarantineRootPath => Path.Combine(_rootPath, "Quarantine");
        public string RulePackDirectoryPath => Path.Combine(_rootPath, "RulePacks");
        public string ImportedRulePackPath => Path.Combine(RulePackDirectoryPath, "cleaner-rules.bundle.json");
        private string HistoryFilePath => Path.Combine(_rootPath, "cleanup-history.json");
        private string ExclusionsFilePath => Path.Combine(_rootPath, "exclusions.json");
        private string AuditFilePath => Path.Combine(_rootPath, "audit.json");
        private string PreferencesFilePath => Path.Combine(_rootPath, "preferences.json");
        private string RuleUpdateStateFilePath => Path.Combine(_rootPath, "rule-update.json");

        public async Task<IReadOnlyList<CleanerCleanupBatch>> LoadHistoryAsync()
        {
            SemaphoreSlim fileLock = GetLock(HistoryFilePath);
            await fileLock.WaitAsync();
            try
            {
                return await ReadAsync(HistoryFilePath, CleanerStoreJsonContext.Default.ListCleanerCleanupBatch);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveHistoryAsync(IReadOnlyList<CleanerCleanupBatch> history)
        {
            SemaphoreSlim fileLock = GetLock(HistoryFilePath);
            await fileLock.WaitAsync();
            try
            {
                await WriteAsync(HistoryFilePath, history, CleanerStoreJsonContext.Default.ListCleanerCleanupBatch);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task<IReadOnlyList<CleanerExclusionEntry>> LoadExclusionsAsync()
        {
            SemaphoreSlim fileLock = GetLock(ExclusionsFilePath);
            await fileLock.WaitAsync();
            try
            {
                return await ReadAsync(ExclusionsFilePath, CleanerStoreJsonContext.Default.ListCleanerExclusionEntry);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveExclusionsAsync(IReadOnlyList<CleanerExclusionEntry> exclusions)
        {
            SemaphoreSlim fileLock = GetLock(ExclusionsFilePath);
            await fileLock.WaitAsync();
            try
            {
                await WriteAsync(ExclusionsFilePath, exclusions, CleanerStoreJsonContext.Default.ListCleanerExclusionEntry);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task<CleanerAuditSnapshot> LoadAuditAsync()
        {
            SemaphoreSlim fileLock = GetLock(AuditFilePath);
            await fileLock.WaitAsync();
            try
            {
                if (!File.Exists(AuditFilePath))
                {
                    return new CleanerAuditSnapshot();
                }

                await using FileStream stream = File.OpenRead(AuditFilePath);
                CleanerAuditSnapshot? value = await JsonSerializer.DeserializeAsync(stream, CleanerStoreJsonContext.Default.CleanerAuditSnapshot);
                return value ?? new CleanerAuditSnapshot();
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveAuditAsync(CleanerAuditSnapshot snapshot)
        {
            SemaphoreSlim fileLock = GetLock(AuditFilePath);
            await fileLock.WaitAsync();
            try
            {
                string tempFile = AuditFilePath + ".tmp";
                await using (FileStream stream = File.Create(tempFile))
                {
                    await JsonSerializer.SerializeAsync(stream, snapshot, CleanerStoreJsonContext.Default.CleanerAuditSnapshot);
                }

                File.Move(tempFile, AuditFilePath, true);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task<CleanerPreferenceState> LoadPreferencesAsync()
        {
            SemaphoreSlim fileLock = GetLock(PreferencesFilePath);
            await fileLock.WaitAsync();
            try
            {
                if (!File.Exists(PreferencesFilePath))
                {
                    return new CleanerPreferenceState();
                }

                await using FileStream stream = File.OpenRead(PreferencesFilePath);
                CleanerPreferenceState? value = await JsonSerializer.DeserializeAsync(stream, CleanerStoreJsonContext.Default.CleanerPreferenceState);
                return value ?? new CleanerPreferenceState();
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SavePreferencesAsync(CleanerPreferenceState preferences)
        {
            SemaphoreSlim fileLock = GetLock(PreferencesFilePath);
            await fileLock.WaitAsync();
            try
            {
                string tempFile = PreferencesFilePath + ".tmp";
                await using (FileStream stream = File.Create(tempFile))
                {
                    await JsonSerializer.SerializeAsync(stream, preferences, CleanerStoreJsonContext.Default.CleanerPreferenceState);
                }

                File.Move(tempFile, PreferencesFilePath, true);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task<CleanerRuleUpdateState> LoadRuleUpdateStateAsync()
        {
            SemaphoreSlim fileLock = GetLock(RuleUpdateStateFilePath);
            await fileLock.WaitAsync();
            try
            {
                if (!File.Exists(RuleUpdateStateFilePath))
                {
                    return new CleanerRuleUpdateState();
                }

                await using FileStream stream = File.OpenRead(RuleUpdateStateFilePath);
                CleanerRuleUpdateState? value = await JsonSerializer.DeserializeAsync(stream, CleanerStoreJsonContext.Default.CleanerRuleUpdateState);
                return value ?? new CleanerRuleUpdateState();
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveRuleUpdateStateAsync(CleanerRuleUpdateState state)
        {
            SemaphoreSlim fileLock = GetLock(RuleUpdateStateFilePath);
            await fileLock.WaitAsync();
            try
            {
                string tempFile = RuleUpdateStateFilePath + ".tmp";
                await using (FileStream stream = File.Create(tempFile))
                {
                    await JsonSerializer.SerializeAsync(stream, state, CleanerStoreJsonContext.Default.CleanerRuleUpdateState);
                }

                File.Move(tempFile, RuleUpdateStateFilePath, true);
            }
            finally
            {
                fileLock.Release();
            }
        }

        private static async Task<IReadOnlyList<T>> ReadAsync<T>(string path, JsonTypeInfo<List<T>> typeInfo)
        {
            if (!File.Exists(path))
            {
                return Array.Empty<T>();
            }

            await using FileStream stream = File.OpenRead(path);
            List<T>? value = await JsonSerializer.DeserializeAsync(stream, typeInfo);
            return value ?? new List<T>();
        }

        private static async Task WriteAsync<T>(string path, IReadOnlyList<T> value, JsonTypeInfo<List<T>> typeInfo)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempFile = path + ".tmp";
            await using (FileStream stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, new List<T>(value), typeInfo);
            }

            File.Move(tempFile, path, true);
        }
    }

    [JsonSerializable(typeof(List<CleanerCleanupBatch>))]
    [JsonSerializable(typeof(List<CleanerCleanupEntry>))]
    [JsonSerializable(typeof(List<CleanerExclusionEntry>))]
    [JsonSerializable(typeof(CleanerAuditSnapshot))]
    [JsonSerializable(typeof(CleanerPreferenceState))]
    [JsonSerializable(typeof(CleanerRuleUpdateState))]
    internal partial class CleanerStoreJsonContext : JsonSerializerContext
    {
    }
}
