using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerRuleServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerRuleTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetRulesAsync_LoadsBundledCleanerRules()
    {
        CleanerRuleService service = new(new CleanerStateStore(_rootPath));

        IReadOnlyList<CleanerRuleDefinition> rules = await service.GetRulesAsync();

        Assert.NotEmpty(rules);
        Assert.Contains(rules, rule => string.Equals(rule.Id, "windows_temp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "windows_update_download", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "windows_wer_reports", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "windows_wer_temp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "windows_minidump", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "live_kernel_reports", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "brave_http_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "discord_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "slack_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "adobe_media_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "jetbrains_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "steam_html_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "github_desktop_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "postman_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "lark_feishu_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "epic_launcher_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "msteams_webview_cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportRulePackAsync_SandboxesExternalRulesWithoutOverridingBuiltIns()
    {
        string builtInPath = Path.Combine(_rootPath, "builtin.json");
        string importPath = Path.Combine(_rootPath, "external.json");
        Directory.CreateDirectory(_rootPath);

        await File.WriteAllTextAsync(builtInPath,
            """
            {
              "rules": [
                {
                  "id": "built_in_cache",
                  "name": "内置缓存",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\A"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true
                },
                {
                  "id": "legacy_rule",
                  "name": "旧规则",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\B"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true
                }
              ]
            }
            """);

        await File.WriteAllTextAsync(importPath,
            """
            {
              "version": "2026.03.29-hotfix",
              "source": "测试规则包",
              "disabledRuleIds": ["legacy_rule"],
              "rules": [
                {
                  "id": "built_in_cache",
                  "name": "覆盖后的缓存",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\Override"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Medium",
                  "defaultSelected": false
                },
                {
                  "id": "new_rule",
                  "name": "新增规则",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Deep",
                  "paths": ["%TEMP%\\New"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true
                }
              ]
            }
            """);

        CleanerStateStore store = new(_rootPath);
        CleanerRuleService service = new(store, builtInPath);

        CleanerRuleBundleStatus status = await service.ImportRulePackAsync(importPath);
        IReadOnlyList<CleanerRuleDefinition> rules = await service.GetRulesAsync();

        Assert.True(status.HasExternalBundle);
        Assert.Equal(2, status.BuiltInRuleCount);
        Assert.Equal(3, status.EffectiveRuleCount);
        Assert.Equal(2, status.ExternalRuleCount);
        Assert.Equal(0, status.DisabledRuleCount);
        Assert.Contains(rules, rule => string.Equals(rule.Id, "legacy_rule", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule =>
            string.Equals(rule.Id, "built_in_cache", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Name, "内置缓存", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule =>
            string.Equals(rule.Id, "new_rule", StringComparison.OrdinalIgnoreCase) &&
            rule.ViewOnly &&
            !rule.DefaultSelected &&
            rule.RiskLevel == CleanerRiskLevel.High &&
            rule.ExecutionMode == CleanerExecutionMode.None);
    }

    [Fact]
    public async Task DisableRuleLocallyAsync_RemovesRuleUntilRestored()
    {
        string builtInPath = Path.Combine(_rootPath, "builtin-local-disable.json");
        Directory.CreateDirectory(_rootPath);

        await File.WriteAllTextAsync(builtInPath,
            """
            {
              "rules": [
                {
                  "id": "cache_rule",
                  "name": "缓存规则",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\A"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true
                },
                {
                  "id": "log_rule",
                  "name": "日志规则",
                  "category": "app_logs",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\B"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true
                }
              ]
            }
            """);

        CleanerStateStore store = new(_rootPath);
        CleanerRuleService service = new(store, builtInPath);

        CleanerRuleBundleStatus disabledStatus = await service.DisableRuleLocallyAsync("log_rule");
        IReadOnlyList<CleanerRuleDefinition> effectiveRules = await service.GetRulesAsync();
        CleanerRuleUpdateState updateState = await service.GetUpdateStateAsync();

        Assert.Equal(1, disabledStatus.LocalDisabledRuleCount);
        Assert.DoesNotContain(effectiveRules, rule => string.Equals(rule.Id, "log_rule", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("log_rule", updateState.LocalDisabledRuleIds, StringComparer.OrdinalIgnoreCase);

        CleanerRuleBundleStatus restoredStatus = await service.EnableAllLocallyDisabledRulesAsync();
        IReadOnlyList<CleanerRuleDefinition> restoredRules = await service.GetRulesAsync();

        Assert.Equal(0, restoredStatus.LocalDisabledRuleCount);
        Assert.Contains(restoredRules, rule => string.Equals(rule.Id, "log_rule", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetRulesAsync_FiltersRulesByRolloutChannelAndPercentage()
    {
        string builtInPath = Path.Combine(_rootPath, "builtin-rollout.json");
        Directory.CreateDirectory(_rootPath);

        await File.WriteAllTextAsync(builtInPath,
            """
            {
              "rules": [
                {
                  "id": "stable_rule",
                  "name": "稳定规则",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\Stable"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true,
                  "rolloutChannels": ["stable"]
                },
                {
                  "id": "canary_rule",
                  "name": "灰度规则",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\Canary"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true,
                  "rolloutChannels": ["canary"]
                },
                {
                  "id": "partial_rule",
                  "name": "百分比规则",
                  "category": "app_cache",
                  "scanKind": "DirectoryContents",
                  "scope": "Quick",
                  "paths": ["%TEMP%\\Partial"],
                  "executionMode": "Quarantine",
                  "riskLevel": "Low",
                  "defaultSelected": true,
                  "rolloutChannels": ["canary"],
                  "rolloutPercentage": 0
                }
              ]
            }
            """);

        CleanerStateStore store = new(_rootPath);
        await store.SavePreferencesAsync(new CleanerPreferenceState
        {
            DeviceProfileId = "test-device-rollout",
            RolloutChannel = "canary"
        });

        CleanerProfileService profileService = new(store);
        CleanerRuleService service = new(store, profileService, builtInPath);

        CleanerRuleBundleStatus status = await service.GetStatusAsync();
        IReadOnlyList<CleanerRuleDefinition> rules = await service.GetRulesAsync();

        Assert.Equal("canary", status.ActiveRolloutChannel);
        Assert.Equal(2, status.RolloutFilteredRuleCount);
        Assert.DoesNotContain(rules, rule => string.Equals(rule.Id, "stable_rule", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rules, rule => string.Equals(rule.Id, "partial_rule", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, rule => string.Equals(rule.Id, "canary_rule", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
