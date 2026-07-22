using System;
using System.Text.Json.Nodes;

namespace BlueSapphire.Models;

/// <summary>
/// AI 调用工具时的风险级别。风险级别是工具能力的一部分，不能只写在提示词里。
/// </summary>
public enum AIToolRiskLevel
{
    ReadOnly,
    RequiresConfirmation,
    Destructive,
    External
}

/// <summary>
/// 一个可被 AI 中控发现和调用的工具能力描述。
/// 业务执行仍由具体领域工具负责，此模型只描述能力契约。
/// </summary>
public sealed class AIToolCapabilityDefinition
{
    public string ToolId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public JsonNode? Parameters { get; init; }
    public AIToolRiskLevel RiskLevel { get; init; } = AIToolRiskLevel.ReadOnly;
    public bool SupportsCancellation { get; init; }
    public bool SupportsProgress { get; init; }
    public bool SupportsPreview { get; init; }

    public bool RequiresConfirmation =>
        RiskLevel is AIToolRiskLevel.RequiresConfirmation or
            AIToolRiskLevel.Destructive or
            AIToolRiskLevel.External;

    public AIToolCapabilityDefinition Clone() => new()
    {
        ToolId = ToolId,
        Name = Name,
        Description = Description,
        Parameters = Parameters?.DeepClone(),
        RiskLevel = RiskLevel,
        SupportsCancellation = SupportsCancellation,
        SupportsProgress = SupportsProgress,
        SupportsPreview = SupportsPreview
    };
}
