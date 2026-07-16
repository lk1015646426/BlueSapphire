using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private List<string>? _cachedRules;

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
                return _cachedRules!.ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> AddMemoryRuleAsync(string rule)
        {
            string normalized = NormalizeRule(rule);
            if (normalized.Length == 0) return false;

            await _gate.WaitAsync();
            try
            {
                await EnsureLoadedCoreAsync();
                List<string> rules = _cachedRules!;
                if (rules.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (rules.Count >= MaxRules)
                {
                    throw new InvalidOperationException($"长期偏好最多保存 {MaxRules} 条。");
                }

                rules.Add(normalized);
                await SaveMemoryCoreAsync();
                return true;
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
                _cachedRules = new List<string>();
                await SaveMemoryCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task EnsureLoadedCoreAsync()
        {
            if (_cachedRules != null) return;

            if (File.Exists(_memoryFilePath))
            {
                try
                {
                    FileInfo info = new(_memoryFilePath);
                    if (info.Length is <= 0 or > MaxMemoryFileBytes)
                    {
                        _cachedRules = new List<string>();
                        return;
                    }

                    byte[] protectedBytes = await File.ReadAllBytesAsync(_memoryFilePath);
                    byte[] jsonBytes = ProtectedData.Unprotect(
                        protectedBytes,
                        optionalEntropy: null,
                        DataProtectionScope.CurrentUser);
                    _cachedRules = NormalizeRules(
                        JsonSerializer.Deserialize<List<string>>(jsonBytes));
                    return;
                }
                catch
                {
                    _cachedRules = new List<string>();
                    return;
                }
            }

            _cachedRules = await LoadLegacyRulesAsync();
            if (_cachedRules.Count > 0)
            {
                await SaveMemoryCoreAsync();
                try { File.Delete(_legacyMemoryFilePath); } catch { }
            }
        }

        private async Task<List<string>> LoadLegacyRulesAsync()
        {
            try
            {
                if (!File.Exists(_legacyMemoryFilePath) ||
                    new FileInfo(_legacyMemoryFilePath).Length is <= 0 or > MaxMemoryFileBytes)
                {
                    return new List<string>();
                }

                string json = await File.ReadAllTextAsync(_legacyMemoryFilePath);
                return NormalizeRules(JsonSerializer.Deserialize<List<string>>(json));
            }
            catch
            {
                return new List<string>();
            }
        }

        private async Task SaveMemoryCoreAsync()
        {
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_cachedRules ?? new List<string>());
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
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch { }
                throw;
            }
        }

        private static List<string> NormalizeRules(IEnumerable<string>? rules)
        {
            return rules?
                .Select(NormalizeRule)
                .Where(rule => rule.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRules)
                .ToList()
                ?? new List<string>();
        }

        private static string NormalizeRule(string? rule)
        {
            string value = (rule ?? string.Empty).Trim();
            return value[..Math.Min(value.Length, MaxRuleCharacters)];
        }
    }
}
