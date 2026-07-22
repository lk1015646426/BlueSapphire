using System.Collections.Generic;
using BlueSapphire.Models;

namespace BlueSapphire.Interfaces;

/// <summary>
/// 领域工具向 AI 中控声明自身能力的协议。
/// 工具仍自行执行动作；中控只负责发现、编排和交给权限策略判断。
/// </summary>
public interface IAIToolCapabilityProvider
{
    string ToolId { get; }
    IReadOnlyList<AIToolCapabilityDefinition> GetCapabilities();
}
