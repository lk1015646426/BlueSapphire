using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSapphire.Helpers
{
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    internal partial class AppSettingsJsonContext : JsonSerializerContext { }

    public static class AppSettings
    {
        private static readonly string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire");
        private static readonly string FilePath = Path.Combine(FolderPath, "config.json");

        private static Dictionary<string, JsonElement> _settingsCache = new();

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
                    var loaded = JsonSerializer.Deserialize(
                        json,
                        typeof(Dictionary<string, JsonElement>),
                        AppSettingsJsonContext.Default) as Dictionary<string, JsonElement>;
                    if (loaded != null) _settingsCache = loaded;
                }
            }
            catch
            {
                _settingsCache = new();
            }
        }

        public static void Save(string key, object value)
        {
            _settingsCache[key] = SerializeValue(value);
            try
            {
                string json = JsonSerializer.Serialize(
                    _settingsCache,
                    typeof(Dictionary<string, JsonElement>),
                    AppSettingsJsonContext.Default);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        public static T? Get<T>(string key, T? defaultValue = default)
        {
            if (_settingsCache.TryGetValue(key, out JsonElement val))
            {
                try
                {
                    if (typeof(T) == typeof(bool)) return (T)(object)val.GetBoolean();
                    if (typeof(T) == typeof(string)) return (T)(object)(val.GetString() ?? string.Empty);
                    return JsonSerializer.Deserialize<T>(val.GetRawText());
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private static JsonElement SerializeValue(object value)
        {
            return value switch
            {
                bool boolValue => JsonSerializer.SerializeToElement(boolValue, AppSettingsJsonContext.Default.Boolean),
                string stringValue => JsonSerializer.SerializeToElement(stringValue, AppSettingsJsonContext.Default.String),
                null => JsonSerializer.SerializeToElement<string?>(null),
                _ => JsonSerializer.SerializeToElement(value, value.GetType())
            };
        }
    }
}
