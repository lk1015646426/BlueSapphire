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

namespace BlueSapphire.Services.AI
{
    // 主分部：依赖注入、字段、工具调用分发与内置处理器注册。
    // 系统提示词见 AIToolsRegistry.SystemPrompt.cs；网络抓取与安装见 AIToolsRegistry.WebFetch.cs；
    // 工具目录构建见 AIToolsRegistry.ToolCatalog.cs；Agent 循环见 AIToolsRegistry.AgentLoop.cs。
    public partial class AIToolsRegistry
    {
        private readonly DeepSeekAIService _aiService;
        private readonly DevLogDataService _devLogDataService;
        private readonly AIMemoryService _memoryService;
        private readonly McpServerManager _mcpServerManager;
        private readonly WebSkillManager _webSkillManager;
        private readonly AgentSkillManager _agentSkillManager;
        private readonly AISharedContextService _sharedContext;
        private readonly AIPrivacyService _privacyService;
        private readonly AIDiagnosticsService _diagnosticsService;
        private readonly AIInsightService _insightService;
        private readonly AIOperationPolicyService _operationPolicy;
        private readonly AIToolCapabilityCatalog _capabilityCatalog;
        private readonly AIToolActionHandlerRegistry _actionHandlers = new();
        private readonly ConcurrentDictionary<string, (string ServerId, string ToolName)> _mcpToolRoutes =
            new(StringComparer.Ordinal);
        private readonly ILogger<AIToolsRegistry>? _logger;

        public AIToolsRegistry(
            DeepSeekAIService aiService,
            DevLogDataService devLogDataService,
            AIMemoryService memoryService,
            McpServerManager mcpServerManager,
            WebSkillManager webSkillManager,
            AgentSkillManager agentSkillManager,
            AISharedContextService sharedContext,
            AIPrivacyService privacyService,
            AIDiagnosticsService diagnosticsService,
            AIInsightService insightService,
            AIOperationPolicyService operationPolicy,
            AIToolCapabilityCatalog capabilityCatalog,
            IEnumerable<IAIToolCapabilityProvider> capabilityProviders,
            IEnumerable<IAIToolActionProvider> actionProviders,
            ILogger<AIToolsRegistry>? logger = null)
        {
            _aiService = aiService;
            _devLogDataService = devLogDataService;
            _memoryService = memoryService;
            _mcpServerManager = mcpServerManager;
            _webSkillManager = webSkillManager;
            _agentSkillManager = agentSkillManager;
            _sharedContext = sharedContext;
            _privacyService = privacyService;
            _diagnosticsService = diagnosticsService;
            _insightService = insightService;
            _operationPolicy = operationPolicy;
            _capabilityCatalog = capabilityCatalog;
            _logger = logger;
            foreach (IAIToolCapabilityProvider provider in capabilityProviders)
            {
                _capabilityCatalog.RegisterProvider(provider);
            }
            RegisterBuiltInActionHandlers();
            foreach (IAIToolActionProvider provider in actionProviders)
            {
                provider.RegisterHandlers(_actionHandlers);
            }
        }

        private static readonly System.Net.Http.HttpClient _directClient = CreateHttpClient(false);
        private static System.Net.Http.HttpClient? _proxyClient;
        private static int? _cachedProxyPort = -1;

        private static System.Net.Http.HttpClient CreateHttpClient(bool useProxy, int? proxyPort = null)
        {
            var handler = new System.Net.Http.HttpClientHandler();
            handler.AllowAutoRedirect = false;
            if (useProxy && proxyPort.HasValue)
            {
                handler.Proxy = new System.Net.WebProxy($"http://127.0.0.1:{proxyPort.Value}");
                handler.UseProxy = true;
            }
            else if (!useProxy)
            {
                handler.UseProxy = false;
            }
            var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BlueSapphire-AI");
            return client;
        }

