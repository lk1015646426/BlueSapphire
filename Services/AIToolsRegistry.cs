using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class AIToolsRegistry
    {
        private readonly DeepSeekAIService _aiService;
        private readonly CleanerScanService _scanService;
        private readonly CleanerExecutionService _executionService;
        private readonly CleanerAuditService _auditService;
        private readonly DevLogDataService _devLogDataService;
        private readonly AIMemoryService _memoryService;

        private List<CleanerScanItem>? _lastScanResults;

        public AIToolsRegistry(
            DeepSeekAIService aiService, 
            CleanerScanService scanService, 
            CleanerExecutionService executionService, 
            CleanerAuditService auditService,
            DevLogDataService devLogDataService,
            AIMemoryService memoryService)
        {
            _aiService = aiService;
            _scanService = scanService;
            _executionService = executionService;
            _auditService = auditService;
            _devLogDataService = devLogDataService;
            _memoryService = memoryService;
        }

        public async Task<ChatMessage> GetSystemPromptAsync(IEnumerable<string> features)
        {
            var systemPrompt = "你现在是“蓝宝石（BlueSapphire）”工具箱的智能助理。蓝宝石是一款 Windows 桌面效率软件，目前系统已安装的功能包括：\n";

            foreach (var feature in features)
            {
                systemPrompt += $"- 【{feature}】\n";
            }

            try
            {
                var drives = string.Join(", ", System.IO.DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name));
                systemPrompt += $"\n【可用本地磁盘】系统当前就绪的磁盘有：{drives}。";
            }
            catch { }

            systemPrompt += @"
【你的能力与工具】
1. start_smart_cleanup：扫描磁盘空间，获取安全可清理项 (Safe) 和需要确认的项 (Review)。你可以根据用户意图选择 scan_mode (Quick/Deep) 和 drives_to_scan。
2. execute_cleanup：执行实际的清理操作。你可以指定清理 categories_to_clean (例如 ['Safe', 'app_cache'])。
3. analyze_latest_cleanup_log：读取最近一次清理的日志。
4. navigate_to_feature：跳转到指定功能界面。
5. add_dev_log_record：自动帮用户生成并写入开发日志（发布记录）。当用户告诉你他们做了哪些开发、总结了什么内容时，你可以提取标题、版本号、级别等信息，调用此工具直接写入。

【核心交互流程与约束 - 必须严格遵守】
1. 需求理解：当用户表达磁盘空间不足时，先询问他们想扫描哪些盘，或者直接调用 start_smart_cleanup 进行默认扫描。
2. 报告结果：收到 start_smart_cleanup 的结果后，必须使用 Markdown 语法（如表格、加粗、Emoji、列表等）进行优美排版，向用户清晰展示各项详细体积与数量，并且用通俗易懂的语言简单解释这些垃圾文件是用来做什么的，删除它们会有什么好处。
3. 必须等待授权：汇报完毕后，你必须停下来，询问用户是否要执行清理。**绝对禁止**在用户未明确同意的情况下调用 execute_cleanup。
4. 结果反馈：收到 execute_cleanup 的结果后，向用户汇报释放的空间大小和失败情况。绝对不可伪造或虚构清理结果！

【安全红线】
- 绝不要猜测或虚构文件路径。
- 如果用户要求清理某些你认为属于 High 风险或用户数据的类别，必须发出警告并拒绝静默清理。
- 整个过程不跳转界面，全在对话框完成！请始终使用中文回复用户，态度专业、友善。";

            try
            {
                var rules = await _memoryService.GetMemoryRulesAsync();
                if (rules != null && rules.Count > 0)
                {
                    systemPrompt += "\n\n【用户长期记忆偏好规则】（最高优先级，在操作时必须严格遵守）：\n";
                    foreach (var rule in rules)
                    {
                        systemPrompt += $"- {rule}\n";
                    }
                }
            }
            catch { }

            return new ChatMessage { Role = "system", Content = systemPrompt };
        }

        public async Task<string> ExecuteToolCallAsync(string toolCallJson, Func<string, Task<bool>>? requestConfirmation = null)
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

                    if (name == "start_smart_cleanup")
                    {
                        return await StartSmartCleanupAsync(args);
                    }
                    else if (name == "analyze_latest_cleanup_log")
                    {
                        return await AnalyzeLatestCleanupLogAsync();
                    }
                    else if (name == "execute_cleanup")
                    {
                        return await ExecuteCleanupAsync(args, requestConfirmation);
                    }
                    else if (name == "navigate_to_feature")
                    {
                        return await NavigateToFeatureAsync(args);
                    }
                    else if (name == "add_dev_log_record")
                    {
                        return await AddDevLogRecordAsync(args);
                    }
                    else if (name == "remember_user_preference")
                    {
                        return await RememberUserPreferenceAsync(args);
                    }
                }
                return "未找到对应的指令。";
            }
            catch (Exception ex)
            {
                return $"执行指令失败: {ex.Message}";
            }
        }

        private async Task<string> StartSmartCleanupAsync(string args)
        {
            try
            {
                CleanerScanScope scope = CleanerScanScope.Quick;
                CleanerScanOptions options = new CleanerScanOptions();

                if (!string.IsNullOrWhiteSpace(args))
                {
                    try
                    {
                        var json = System.Text.Json.JsonDocument.Parse(args);
                        if (json.RootElement.TryGetProperty("scan_mode", out var modeProp))
                        {
                            if (modeProp.GetString()?.Equals("Deep", StringComparison.OrdinalIgnoreCase) == true)
                                scope = CleanerScanScope.Deep;
                        }

                        if (json.RootElement.TryGetProperty("drives_to_scan", out var drivesProp) && drivesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var drives = drivesProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                            if (drives.Contains("All", StringComparer.OrdinalIgnoreCase) || drives.Count == 0)
                            {
                                var allDrives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
                                options.AnalysisDriveRoots.AddRange(allDrives);
                            }
                            else
                            {
                                options.AnalysisDriveRoots.AddRange(drives!);
                            }
                        }
                    }
                    catch { }
                }

                var report = await _scanService.ScanAsync(scope, options, null, System.Threading.CancellationToken.None);
                _lastScanResults = report.Items.ToList();

                var safeItems = _lastScanResults.Where(x => x.RiskLevel == CleanerRiskLevel.Low).ToList();
                var reviewItems = _lastScanResults.Where(x => x.RiskLevel == CleanerRiskLevel.Medium).ToList();

                var result = new
                {
                    SafeItemsCount = safeItems.Count,
                    SafeItemsSize = CleanerSizeFormatter.Format(safeItems.Sum(x => x.SizeBytes)),
                    ReviewItemsCount = reviewItems.Count,
                    ReviewItemsSize = CleanerSizeFormatter.Format(reviewItems.Sum(x => x.SizeBytes)),
                    Details = new
                    {
                        SafeCategories = safeItems.GroupBy(x => x.Category).Select(g => new { Category = CleanerPresentation.ToCategoryText(g.Key), Count = g.Count(), Size = CleanerSizeFormatter.Format(g.Sum(x => x.SizeBytes)) }),
                        ReviewCategories = reviewItems.GroupBy(x => x.Category).Select(g => new { Category = CleanerPresentation.ToCategoryText(g.Key), Count = g.Count(), Size = CleanerSizeFormatter.Format(g.Sum(x => x.SizeBytes)) })
                    }
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            catch (Exception ex)
            {
                return $"扫描失败: {ex.Message}";
            }
        }

        private async Task<string> ExecuteCleanupAsync(string args, Func<string, Task<bool>>? requestConfirmation)
        {
            if (_lastScanResults == null || _lastScanResults.Count == 0)
            {
                return "错误：没有找到可清理的项目，请先执行扫描 (start_smart_cleanup)。";
            }

            try
            {
                var doc = JsonDocument.Parse(args);
                var targets = new List<string>();
                if (doc.RootElement.TryGetProperty("categories_to_clean", out var categoriesProp))
                {
                    foreach (var cat in categoriesProp.EnumerateArray())
                    {
                        targets.Add(cat.GetString()!);
                    }
                }

                var itemsToClean = new List<CleanerScanItem>();
                
                foreach (var item in _lastScanResults)
                {
                    if (targets.Contains("Safe") && item.RiskLevel == CleanerRiskLevel.Low)
                    {
                        itemsToClean.Add(item);
                    }
                    else if (targets.Contains("Review") && item.RiskLevel == CleanerRiskLevel.Medium)
                    {
                        itemsToClean.Add(item);
                    }
                    else if (targets.Contains("All") && (item.RiskLevel == CleanerRiskLevel.Low || item.RiskLevel == CleanerRiskLevel.Medium))
                    {
                        itemsToClean.Add(item);
                    }
                    else if (targets.Contains(item.Category) || targets.Contains(CleanerPresentation.ToCategoryText(item.Category)))
                    {
                        itemsToClean.Add(item);
                    }
                }

                if (itemsToClean.Count == 0)
                {
                    return "未匹配到需要清理的项目。传入的 categories_to_clean 参数未命中任何扫描结果。可用参数：'Safe', 'Review', 'All' 或具体的类别名称。";
                }

                if (requestConfirmation != null)
                {
                    long totalSize = itemsToClean.Sum(x => x.SizeBytes);
                    bool confirmed = await requestConfirmation($"即将清理 {itemsToClean.Count} 个项目（预计释放 {CleanerSizeFormatter.Format(totalSize)}），是否继续？");
                    if (!confirmed)
                    {
                        return "用户在安全确认弹窗中拒绝了本次清理操作。请告知用户清理已取消。";
                    }
                }

                var batch = await _executionService.ExecuteAsync(itemsToClean, CleanerScanScope.Quick, null, System.Threading.CancellationToken.None);
                await _auditService.RecordCleanupAsync(batch, 0);

                var failedEntries = batch.Entries.Where(e => !string.Equals(e.Status, "Completed", StringComparison.OrdinalIgnoreCase)).ToList();
                var result = new
                {
                    TotalProcessed = batch.Entries.Count,
                    CleanedCount = batch.CompletedCount,
                    FailedCount = batch.FailedCount,
                    ReleasedSize = CleanerSizeFormatter.Format(batch.ReleasedBytes),
                    FailedDetails = failedEntries.Select(e => new { Name = e.ItemName, Error = e.ErrorMessage, Reason = CleanerPresentation.ToFailureReasonText(e.FailureReason) }).Take(5).ToList()
                };

                return $"清理完成。结果：\n{JsonSerializer.Serialize(result, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}";
            }
            catch (Exception ex)
            {
                return $"清理执行失败: {ex.Message}";
            }
        }

        private async Task<string> AnalyzeLatestCleanupLogAsync()
        {
            try
            {
                var auditDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire", "Audits");
                if (!Directory.Exists(auditDir)) return "尚未发现任何清理记录。";

                var files = Directory.GetFiles(auditDir, "cleanup-*.json");
                if (files.Length == 0) return "尚未发现任何清理记录。";

                var latestFile = files.OrderByDescending(f => f).First();
                var json = await File.ReadAllTextAsync(latestFile);
                
                return $"[LOG_DATA] {json}";
            }
            catch (Exception ex)
            {
                return $"读取日志失败: {ex.Message}";
            }
        }

        private async Task<string> AddDevLogRecordAsync(string args)
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

        private async Task<string> RememberUserPreferenceAsync(string args)
        {
            try
            {
                var doc = JsonDocument.Parse(args);
                string rule = doc.RootElement.GetProperty("rule").GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(rule))
                {
                    await _memoryService.AddMemoryRuleAsync(rule);
                    return $"已成功记住该偏好规则：{rule}。它将在未来的对话和操作中自动生效。";
                }
                return "错误：规则内容为空。";
            }
            catch (Exception ex)
            {
                return $"保存记忆失败: {ex.Message}";
            }
        }

        public static List<ChatTool> BuildCleanerTools(IEnumerable<string> features)
        {
            var featureEnum = features.ToList();
            featureEnum.Add("Settings");

            return new List<ChatTool>
            {
                new ChatTool
                {
                    Type = "function",
                    Function = new ChatFunction
                    {
                        Name = "start_smart_cleanup",
                        Description = "Starts the smart system cleanup process. If the user explicitly specifies the drives (e.g. 'all drives', 'C drive'), call this tool immediately. If they do NOT specify any drives, you MUST ask them which drives they want to scan before calling this.",
                        Parameters = JsonSerializer.SerializeToNode(new
                        {
                            type = "object",
                            properties = new
                            {
                                scan_mode = new
                                {
                                    type = "string",
                                    description = "The mode of scanning. 'Quick' for fast scanning of common junk, 'Deep' for full disk deep scan of large files. Default is 'Deep' if specific drives are given.",
                                    @enum = new[] { "Quick", "Deep" }
                                },
                                drives_to_scan = new
                                {
                                    type = "array",
                                    items = new { type = "string" },
                                    description = "List of drive roots to scan, e.g. [\"C:\\\", \"D:\\\"]. Pass [\"All\"] to scan all available drives."
                                }
                            },
                            required = new[] { "scan_mode", "drives_to_scan" }
                        })
                    }
                },
                new ChatTool
                {
                    Type = "function",
                    Function = new ChatFunction
                    {
                        Name = "execute_cleanup",
                        Description = "Executes the cleanup process to free up space. Use this ONLY AFTER the user has explicitly confirmed what to clean from the scan results.",
                        Parameters = JsonSerializer.SerializeToNode(new
                        {
                            type = "object",
                            properties = new
                            {
                                categories_to_clean = new
                                {
                                    type = "array",
                                    items = new { type = "string" },
                                    description = "The categories or RiskLevels to clean, e.g. ['Safe'], ['Review'], or specific rule names."
                                }
                            },
                            required = new[] { "categories_to_clean" }
                        })
                    }
                },
                new ChatTool
                {
                    Type = "function",
                    Function = new ChatFunction
                    {
                        Name = "analyze_latest_cleanup_log",
                        Description = "Reads the latest cleanup audit log and returns its JSON content. Use this to analyze what was cleaned up and explain it to the user."
                    }
                },
                new ChatTool
                {
                    Type = "function",
                    Function = new ChatFunction
                    {
                        Name = "navigate_to_feature",
                        Description = "Navigates the UI to a specific feature page. Use this when the user asks to open a specific tool.",
                        Parameters = JsonSerializer.SerializeToNode(new
                        {
                            type = "object",
                            properties = new
                            {
                                feature = new
                                {
                                    type = "string",
                                    @enum = featureEnum.ToArray()
                                }
                            },
                            required = new[] { "feature" }
                        })
                    }
                },
                new ChatTool
                {
                    Type = "function",
                    Function = new ChatFunction
                    {
                        Name = "add_dev_log_record",
                        Description = "Automatically generates and saves a development log entry based on the user's summary of their work. Use this when the user describes what they've developed or asks to record a dev log.",
                        Parameters = JsonSerializer.SerializeToNode(new
                        {
                            type = "object",
                            properties = new
                            {
                                title = new { type = "string", description = "A short and concise title for the update." },
                                version = new { type = "string", description = "The version number, e.g. '1.0.6'. If the user doesn't provide one, default to '1.0.0' or ask them." },
                                level = new { type = "string", description = "The update level. Must be one of: '常规迭代' (Regular iteration), '核心跃迁' (Major feature/update), '漏洞修复' (Bug fixes). Default is '常规迭代'.", @enum = new[] { "常规迭代", "核心跃迁", "漏洞修复" } },
                                summary = new { type = "string", description = "A brief 1-2 sentence summary of the update." },
                                fullContent = new { type = "string", description = "The full, detailed release notes formatted in Markdown. Can include bullet points." }
                            },
                            required = new[] { "title", "version", "level", "summary", "fullContent" }
                        })
                    }
                },
                new ChatTool
                {
                    Type = "function",
                    Function = new ChatFunction
                    {
                        Name = "remember_user_preference",
                        Description = "Extracts and saves a long-term memory rule based on the user's instructions or preferences. Call this tool when the user tells you to remember something, or expresses a strong preference (e.g., 'Never clean .mp4 files', 'Always use deep scan').",
                        Parameters = JsonSerializer.SerializeToNode(new
                        {
                            type = "object",
                            properties = new
                            {
                                rule = new { type = "string", description = "A concise, actionable rule that captures the user's preference. Keep it short but specific, e.g., '不清理 .mp4 格式文件' or '习惯使用深度扫描'." }
                            },
                            required = new[] { "rule" }
                        })
                    }
                }
            };
        }

        public async Task<string> RunAgentLoopAsync(
            List<ChatMessage> messages, 
            IEnumerable<string> features,
            Action<string, string, bool> onMessageGenerated,
            Func<string, Task<bool>> requestConfirmation)
        {
            var tools = BuildCleanerTools(features);
            int maxRounds = 5;

            for (int round = 0; round < maxRounds; round++)
            {
                var stream = _aiService.SendChatStreamAsync(messages, tools);

                string fullContent = "";
                var toolCallsAccumulator = new Dictionary<int, AccumulatingToolCall>();
                bool isFirstChunk = true;

                await foreach (var evt in stream)
                {
                    if (!string.IsNullOrEmpty(evt.ContentDelta))
                    {
                        fullContent += evt.ContentDelta;
                        onMessageGenerated("assistant", evt.ContentDelta, !isFirstChunk);
                        isFirstChunk = false;
                    }

                    if (evt.ToolCallFragments != null)
                    {
                        foreach (var frag in evt.ToolCallFragments)
                        {
                            if (!toolCallsAccumulator.TryGetValue(frag.Index, out var acc))
                            {
                                acc = new AccumulatingToolCall();
                                toolCallsAccumulator[frag.Index] = acc;
                            }
                            if (frag.Id != null) acc.Id = frag.Id;
                            if (frag.Type != null) acc.Type = frag.Type;
                            if (frag.FunctionName != null) acc.FunctionName += frag.FunctionName;
                            if (frag.FunctionArgumentsDelta != null) acc.FunctionArguments += frag.FunctionArgumentsDelta;
                        }
                    }
                }

                if (toolCallsAccumulator.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(fullContent))
                    {
                        messages.Add(new ChatMessage { Role = "assistant", Content = fullContent });
                    }
                    return "OK";
                }

                var toolCallsArrayJson = "[";
                var toolCallsList = toolCallsAccumulator.OrderBy(x => x.Key).Select(x => x.Value).ToList();
                for (int i = 0; i < toolCallsList.Count; i++)
                {
                    var acc = toolCallsList[i];
                    toolCallsArrayJson += $"{{\"id\":\"{acc.Id}\",\"type\":\"{acc.Type}\",\"function\":{{\"name\":\"{acc.FunctionName}\",\"arguments\":{JsonSerializer.Serialize(acc.FunctionArguments)}}}}}";
                    if (i < toolCallsList.Count - 1) toolCallsArrayJson += ",";
                }
                toolCallsArrayJson += "]";

                var toolCallsNode = JsonDocument.Parse(toolCallsArrayJson).RootElement;

                messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = fullContent,
                    ToolCalls = toolCallsNode
                });

                foreach (var acc in toolCallsList)
                {
                    onMessageGenerated("tool_progress", $"执行中: {acc.FunctionName}...", false);

                    string result = acc.FunctionName switch
                    {
                        "start_smart_cleanup" => await StartSmartCleanupAsync(acc.FunctionArguments),
                        "execute_cleanup" => await ExecuteCleanupAsync(acc.FunctionArguments, requestConfirmation),
                        "analyze_latest_cleanup_log" => await AnalyzeLatestCleanupLogAsync(),
                        "navigate_to_feature" => await NavigateToFeatureAsync(acc.FunctionArguments),
                        "add_dev_log_record" => await AddDevLogRecordAsync(acc.FunctionArguments),
                        "remember_user_preference" => await RememberUserPreferenceAsync(acc.FunctionArguments),
                        _ => $"未知操作: {acc.FunctionName}"
                    };

                    messages.Add(new ChatMessage
                    {
                        Role = "tool",
                        ToolCallId = acc.Id,
                        Content = result
                    });
                }
            }

            string err = "后台任务执行次数已达上限，系统已中断连续操作。";
            messages.Add(new ChatMessage { Role = "assistant", Content = err });
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