using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        private const string SecretKeySuffix = ".Secret";

        private static Dictionary<string, JsonElement> _settingsCache = new();

        static AppSettings()
        {
            LoadSettings();
            MigratePlaintextSecrets();
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

        private static void MigratePlaintextSecrets()
        {
            string? plainKey = Get<string>("DeepSeekApiKey", null);
            if (string.IsNullOrWhiteSpace(plainKey)) return;
            SaveSecret("DeepSeekApiKey", plainKey);
            _settingsCache.Remove("DeepSeekApiKey");
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

        public static void SaveSecret(string key, string value)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(value);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            string base64 = Convert.ToBase64String(encryptedBytes);
            Save(key + SecretKeySuffix, base64);
        }

        public static string? GetSecret(string key)
        {
            string? base64 = Get<string>(key + SecretKeySuffix, null);
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(base64);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return null;
            }
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