        private System.Net.Http.HttpClient GetHttpClient(bool useProxy)
        {
            if (!useProxy) return _directClient;
            int? currentPort = GetActiveProxyPort();
            if (_proxyClient == null || _cachedProxyPort != currentPort)
            {
                _cachedProxyPort = currentPort;
                _proxyClient = CreateHttpClient(true, currentPort);
            }
            return _proxyClient;
        }

        public async Task<string> ExecuteToolCallAsync(
            string toolCallJson,
            Func<string, Task<bool>>? requestConfirmation = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var doc = JsonDocument.Parse(toolCallJson);
                var calls = doc.RootElement.EnumerateArray();
                
                foreach (var call in calls)
                {
                    var function = call.GetProperty("function");
                    var name = function.GetProperty("name").GetString();
                    var args = function.GetProperty("arguments").GetString() ?? "{}";
                    cancellationToken.ThrowIfCancellationRequested();
                    if (args.Length > 128 * 1024)
                    {
                        return "安全拦截：工具参数超过 128 KB 限制。";
                    }

                    if (name != null)
                    {
                        string? handledResult = await _actionHandlers.TryExecuteAsync(
                            name,
                            args,
                            new AIToolExecutionContext
                            {
                                RequestConfirmation = requestConfirmation,
                                CancellationToken = cancellationToken
                            });
                        if (handledResult != null)
                        {
                            return handledResult;
                        }
                    }

                    if (name == "navigate_to_feature")
                    {
                        return await NavigateToFeatureAsync(args);
                    }
                    else if (name == "add_dev_log_record")
                    {
                        return await AddDevLogRecordAsync(args, requestConfirmation);
                    }
                    else if (name == "remember_user_preference")
                    {
                        return await RememberUserPreferenceAsync(args, requestConfirmation);
                    }
                    else if (name == "add_mcp_server")
                    {
                        return await AddMcpServerAsync(
                            args,
                            requestConfirmation,
                            cancellationToken);
                    }
                    else if (name == "handle_github_url")
                    {
                        return await HandleGithubUrlAsync(
                            args,
                            requestConfirmation,
                            cancellationToken);
                    }
                    else if (name == "add_skill")
                    {
                        return await AddSkillAsync(
                            args,
                            requestConfirmation,
                            cancellationToken);
                    }
                    else if (name == "http_request")
                    {
                        return await HttpRequestAsync(args, requestConfirmation, cancellationToken);
                    }
                    else if (name != null && _mcpToolRoutes.TryGetValue(name, out var route))
                    {
                        if (!await ConfirmRequiredActionAsync(
                                requestConfirmation,
                                $"即将调用第三方 MCP 工具：{route.ToolName}\n服务器：{route.ServerId}\n\n第三方工具可能读取或修改本机/远程数据，是否继续？"))
                        {
                            return "用户未授权调用第三方 MCP 工具。";
                        }
                        return await _mcpServerManager.CallToolAsync(
                            route.ServerId,
                            route.ToolName,
                            args,
                            cancellationToken);
                    }
                    else if (name != null && name.StartsWith("skill__"))
                    {
                        if (!await ConfirmRequiredActionAsync(
                                requestConfirmation,
                                $"即将调用第三方 Web API 技能：{name}\n\n请求会向外部服务发送参数，是否继续？"))
                        {
                            return "用户未授权调用第三方 Web API 技能。";
                        }
                        return await _webSkillManager.CallSkillAsync(name, args, cancellationToken);
                    }
                    else
                    {
                        return "未找到对应的指令。";
                    }
                }
                return "未找到对应的指令。";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"执行指令失败: {ex.Message}";
            }
        }

