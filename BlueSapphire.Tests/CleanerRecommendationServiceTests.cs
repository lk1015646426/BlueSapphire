using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerRecommendationServiceTests
{
    [Fact]
    public void BuildSummary_SuggestsAutomaticLowRiskCleanupForStableLowRiskPattern()
    {
        CleanerRecommendationService service = new();
        CleanerRecommendationSummary summary = service.BuildSummary(
            new CleanerAuditSnapshot
            {
                TotalCleanupRuns = 3,
                TotalCleanupFailures = 0
            },
            [
                new CleanerScanItem
                {
                    Name = "低风险缓存",
                    Category = "app_cache",
                    SizeBytes = 2L * 1024 * 1024 * 1024,
                    RiskLevel = CleanerRiskLevel.Low,
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    CanSelect = true
                }
            ],
            new CleanerProfileState
            {
                DeviceProfileId = "profile",
                RolloutChannel = "stable",
                DeviceBucket = 10
            },
            new CleanerAutomationStatus
            {
                ReminderEnabled = true,
                AutoLowRiskCleanupEnabled = false
            },
            new CleanerRuleBundleStatus());

        Assert.Contains(summary.Entries, entry => entry.Title.Contains("自动低风险保洁", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSummary_HighlightsReviewItemsWhenReviewBytesDominate()
    {
        CleanerRecommendationService service = new();
        CleanerRecommendationSummary summary = service.BuildSummary(
            new CleanerAuditSnapshot(),
            [
                new CleanerScanItem
                {
                    Name = "中风险缓存",
                    Category = "app_cache",
                    SizeBytes = 1024 * 1024 * 1024,
                    RiskLevel = CleanerRiskLevel.Medium,
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = false,
                    CanSelect = true
                }
            ],
            new CleanerProfileState
            {
                DeviceProfileId = "profile",
                RolloutChannel = "stable",
                DeviceBucket = 5
            },
            new CleanerAutomationStatus(),
            new CleanerRuleBundleStatus());

        Assert.Contains(summary.Entries, entry => entry.Title.Contains("人工确认", StringComparison.OrdinalIgnoreCase));
    }
}
