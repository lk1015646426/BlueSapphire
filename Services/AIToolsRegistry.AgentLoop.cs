using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BlueSapphire.Models;
using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services
{
    // Agent 循环分部：多轮工具调用编排、流式累积与隐私安全消息构建。
    public partial class AIToolsRegistry
    {
        public async Task<string> RunAgentLoopAsync(
            List<ChatMessage> messages, 
            IEnumerable<string> features,
            Action<string, string, bool> onMessageGenerated,
            Func<string, Task<bool>> requestConfirmation,
            CancellationToken cancellationToken = default)
        {
            var tools = await BuildCleanerToolsAsync(features);
            int maxRounds = 5;

            for (int round = 0; round < maxRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TrimMessageHistory(messages);
                List<ChatMessage> remoteMessages = BuildPrivacySafeMessages(messages);
                var stream = _aiService.SendChatStreamAsync(remoteMessages, tools, cancellationToken);

                var fullContentBuilder = new StringBuilder();
                var toolCallsAccumulator = new Dictionary<int, AccumulatingToolCall>();
                bool isFirstChunk = true;

                await foreach (var evt in stream)
                {
                    if (!string.IsNullOrEmpty(evt.ContentDelta))
                    {
                        if (fullContentBuilder.Length + evt.ContentDelta.Length > 200_000)
                        {
                            throw new InvalidOperationException("模型回复超过 200,000 字符限制，已停止处理。");
                        }
                        fullContentBuilder.Append(evt.ContentDelta);
                        onMessageGenerated("assistant", evt.ContentDelta, !isFirstChunk);
                        isFirstChunk = false;
                    }

                    if (evt.ToolCallFragments != null)
                    {
                        foreach (var frag in evt.ToolCallFragments)
                        {
                            if (frag.Index is < 0 or >= 8)
                            {
                                throw new InvalidOperationException("模型一次请求了过多工具。");
                            }
                            if (!toolCallsAccumulator.TryGetValue(frag.Index, out var acc))
                            {
                                acc = new AccumulatingToolCall();
                                toolCallsAccumulator[frag.Index] = acc;
                            }
                            if (frag.Id != null) acc.Id = frag.Id;
                            if (frag.Type != null) acc.Type = frag.Type;
                            if (frag.FunctionName != null) acc.FunctionName += frag.FunctionName;
                            if (frag.FunctionArgumentsDelta != null) acc.FunctionArguments += frag.FunctionArgumentsDelta;
                            if (acc.FunctionName.Length > 128 ||
                                acc.FunctionArguments.Length > 128 * 1024)
                            {
                                throw new InvalidOperationException("模型生成的工具名称或参数超过安全限制。");
                            }
                        }
                    }
                }

                string fullContent = fullContentBuilder.ToString();
                if (toolCallsAccumulator.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(fullContent))
                    {
                        lock (messages) { messages.Add(new ChatMessage { Role = "assistant", Content = fullContent }); }
                    }
                    return "OK";
                }

                var toolCallsList = toolCallsAccumulator.OrderBy(x => x.Key).Select(x => x.Value).ToList();
                foreach (AccumulatingToolCall toolCall in toolCallsList)
                {
                    if (string.IsNullOrWhiteSpace(toolCall.Id) ||
                        string.IsNullOrWhiteSpace(toolCall.FunctionName) ||
                        !Regex.IsMatch(toolCall.FunctionName, "^[A-Za-z0-9_-]{1,128}$"))
                    {
                        throw new InvalidOperationException("模型返回了无效的工具调用标识或名称。");
                    }
                }
                JsonElement toolCallsNode = JsonSerializer.SerializeToElement(
                    toolCallsList.Select(toolCall => new
                    {
                        id = toolCall.Id,
                        type = "function",
                        function = new
                        {
                            name = toolCall.FunctionName,
                            arguments = toolCall.FunctionArguments
                        }
                    }).ToArray());

                lock (messages)
                {
                    messages.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = fullContent,
                        ToolCalls = toolCallsNode
                    });
                }

                foreach (var acc in toolCallsList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onMessageGenerated("tool_progress", $"执行中: {acc.FunctionName}...", false);

                    string? handledResult = await _actionHandlers.TryExecuteAsync(
                        acc.FunctionName!,
                        acc.FunctionArguments,
                        new AIToolExecutionContext
                        {
                            RequestConfirmation = requestConfirmation,
                            CancellationToken = cancellationToken
                        });
                    string result;
                    if (handledResult != null)
                    {
                        result = handledResult;
                    }
                    else if (acc.FunctionName != null &&
                             _mcpToolRoutes.TryGetValue(acc.FunctionName, out var route))
                    {
                        if (await ConfirmRequiredActionAsync(
                                requestConfirmation,
                                $"即将调用第三方 MCP 工具：{route.ToolName}\n服务器：{route.ServerId}\n\n第三方工具可能读取或修改本机/远程数据，是否继续？"))
                        {
                            result = await _mcpServerManager.CallToolAsync(
                                route.ServerId,
                                route.ToolName,
                                acc.FunctionArguments,
                                cancellationToken);
                        }
                        else
                        {
                            result = "用户未授权调用第三方 MCP 工具。";
                        }
                    }
                    else if (acc.FunctionName != null && acc.FunctionName.StartsWith("skill__"))
                    {
                        if (await ConfirmRequiredActionAsync(
                                requestConfirmation,
                                $"即将调用第三方 Web API 技能：{acc.FunctionName}\n\n请求会向外部服务发送参数，是否继续？"))
                        {
                            result = await _webSkillManager.CallSkillAsync(
                                acc.FunctionName,
                                acc.FunctionArguments,
                                cancellationToken);
                        }
                        else
                        {
                            result = "用户未授权调用第三方 Web API 技能。";
                        }
                    }
                    else
                    {
                        result = $"未知操作: {acc.FunctionName}";
                    }

                    if (result.Length > 128 * 1024)
                    {
                        result = result[..(128 * 1024)] + "\n...[工具结果过长，已截断]";
                    }

                    lock (messages)
                    {
                        messages.Add(new ChatMessage
                        {
                            Role = "tool",
                            ToolCallId = acc.Id,
                            Content = result
                        });
                    }
                    onMessageGenerated("tool_result", $"已完成: {acc.FunctionName}", false);
                }
            }

            string err = "后台任务执行次数已达上限，系统已中断连续操作。";
            lock (messages) { messages.Add(new ChatMessage { Role = "assistant", Content = err }); }
            onMessageGenerated("assistant", err, false);
            return err;
        }

        private class AccumulatingToolCall
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "function";
            public string FunctionName { get; set; } = "";
            public string FunctionArguments { get; set; } = "";
        }
    }
}

