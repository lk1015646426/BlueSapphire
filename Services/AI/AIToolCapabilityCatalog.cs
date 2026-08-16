using System;
using System.Collections.Generic;
using System.Linq;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;

namespace BlueSapphire.Services.AI;

/// <summary>
/// AI 中控的能力目录。
/// 目录是工具能力的单一运行时来源；它不负责执行能力，也不替代领域服务。
/// </summary>
public sealed class AIToolCapabilityCatalog
{
    private readonly object _gate = new();
    private Dictionary<string, AIToolCapabilityDefinition> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIToolCapabilityDefinition> _providerCapabilities =
        new(StringComparer.OrdinalIgnoreCase);

    public void Replace(IEnumerable<ChatTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var next = new Dictionary<string, AIToolCapabilityDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (ChatTool tool in tools)
        {
            ChatFunction function = tool.Function;
            if (string.IsNullOrWhiteSpace(function.Name))
            {
                continue;
            }

            AIToolCapabilityDefinition? providerDefinition = null;
            lock (_gate)
            {
                _providerCapabilities.TryGetValue(function.Name, out providerDefinition);
            }

            next[function.Name] = new AIToolCapabilityDefinition
            {
                // 旧版工具定义尚未全部迁移到各自 Tool 类，先用能力名称作为兼容归属。
                ToolId = providerDefinition?.ToolId ?? InferToolId(function.Name),
                Name = function.Name,
                Description = function.Description ?? string.Empty,
                Parameters = function.Parameters is System.Text.Json.Nodes.JsonNode node
                    ? node.DeepClone()
                    : null,
                RiskLevel = providerDefinition?.RiskLevel ?? InferRiskLevel(function.Name, function.Description),
                SupportsCancellation = providerDefinition?.SupportsCancellation == true ||
                                       function.Name.Contains("scan", StringComparison.OrdinalIgnoreCase),
                SupportsProgress = providerDefinition?.SupportsProgress == true ||
                                   function.Name.Contains("scan", StringComparison.OrdinalIgnoreCase),
                SupportsPreview = providerDefinition?.SupportsPreview == true ||
                                  function.Name.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                                  function.Name.Contains("plan", StringComparison.OrdinalIgnoreCase)
            };
        }

        // 保留由领域工具显式声明但当前模型列表尚未暴露的能力。
        KeyValuePair<string, AIToolCapabilityDefinition>[] providerSnapshot;
        lock (_gate)
        {
            providerSnapshot = _providerCapabilities.ToArray();
        }

        foreach ((string name, AIToolCapabilityDefinition capability) in providerSnapshot)
        {
            if (next.TryGetValue(name, out AIToolCapabilityDefinition? current))
            {
                // 旧版 ChatTool 定义保留模型所需的详细描述和参数；领域工具声明覆盖归属与风险元数据。
                next[name] = new AIToolCapabilityDefinition
                {
                    ToolId = capability.ToolId,
                    Name = current.Name,
                    Description = current.Description,
                    Parameters = current.Parameters?.DeepClone(),
                    RiskLevel = capability.RiskLevel,
                    SupportsCancellation = capability.SupportsCancellation || current.SupportsCancellation,
                    SupportsProgress = capability.SupportsProgress || current.SupportsProgress,
                    SupportsPreview = capability.SupportsPreview || current.SupportsPreview
                };
            }
            else
            {
                next[name] = capability.Clone();
            }
        }

        lock (_gate)
        {
            _capabilities = next;
        }
    }

    public void RegisterProvider(IAIToolCapabilityProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        RegisterRange(provider.GetCapabilities().Select(capability =>
        {
            AIToolCapabilityDefinition clone = capability.Clone();
            return new AIToolCapabilityDefinition
            {
                ToolId = string.IsNullOrWhiteSpace(clone.ToolId) ? provider.ToolId : clone.ToolId,
                Name = clone.Name,
                Description = clone.Description,
                Parameters = clone.Parameters,
                RiskLevel = clone.RiskLevel,
                SupportsCancellation = clone.SupportsCancellation,
                SupportsProgress = clone.SupportsProgress,
                SupportsPreview = clone.SupportsPreview
            };
        }));
    }

    public void RegisterRange(IEnumerable<AIToolCapabilityDefinition> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        lock (_gate)
        {
            foreach (AIToolCapabilityDefinition capability in capabilities)
            {
                if (!string.IsNullOrWhiteSpace(capability.Name))
                {
                    _providerCapabilities[capability.Name] = capability.Clone();
                    _capabilities[capability.Name] = capability.Clone();
                }
            }
        }
    }

    public IReadOnlyList<AIToolCapabilityDefinition> Snapshot()
    {
        lock (_gate)
        {
            return _capabilities.Values
                .Select(capability => capability.Clone())
                .OrderBy(capability => capability.ToolId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(capability => capability.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<ChatTool> BuildChatTools()
    {
        return Snapshot()
            .Select(capability => new ChatTool
            {
                Type = "function",
                Function = new ChatFunction
                {
                    Name = capability.Name,
                    Description = capability.Description,
                    Parameters = capability.Parameters?.DeepClone()
                }
            })
            .ToArray();
    }

    public string BuildSummary()
    {
        var capabilities = Snapshot();
        if (capabilities.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            capabilities.Select(capability =>
                $"- {capability.Name}（{capability.ToolId}，风险：{GetRiskText(capability.RiskLevel)}）：{capability.Description}"));
    }

    private static string InferToolId(string name)
    {
        if (name.StartsWith("analyze_media", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("preview_media", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("execute_media", StringComparison.OrdinalIgnoreCase))
        {
            return "MediaManager";
        }

        if (name.Contains("cleanup", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cleaner", StringComparison.OrdinalIgnoreCase))
        {
            return "CleanerAssistant";
        }

        if (name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("skill__", StringComparison.OrdinalIgnoreCase) ||
            name is "http_request" or "add_mcp_server" or "add_skill" or "handle_github_url")
        {
            return "External";
        }

        return "AICopilot";
    }

    private static AIToolRiskLevel InferRiskLevel(string name, string? description)
    {
        if (name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("skill__", StringComparison.OrdinalIgnoreCase) ||
            name is "http_request" or "add_mcp_server" or "add_skill" or "handle_github_url")
        {
            return AIToolRiskLevel.External;
        }

        string text = $"{name} {description}";
        if (text.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cleanup", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("install", StringComparison.OrdinalIgnoreCase))
        {
            return AIToolRiskLevel.Destructive;
        }

        if (text.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("confirmation", StringComparison.OrdinalIgnoreCase))
        {
            return AIToolRiskLevel.RequiresConfirmation;
        }

        return AIToolRiskLevel.ReadOnly;
    }

    private static string GetRiskText(AIToolRiskLevel level) => level switch
    {
        AIToolRiskLevel.ReadOnly => "只读",
        AIToolRiskLevel.RequiresConfirmation => "需要确认",
        AIToolRiskLevel.Destructive => "可能修改本地数据",
        AIToolRiskLevel.External => "第三方调用",
        _ => "未知"
    };
}