        private void RegisterBuiltInActionHandlers()
        {
            _actionHandlers.Register(
                "navigate_to_feature",
                (args, _) => NavigateToFeatureAsync(args));
            _actionHandlers.Register(
                "add_dev_log_record",
                (args, context) => AddDevLogRecordAsync(args, context.RequestConfirmation));
            _actionHandlers.Register(
                "remember_user_preference",
                (args, context) => RememberUserPreferenceAsync(args, context.RequestConfirmation));
            _actionHandlers.Register(
                "add_mcp_server",
                (args, context) => AddMcpServerAsync(
                    args,
                    context.RequestConfirmation,
                    context.CancellationToken));
            _actionHandlers.Register(
                "handle_github_url",
                (args, context) => HandleGithubUrlAsync(
                    args,
                    context.RequestConfirmation,
                    context.CancellationToken));
            _actionHandlers.Register(
                "add_skill",
                (args, context) => AddSkillAsync(
                    args,
                    context.RequestConfirmation,
                    context.CancellationToken));
            _actionHandlers.Register(
                "http_request",
                (args, context) => HttpRequestAsync(
                    args,
                    context.RequestConfirmation,
                    context.CancellationToken));
            _actionHandlers.Register(
                "diagnose_application",
                (_, _) => DiagnoseApplicationAsync());
            _actionHandlers.Register(
                "build_cross_module_plan",
                (args, _) => Task.FromResult(BuildCrossModulePlan(args)));
            _actionHandlers.Register(
                "get_proactive_suggestions",
                (_, _) => GetProactiveSuggestionsAsync());
        }


        private async Task<string> AddDevLogRecordAsync(string args, Func<string, Task<bool>>? requestConfirmation)
        {
            try
            {
                var doc = JsonDocument.Parse(args);
                var root = doc.RootElement;

                string title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "更新记录" : "更新记录";
                string version = root.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "1.0.0" : "1.0.0";
                string level = root.TryGetProperty("level", out var lProp) ? lProp.GetString() ?? "常规迭代" : "常规迭代";
                string summary = root.TryGetProperty("summary", out var sProp) ? sProp.GetString() ?? "修复了一些问题并提升了体验。" : "修复了一些问题并提升了体验。";
                string fullContent = root.TryGetProperty("fullContent", out var fProp) ? fProp.GetString() ?? summary : summary;
                if (!_devLogDataService.CanWrite)
                {
                    return "当前为发布环境，开发日志是只读内容，不能写入。";
                }
                if (!await ConfirmRequiredActionAsync(
                        requestConfirmation,
                        $"即将写入开发日志：\n版本：{version}\n标题：{title}\n\n是否保存？"))
                {
                    return "用户已取消写入开发日志。";
                }

                var newItem = new DevLogItem
                {
                    Title = title,
                    Version = version,
                    UpdateLevel = level,
                    Description = summary,
                    FullContent = fullContent,
                    Status = DevLogStatus.Completed,
                    Timestamp = DateTime.Now
                };

                var logs = await _devLogDataService.LoadLogsAsync();
                logs.Insert(0, newItem);
                await _devLogDataService.SaveLogsAsync(logs);

                return $"已成功为您生成并保存版本 {version} 的开发日志！您可以前往“开发日志与版本记录”页面查看。如果页面已经打开，可能需要重新进入以刷新数据。";
            }
            catch (Exception ex)
            {
                return $"生成开发日志失败: {ex.Message}";
            }
        }

        private async Task<string> NavigateToFeatureAsync(string args)
        {
            var doc = JsonDocument.Parse(args);
            string feature = doc.RootElement.GetProperty("feature").GetString() ?? "";

            if (App.CurrentWindow is MainWindow mainWindow)
            {
                if (!string.IsNullOrEmpty(feature))
                {
                    mainWindow.NavigateToTool(feature);
                    return $"已为你跳转到 {feature} 界面。";
                }
            }
            return $"无法找到功能：{feature}。";
        }

