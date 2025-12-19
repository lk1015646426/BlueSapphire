using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization; // [新增]

namespace BlueSapphire.Helpers
{
    // [核心修改] 定义 JSON 源生成上下文，确保 AOT 兼容
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    internal partial class AppSettingsJsonContext : JsonSerializerContext { }

    public static class AppSettings
    {
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
                if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);

                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    // [核心修改] 使用源生成上下文进行反序列化
                    var loaded = JsonSerializer.Deserialize(json, typeof(Dictionary<string, object>), AppSettingsJsonContext.Default) as Dictionary<string, object>;
                    if (loaded != null) _settingsCache = loaded;
                }
            }
            catch
            {
                _settingsCache = new Dictionary<string, object>();
            }
        }

        public static void Save(string key, object value)
        {
            _settingsCache[key] = value;
            try
            {
                // [核心修改] 使用源生成上下文进行序列化
                string json = JsonSerializer.Serialize(_settingsCache, typeof(Dictionary<string, object>), AppSettingsJsonContext.Default);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        public static T? Get<T>(string key, T? defaultValue = default)
        {
            if (_settingsCache.TryGetValue(key, out object? val))
            {
                // AOT 环境下处理 JsonElement 的转换
                if (val is JsonElement element)
                {
                    if (typeof(T) == typeof(bool)) return (T)(object)element.GetBoolean();
                    if (typeof(T) == typeof(string)) return (T)(object)element.GetString()!;
                }
                if (val is T tVal) return tVal;
            }
            return defaultValue;
        }
    }
}