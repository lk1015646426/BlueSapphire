using BlueSapphire.Helpers;
using System.Reflection;
using System.Text.Json;

namespace BlueSapphire.Tests;

public class AppSettingsTests : IDisposable
{
    private static readonly string TestRoot = Path.Combine(
        Path.GetTempPath(),
        "BlueSapphire.Tests",
        "AppSettings",
        Guid.NewGuid().ToString("N"));

    static AppSettingsTests()
    {
        Environment.SetEnvironmentVariable("BLUESAPPHIRE_SETTINGS_ROOT", TestRoot);
    }

    public AppSettingsTests()
    {
        Directory.CreateDirectory(TestRoot);
    }

    [Fact]
    public void SaveAndReload_BoolValue_RoundTripsSuccessfully()
    {
        string key = $"TestBool_{Guid.NewGuid():N}";
        AppSettings.Save(key, true);

        ReloadSettingsCache();

        Assert.True(AppSettings.Get(key, false));
    }

    [Fact]
    public void SaveAndReload_StringValue_RoundTripsSuccessfully()
    {
        string key = $"TestString_{Guid.NewGuid():N}";
        string expected = $"value_{Guid.NewGuid():N}";
        AppSettings.Save(key, expected);

        ReloadSettingsCache();

        Assert.Equal(expected, AppSettings.Get<string?>(key));
    }

    [Fact]
    public void Get_ReturnsProvidedDefault_WhenKeyDoesNotExist()
    {
        string key = $"Missing_{Guid.NewGuid():N}";

        Assert.Equal("fallback", AppSettings.Get(key, "fallback"));
    }

    [Fact]
    public void Save_DoesNotThrow_AndRaisesPersistFailed_WhenDiskWriteFails()
    {
        string key = $"PersistFail_{Guid.NewGuid():N}";
        // 用同名目录占住临时写入路径，使 WriteAllText 必然失败，模拟磁盘写入故障。
        string tempWritePath = Path.Combine(TestRoot, "config.json.tmp");
        Directory.CreateDirectory(tempWritePath);

        Exception? observedError = null;
        string? observedKey = null;
        void OnPersistFailed(Exception ex, string failedKey)
        {
            observedError = ex;
            observedKey = failedKey;
        }

        AppSettings.PersistFailed += OnPersistFailed;
        try
        {
            AppSettings.Save(key, true);
        }
        finally
        {
            AppSettings.PersistFailed -= OnPersistFailed;
            // 恢复临时路径，避免占用同一 TestRoot 的其他测试写入失败。
            Directory.Delete(tempWritePath, recursive: true);
        }

        Assert.NotNull(observedError);
        Assert.Equal(key, observedKey);
    }

    private static void ReloadSettingsCache()
    {
        Type settingsType = typeof(AppSettings);
        FieldInfo cacheField = settingsType.GetField("_settingsCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo loadMethod = settingsType.GetMethod("LoadSettings", BindingFlags.NonPublic | BindingFlags.Static)!;

        cacheField.SetValue(null, new Dictionary<string, JsonElement>());
        loadMethod.Invoke(null, null);
    }

    public void Dispose()
    {
        if (Directory.Exists(TestRoot))
        {
            Directory.Delete(TestRoot, recursive: true);
        }
    }
}
