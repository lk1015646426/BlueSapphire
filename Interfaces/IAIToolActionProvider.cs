namespace BlueSapphire.Interfaces;

using BlueSapphire.Services;

/// <summary>
/// 领域工具注册自身 AI 动作处理器的协议。
/// </summary>
public interface IAIToolActionProvider
{
    string ToolId { get; }
    void RegisterHandlers(AIToolActionHandlerRegistry registry);
}
