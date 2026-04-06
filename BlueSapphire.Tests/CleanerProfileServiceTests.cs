using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerProfileServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerProfileTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetProfileAsync_CreatesStableProfileWhenMissing()
    {
        CleanerProfileService service = new(new CleanerStateStore(_rootPath));

        CleanerProfileState profile = await service.GetProfileAsync();

        Assert.False(string.IsNullOrWhiteSpace(profile.DeviceProfileId));
        Assert.Equal("stable", profile.RolloutChannel);
        Assert.InRange(profile.DeviceBucket, 0, 99);
    }

    [Fact]
    public async Task SetRolloutChannelAsync_NormalizesChannelAndPersists()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerProfileService service = new(store);

        CleanerProfileState profile = await service.SetRolloutChannelAsync("CANARY");
        CleanerPreferenceState preferences = await store.LoadPreferencesAsync();

        Assert.Equal("canary", profile.RolloutChannel);
        Assert.Equal("canary", preferences.RolloutChannel);
        Assert.False(string.IsNullOrWhiteSpace(preferences.DeviceProfileId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
