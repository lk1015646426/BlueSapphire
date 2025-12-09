using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BlueSapphire.Helpers
{
    public static class AppSettings
    {
        // 获取标准存储路径: %LOCALAPPDATA%\BlueSapphire\config.json
        private static readonly string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire");
        private static readonly string FilePath = Path.Combine(FolderPath, "config.json");

        private static Dictionary<string, object> _settingsCache = new Dictionary<string, object>();

        static AppSettings()
        {
            LoadSettings();
        }

        private static void LoadSettings()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                    // 将 JsonElement 转换为基础类型，方便使用
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded)
                        {
                            if (kvp.Value.ValueKind == JsonValueKind.True || kvp.Value.ValueKind == JsonValueKind.False)
                            {
                                _settingsCache[kvp.Key] = kvp.Value.GetBoolean();
                            }
                            else if (kvp.Value.ValueKind == JsonValueKind.String)
                            {
                                _settingsCache[kvp.Key] = kvp.Value.GetString();
                            }
                            // 这里可以根据需要扩展更多类型
                        }
                    }
                }
            }
            catch
            {
                // 读取失败则使用空字典
                _settingsCache = new Dictionary<string, object>();
            }
        }

        public static void Save(string key, object value)
        {
            _settingsCache[key] = value;
            try
            {
                string json = JsonSerializer.Serialize(_settingsCache);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // 忽略写入错误
            }
        }

        public static T? Get<T>(string key, T? defaultValue = default)
        {
            if (_settingsCache.TryGetValue(key, out object? val))
            {
                if (val is T tVal) return tVal;
            }
            return defaultValue;
        }
    }
}