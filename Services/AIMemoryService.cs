using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public class AIMemoryService
    {
        private readonly string _memoryFilePath;
        private List<string>? _cachedRules;

        public AIMemoryService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appData, "BlueSapphire");
            Directory.CreateDirectory(appFolder);
            _memoryFilePath = Path.Combine(appFolder, "AIMemory.json");
        }

        public async Task<List<string>> GetMemoryRulesAsync()
        {
            if (_cachedRules != null)
                return _cachedRules;

            if (!File.Exists(_memoryFilePath))
            {
                _cachedRules = new List<string>();
                return _cachedRules;
            }

            try
            {
                string json = await File.ReadAllTextAsync(_memoryFilePath);
                _cachedRules = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                _cachedRules = new List<string>();
            }

            return _cachedRules;
        }

        public async Task AddMemoryRuleAsync(string rule)
        {
            if (string.IsNullOrWhiteSpace(rule)) return;

            var rules = await GetMemoryRulesAsync();
            if (!rules.Contains(rule))
            {
                rules.Add(rule);
                await SaveMemoryAsync(rules);
            }
        }

        public async Task ClearMemoryAsync()
        {
            _cachedRules = new List<string>();
            await SaveMemoryAsync(_cachedRules);
        }

        private async Task SaveMemoryAsync(List<string> rules)
        {
            try
            {
                string json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                await File.WriteAllTextAsync(_memoryFilePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
