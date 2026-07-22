using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerRiskEvaluatorTests
{
    private readonly CleanerRiskEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_ReturnsLowRiskForTrustedTempRule()
    {
        CleanerRuleDefinition rule = new()
        {
            Id = "temp",
            Name = "Temp",
            OwnerApp = "BlueSapphire",
            DefaultSelected = true,
            ExecutionMode = CleanerExecutionMode.Quarantine
        };

        string path = Path.Combine(Path.GetTempPath(), "BlueSapphire", "Temp");
        CleanerRiskAssessment result = _evaluator.Evaluate(rule, path, false, DateTimeOffset.Now.AddDays(-30), 10 * 1024 * 1024);

        Assert.Equal(CleanerRiskLevel.Low, result.RiskLevel);
        Assert.True(result.CanSelect);
        Assert.Contains("命中可信规则", result.Detail);
    }

    [Fact]
    public void Evaluate_ReturnsHighRiskForDownloadsDocument()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "report.docx");

        CleanerRiskAssessment result = _evaluator.Evaluate(null, path, false, DateTimeOffset.Now, 100);

        Assert.Equal(CleanerRiskLevel.High, result.RiskLevel);
        Assert.False(result.CanSelect);
    }

    [Fact]
    public void Evaluate_KeepsRecentKnownLowRiskCacheInLowBucket()
    {
        CleanerRuleDefinition rule = new()
        {
            Id = "browser_cache",
            Name = "Browser Cache",
            RiskLevel = CleanerRiskLevel.Low,
            DefaultSelected = true,
            ExecutionMode = CleanerExecutionMode.Quarantine
        };

        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Browser",
            "Cache");

        CleanerRiskAssessment result = _evaluator.Evaluate(
            rule,
            path,
            isLocked: false,
            modifyTime: DateTimeOffset.Now,
            sizeBytes: 2L * 1024 * 1024 * 1024);

        Assert.Equal(CleanerRiskLevel.Low, result.RiskLevel);
        Assert.True(result.CanSelect);
        Assert.Contains("规则基线为低风险", result.Detail);
    }

    [Fact]
    public void Evaluate_DoesNotPromoteDeclaredMediumRuleToLowRisk()
    {
        CleanerRuleDefinition rule = new()
        {
            Id = "diagnostic_logs",
            Name = "Diagnostic Logs",
            RiskLevel = CleanerRiskLevel.Medium,
            ExecutionMode = CleanerExecutionMode.Quarantine
        };

        string path = Path.Combine(Path.GetTempPath(), "DiagnosticLogs");
        CleanerRiskAssessment result = _evaluator.Evaluate(
            rule,
            path,
            isLocked: false,
            modifyTime: DateTimeOffset.Now.AddYears(-1),
            sizeBytes: 1024);

        Assert.Equal(CleanerRiskLevel.Medium, result.RiskLevel);
        Assert.True(result.CanSelect);
    }
}
