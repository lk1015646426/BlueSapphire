using BlueSapphire.Models;
using BlueSapphire.Services;
using System.Text.Json;

namespace BlueSapphire.Tests;

public class CleanerTelemetryServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerTelemetryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveSettingsAsync_PersistsEndpointAndEnabledState()
    {
        CleanerTelemetryService service = CreateService();

        CleanerTelemetryStatus status = await service.SaveSettingsAsync(true, "https://example.com/telemetry");

        Assert.True(status.Enabled);
        Assert.Equal("https://example.com/telemetry", status.Endpoint);
    }

    [Fact]
    public async Task UploadNowAsync_SendsPayloadAndUpdatesUploadState()
    {
        CleanerStateStore store = new(_rootPath);
        await store.SavePreferencesAsync(new CleanerPreferenceState
        {
            TelemetryEnabled = true,
            TelemetryEndpoint = "https://example.com/telemetry",
            DeviceProfileId = "telemetry-device",
            RolloutChannel = "stable"
        });

        CleanerAuditService auditService = new(store);
        await auditService.RecordScanAsync(new CleanerScanReport
        {
            Scope = CleanerScanScope.Quick,
            Duration = TimeSpan.FromSeconds(2),
            Items =
            [
                new CleanerScanItem
                {
                    RuleId = "cache_rule",
                    Name = "缓存",
                    Category = "app_cache",
                    SizeBytes = 1024 * 1024 * 512,
                    RiskLevel = CleanerRiskLevel.Low,
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true
                }
            ]
        });

        string? payloadJson = null;
        CleanerTelemetryService service = CreateService(
            store,
            async (endpoint, payload, _) =>
            {
                payloadJson = payload;
                await Task.CompletedTask;
                return new CleanerTelemetryUploadResult(true, "accepted");
            });

        CleanerTelemetryStatus status = await service.UploadNowAsync();

        Assert.NotNull(payloadJson);
        using JsonDocument document = JsonDocument.Parse(payloadJson!);
        Assert.Equal("stable", document.RootElement.GetProperty("profile").GetProperty("rolloutChannel").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("audit").GetProperty("totalScans").GetInt32());
        Assert.NotNull(status.LastUploadedAt);
        Assert.Contains("上传成功", status.LastStatusText);
    }

    private CleanerTelemetryService CreateService(
        CleanerStateStore? store = null,
        Func<Uri, string, CancellationToken, Task<CleanerTelemetryUploadResult>>? uploader = null)
    {
        CleanerStateStore stateStore = store ?? new CleanerStateStore(_rootPath);
        CleanerProfileService profileService = new(stateStore);
        CleanerAuditService auditService = new(stateStore);
        CleanerRuleService ruleService = new(stateStore, profileService);
        return new CleanerTelemetryService(stateStore, auditService, ruleService, profileService, uploader: uploader);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
