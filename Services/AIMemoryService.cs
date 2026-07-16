using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public class AIMemoryService
    {
        private const int MaxRules = 100;
        private const int MaxRuleCharacters = 500;
        private const long MaxMemoryFileBytes = 512 * 1024;
        private readonly string _memoryFilePath;
        private readonly string _legacyMemoryFilePath;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private AIMemoryState? _cachedState;

        public AIMemoryService(string? rootPath = null)
        {
            string appFolder = rootPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueSapphire");
            Directory.CreateDirectory(appFolder);
            _memoryFilePath = Path.Combine(appFolder, "AIMemory.dat");
            _legacyMemoryFilePath = Path.Combine(appFolder, "AIMemory.json");
        }

        public async Task<List<string>> GetMemoryRulesAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                if (_cachedState!.IsPaused)
                {
                    return new List<string>();
                }

                return _cachedState.Entries
                    .Where(entry => entry.IsEnabled && !entry.IsExpired)
                    .Select(entry => entry.Content)
                    .ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<IReadOnlyList<AIMemoryEntry>> GetEntriesAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                return _cachedState!.Entries
                    .OrderByDescending(entry => entry.UpdatedAt)
                    .Select(Clone)
                    .ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> IsPausedAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                return _cachedState!.IsPaused;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<bool> AddMemoryRuleAsync(string rule)
        {
            return AddMemoryEntryAsync(rule, AIMemoryScope.Global, null, "用户确认");
        }

        public async Task<bool> AddMemoryEntryAsync(
            string content,
            AIMemoryScope scope,
            DateTimeOffset? expiresAt,
            string source)
        {
            string normalized = NormalizeRule(content);
            if (normalized.Length == 0)
            {
                return false;
            }

            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                List<AIMemoryEntry> entries = _cachedState!.Entries;
                if (entries.Any(entry =>
                    string.Equals(entry.Content, normalized, StringComparison.OrdinalIgnoreCase) &&
                    entry.Scope == scope))
                {
                    return false;
                }
                if (entries.Count >= MaxRules)
                {
                    throw new InvalidOperationException($"长期偏好最多保存 {MaxRules} 条。");
                }

                entries.Add(new AIMemoryEntry
                {
                    Content = normalized,
                    Scope = scope,
                    ExpiresAt = expiresAt,
                    Source = NormalizeSource(source)
                });
                await SaveMemoryCoreAsync();
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> UpdateEntryAsync(
            string id,
            string content,
            AIMemoryScope scope,
            DateTimeOffset? expiresAt,
            bool isEnabled)
        {
            string normalized = NormalizeRule(content);
            if (string.IsNullOrWhiteSpace(id) || normalized.Length == 0)
            {
                return false;
            }

            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                AIMemoryEntry? entry = _cachedState!.Entries.FirstOrDefault(item => item.Id == id);
                if (entry == null)
                {
                    return false;
                }

                entry.Content = normalized;
                entry.Scope = scope;
                entry.ExpiresAt = expiresAt;
                entry.IsEnabled = isEnabled;
                entry.UpdatedAt = DateTimeOffset.Now;
                await SaveMemoryCoreAsync();
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> RemoveEntryAsync(string id)
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                int removed = _cachedState!.Entries.RemoveAll(entry => entry.Id == id);
                if (removed == 0)
                {
                    return false;
                }
                await SaveMemoryCoreAsync();
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SetPausedAsync(bool paused)
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                _cachedState!.IsPaused = paused;
                await SaveMemoryCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<int> DeleteExpiredAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                int removed = _cachedState!.Entries.RemoveAll(entry => entry.IsExpired);
                if (removed > 0)
                {
                    await SaveMemoryCoreAsync();
                }
                return removed;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ClearMemoryAsync()
        {
            await _gate.WaitAsync();
            try
            {
                _cachedState = new AIMemoryState
                {
                    IsPaused = _cachedState?.IsPaused ?? false
                };
                await SaveMemoryCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task EnsureLoadedCoreAsync()
        {
            if (_cachedState != null)
            {
                return;
            }

            if (File.Exists(_memoryFilePath))
            {
                try
                {
                    FileInfo info = new(_memoryFilePath);
                    if (info.Length is <= 0 or > MaxMemoryFileBytes)
                    {
                        _cachedState = new AIMemoryState();
                        return;
                    }

                    byte[] protectedBytes = await File.ReadAllBytesAsync(_memoryFilePath);
                    byte[] jsonBytes = ProtectedData.Unprotect(
                        protectedBytes,
                        optionalEntropy: null,
                        DataProtectionScope.CurrentUser);
                    _cachedState = DeserializeState(jsonBytes);
                    return;
                }
                catch
                {
                    _cachedState = new AIMemoryState();
                    return;
                }
            }

            _cachedState = await LoadLegacyStateAsync();
            if (_cachedState.Entries.Count > 0)
            {
                await SaveMemoryCoreAsync();
                try { File.Delete(_legacyMemoryFilePath); } catch { }
            }
        }

        private async Task<AIMemoryState> LoadLegacyStateAsync()
        {
            try
            {
                if (!File.Exists(_legacyMemoryFilePath) ||
                    new FileInfo(_legacyMemoryFilePath).Length is <= 0 or > MaxMemoryFileBytes)
                {
                    return new AIMemoryState();
                }

                byte[] data = await File.ReadAllBytesAsync(_legacyMemoryFilePath);
                return DeserializeState(data);
            }
            catch
            {
                return new AIMemoryState();
            }
        }

        private static AIMemoryState DeserializeState(byte[] jsonBytes)
        {
            try
            {
                AIMemoryState? state = JsonSerializer.Deserialize<AIMemoryState>(jsonBytes);
                if (state?.Entries != null)
                {
                    state.Entries = NormalizeEntries(state.Entries);
                    return state;
                }
            }
            catch
            {
            }

            try
            {
                List<string>? legacyRules = JsonSerializer.Deserialize<List<string>>(jsonBytes);
                return new AIMemoryState
                {
                    Entries = NormalizeLegacyRules(legacyRules)
                };
            }
            catch
            {
                return new AIMemoryState();
            }
        }

        private async Task SaveMemoryCoreAsync()
        {
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_cachedState ?? new AIMemoryState());
            byte[] protectedBytes = ProtectedData.Protect(
                jsonBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            string temporaryPath = _memoryFilePath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes);
                File.Move(temporaryPath, _memoryFilePath, true);
            }
            catch
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch { }
                throw;
            }
        }

        private static List<AIMemoryEntry> NormalizeEntries(IEnumerable<AIMemoryEntry>? entries)
        {
            return entries?
                .Where(entry => entry != null)
                .Select(entry =>
                {
                    entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
                    entry.Content = NormalizeRule(entry.Content);
                    entry.Source = NormalizeSource(entry.Source);
                    return entry;
                })
                .Where(entry => entry.Content.Length > 0)
                .DistinctBy(entry => $"{entry.Scope}:{entry.Content}", StringComparer.OrdinalIgnoreCase)
                .Take(MaxRules)
                .ToList()
                ?? new List<AIMemoryEntry>();
        }

        private static List<AIMemoryEntry> NormalizeLegacyRules(IEnumerable<string>? rules)
        {
            return rules?
                .Select(NormalizeRule)
                .Where(rule => rule.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRules)
                .Select(rule => new AIMemoryEntry { Content = rule })
                .ToList()
                ?? new List<AIMemoryEntry>();
        }

        private static AIMemoryEntry Clone(AIMemoryEntry source)
        {
            return new AIMemoryEntry
            {
                Id = source.Id,
                Content = source.Content,
                Scope = source.Scope,
                Source = source.Source,
                IsEnabled = source.IsEnabled,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                ExpiresAt = source.ExpiresAt
            };
        }

        private static string NormalizeRule(string? rule)
        {
            string value = (rule ?? string.Empty).Trim();
            return value[..Math.Min(value.Length, MaxRuleCharacters)];
        }

        private static string NormalizeSource(string? source)
        {
            string value = (source ?? "用户确认").Trim();
            return value[..Math.Min(value.Length, 80)];
        }
    }
}
