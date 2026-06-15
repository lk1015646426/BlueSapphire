using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        private List<CleanerScanItem>? _lastScanResults;

        public AIToolsRegistry(
            DeepSeekAIService aiService, 
            CleanerScanService scanService, 
            CleanerExecutionService executionService, 
            CleanerAuditService auditService)
        {
            _aiService = aiService;
            _scanService = scanService;
            _executionService = executionService;
            _auditService = auditService;
        }

        public async Task<string> ExecuteToolCallAsync(string toolCallJson)
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
                        return await ExecuteCleanupAsync(args);
                    }
                    else if (name == "navigate_to_feature")
                    {
                        return await NavigateToFeatureAsync(args);
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

        private async Task<string> ExecuteCleanupAsync(string args)
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
    }
}