        private async Task<string> RememberUserPreferenceAsync(string args, Func<string, Task<bool>>? requestConfirmation)
        {
            try
            {
                var doc = JsonDocument.Parse(args);
                string rule = doc.RootElement.GetProperty("rule").GetString() ?? "";
                string scopeText = doc.RootElement.TryGetProperty("scope", out JsonElement scopeProperty)
                    ? scopeProperty.GetString() ?? "Global"
                    : "Global";
                AIMemoryScope scope = Enum.TryParse(scopeText, ignoreCase: true, out AIMemoryScope parsedScope)
                    ? parsedScope
                    : AIMemoryScope.Global;
                int expiresDays = doc.RootElement.TryGetProperty("expires_days", out JsonElement expiryProperty) &&
                                  expiryProperty.TryGetInt32(out int parsedDays)
                    ? Math.Clamp(parsedDays, 0, 3650)
                    : 0;
                DateTimeOffset? expiresAt = expiresDays > 0
                    ? DateTimeOffset.Now.AddDays(expiresDays)
                    : null;

                if (!string.IsNullOrWhiteSpace(rule))
                {
                    if (!await ConfirmRequiredActionAsync(
                            requestConfirmation,
                            $"即将把以下内容保存为长期偏好：\n\n{rule}\n\n是否保存？"))
                    {
                        return "用户已取消保存长期偏好。";
                    }
                    bool added = await _memoryService.AddMemoryEntryAsync(
                        rule,
                        scope,
                        expiresAt,
                        "AI 建议并经用户确认");
                    return added
                        ? $"已保存长期偏好：{rule}。它只用于表达方式和非安全习惯，不会替代任何操作确认。"
                        : "这条长期偏好已经存在，无需重复保存。";
                }
                return "错误：规则内容为空。";
            }
            catch (Exception ex)
            {
                return $"保存记忆失败: {ex.Message}";
            }
        }

        private async Task<string> DiagnoseApplicationAsync()
        {
            return await _diagnosticsService.BuildDiagnosticSummaryAsync();
        }

        private string BuildCrossModulePlan(string args)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(args);
                JsonElement root = document.RootElement;
                string objective = root.GetProperty("objective").GetString() ?? "整理空间与媒体";
                string? folderPath = root.TryGetProperty("folder_path", out JsonElement folderProperty)
                    ? folderProperty.GetString()
                    : null;
                return _insightService.BuildCrossModulePlan(
                    objective,
                    _privacyService.DescribePathWithoutIdentity(folderPath));
            }
            catch (Exception ex)
            {
                return $"生成跨模块计划失败：{_privacyService.RedactForRemoteModel(ex.Message)}";
            }
        }

        private async Task<string> GetProactiveSuggestionsAsync()
        {
            IReadOnlyList<string> suggestions = await _insightService.BuildNonIntrusiveSuggestionsAsync();
            return string.Join(Environment.NewLine, suggestions.Select(item => $"- {item}"));
        }

        private List<ChatMessage> BuildPrivacySafeMessages(List<ChatMessage> messages)
        {
            lock (messages)
            {
                return messages.Select(message => new ChatMessage
                {
                    Role = message.Role,
                    Content = message.Content == null
                        ? null
                        : _privacyService.RedactForRemoteModel(message.Content),
                    Name = message.Name,
                    ToolCalls = message.ToolCalls,
                    ToolCallId = message.ToolCallId
                }).ToList();
            }
        }

        private static void TrimMessageHistory(List<ChatMessage> messages)
        {
            const int maxContextMessages = 32;
            const int maxContextCharacters = 60_000;
            lock (messages)
            {
                ChatMessage? systemMessage = messages.FirstOrDefault(message =>
                    string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase));
                List<ChatMessage> conversation = messages
                    .Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                while (conversation.Count > maxContextMessages ||
                       conversation.Sum(message => message.Content?.Length ?? 0) > maxContextCharacters)
                {
                    conversation.RemoveAt(0);
                }

                while (conversation.Count > 0 &&
                       !string.Equals(conversation[0].Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    conversation.RemoveAt(0);
                }

                messages.Clear();
                if (systemMessage != null)
                {
                    messages.Add(systemMessage);
                }
                messages.AddRange(conversation);
            }
        }
    }
}
