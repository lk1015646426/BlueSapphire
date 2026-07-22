using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;

namespace BlueSapphire.Services;

/// <summary>
/// 清理助手拥有的 AI 动作。所有删除、风险判断、隔离和审计仍由清理领域服务负责。
/// </summary>
public sealed class CleanerAIToolActionProvider : IAIToolActionProvider
{
    private readonly CleanerScanService _scanService;
    private readonly CleanerDeepScanService _deepScanService;
    private readonly CleanerExecutionService _executionService;
    private readonly CleanerAuditService _auditService;
    private readonly AITaskCenterService _taskCenter;
    private readonly AISharedContextService _sharedContext;
    private readonly AIPrivacyService _privacyService;
    private readonly AIOperationPolicyService _operationPolicy;
    private readonly AICleanerRuleDraftService _ruleDraftService;
    private readonly CleanerOperationCoordinator _operationCoordinator;
    private readonly CleanerStateStore? _stateStore;

    public CleanerAIToolActionProvider(
        CleanerScanService scanService,
        CleanerDeepScanService deepScanService,
        CleanerExecutionService executionService,
        CleanerAuditService auditService,
        AITaskCenterService taskCenter,
        AISharedContextService sharedContext,
        AIPrivacyService privacyService,
        AIOperationPolicyService operationPolicy,
        AICleanerRuleDraftService ruleDraftService,
        CleanerOperationCoordinator? operationCoordinator = null,
        CleanerStateStore? stateStore = null)
    {
        _scanService = scanService;
        _deepScanService = deepScanService;
        _executionService = executionService;
        _auditService = auditService;
        _taskCenter = taskCenter;
        _sharedContext = sharedContext;
        _privacyService = privacyService;
        _operationPolicy = operationPolicy;
        _ruleDraftService = ruleDraftService;
        _operationCoordinator = operationCoordinator ?? new CleanerOperationCoordinator();
        _stateStore = stateStore;
    }

    public string ToolId => "CleanerAssistant";

    public void RegisterHandlers(AIToolActionHandlerRegistry registry)
    {
        registry.Register("start_smart_cleanup", (args, context) => StartSmartCleanupAsync(args, context.CancellationToken));
        registry.Register("analyze_latest_cleanup_log", (_, _) => AnalyzeLatestCleanupLogAsync());
        registry.Register(
            "execute_cleanup",
            (args, context) => ExecuteCleanupAsync(
                args,
                context.RequestConfirmation,
                context.CancellationToken));
        registry.Register(
            "create_cleaner_rule_draft",
            (args, context) => CreateCleanerRuleDraftAsync(args, context.RequestConfirmation));
    }

    private async Task<string> StartSmartCleanupAsync(string args, CancellationToken cancellationToken)
    {
        AITaskLease? task = null;
        CleanerOperationLease? operationLease = null;
        try
        {
            CleanerScanScope scope = CleanerScanScope.Quick;
            CleanerScanOptions options = new();
            if (!string.IsNullOrWhiteSpace(args))
            {
                try
                {
                    using JsonDocument json = JsonDocument.Parse(args);
                    if (json.RootElement.TryGetProperty("scan_mode", out JsonElement modeProperty) &&
                        modeProperty.GetString()?.Equals("Deep", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        scope = CleanerScanScope.Deep;
                    }

                    if (json.RootElement.TryGetProperty("drives_to_scan", out JsonElement drivesProperty) &&
                        drivesProperty.ValueKind == JsonValueKind.Array)
                    {
                        List<string?> drives = drivesProperty.EnumerateArray()
                            .Select(item => item.GetString())
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .ToList();
                        if (drives.Contains("All", StringComparer.OrdinalIgnoreCase) || drives.Count == 0)
                        {
                            options.AnalysisDriveRoots.AddRange(
                                DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => drive.Name));
                        }
                        else
                        {
                            options.AnalysisDriveRoots.AddRange(drives!);
                        }
                    }
                }
                catch
                {
                    // 参数异常沿用旧行为，使用默认扫描范围。
                }
            }

            if (options.AnalysisDriveRoots.Count > 0)
            {
                IReadOnlyList<string> validatedRoots =
                    _operationPolicy.ValidateDriveRoots(options.AnalysisDriveRoots);
                options.AnalysisDriveRoots.Clear();
                options.AnalysisDriveRoots.AddRange(validatedRoots);
            }

            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.AiScan, out operationLease))
            {
                return "清理助手正在执行另一项扫描、清理或恢复任务，请稍后再试。";
            }

            string driveSummary = options.AnalysisDriveRoots.Count == 0
                ? "默认磁盘范围"
                : string.Join("、", options.AnalysisDriveRoots.Select(_privacyService.DescribePathWithoutIdentity));
            task = _taskCenter.Begin(
                "cleaner.scan",
                scope == CleanerScanScope.Deep ? "AI 深度扫描" : "AI 快速扫描",
                $"扫描范围：{driveSummary}",
                $"cleaner.scan:{scope}:{string.Join("|", options.AnalysisDriveRoots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))}");
            if (task.IsDuplicate)
            {
                AITaskRecord? existing = _taskCenter.Get(task.TaskId);
                return existing?.IsActive == true
                    ? $"相同的扫描任务正在执行中，任务编号：{task.TaskId}。"
                    : $"相同扫描刚刚完成，可直接使用最近扫描结果。任务编号：{task.TaskId}。";
            }

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                task.Token);
            var progress = new Progress<CleanerScanProgress>(value =>
            {
                double percent = value.ProgressMax > 0
                    ? value.ProgressValue / value.ProgressMax * 100
                    : 0;
                _taskCenter.Report(task.TaskId, percent, value.StageTitle, value.Detail);
            });

            CleanerScanReport report;
            if (scope == CleanerScanScope.Deep)
            {
                CleanerDeepScanResult deepResult = await _deepScanService.ScanAsync(
                    options,
                    progress,
                    linkedCts.Token);
                report = deepResult.Report;
            }
            else
            {
                report = await _scanService.ScanAsync(
                    scope,
                    options,
                    progress,
                    linkedCts.Token);
            }

            _sharedContext.SetCleanerScan(report);
            List<CleanerScanItem> scanResults = report.Items.ToList();
            List<CleanerScanItem> safeItems = scanResults.Where(item => item.RiskLevel == CleanerRiskLevel.Low).ToList();
            List<CleanerScanItem> reviewItems = scanResults.Where(item => item.RiskLevel == CleanerRiskLevel.Medium).ToList();
            var result = new
            {
                SafeItemsCount = safeItems.Count,
                SafeItemsSize = CleanerSizeFormatter.Format(safeItems.Sum(item => item.SizeBytes)),
                ReviewItemsCount = reviewItems.Count,
                ReviewItemsSize = CleanerSizeFormatter.Format(reviewItems.Sum(item => item.SizeBytes)),
                Details = new
                {
                    SafeCategories = safeItems.GroupBy(item => item.Category).Select(group => new
                    {
                        Category = CleanerPresentation.ToCategoryText(group.Key),
                        Count = group.Count(),
                        Size = CleanerSizeFormatter.Format(group.Sum(item => item.SizeBytes))
                    }),
                    ReviewCategories = reviewItems.GroupBy(item => item.Category).Select(group => new
                    {
                        Category = CleanerPresentation.ToCategoryText(group.Key),
                        Count = group.Count(),
                        Size = CleanerSizeFormatter.Format(group.Sum(item => item.SizeBytes))
                    })
                }
            };

            _taskCenter.Complete(
                task.TaskId,
                $"扫描完成：低风险 {safeItems.Count} 项，建议确认 {reviewItems.Count} 项。");
            return JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }
        catch (OperationCanceledException)
        {
            if (task is { IsDuplicate: false })
            {
                _taskCenter.MarkCancelled(task.TaskId);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            return "扫描已由用户从任务中心取消。";
        }
        catch (Exception ex)
        {
            if (task is { IsDuplicate: false })
            {
                _taskCenter.Fail(task.TaskId, _privacyService.RedactForRemoteModel(ex.Message));
            }
            return $"扫描失败: {ex.Message}";
        }
        finally
        {
            task?.Dispose();
            operationLease?.Dispose();
        }
    }

    private async Task<string> ExecuteCleanupAsync(
        string args,
        Func<string, Task<bool>>? requestConfirmation,
        CancellationToken cancellationToken)
    {
        CleanerScanReport? latestScan = _sharedContext.GetCleanerScan(TimeSpan.FromMinutes(30));
        if (latestScan == null || latestScan.Items.Count == 0)
        {
            return "错误：没有找到 30 分钟内的有效扫描结果，请重新执行扫描。过期结果不能用于删除操作。";
        }

        AITaskLease? task = null;
        CleanerOperationLease? operationLease = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(args);
            List<string> targets = document.RootElement.TryGetProperty("categories_to_clean", out JsonElement categoriesProperty)
                ? categoriesProperty.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList()
                : new List<string>();
            List<CleanerScanItem> itemsToClean = latestScan.Items
                .Where(item =>
                    (targets.Contains("Safe") && item.RiskLevel == CleanerRiskLevel.Low) ||
                    (targets.Contains("Review") && item.RiskLevel == CleanerRiskLevel.Medium) ||
                    (targets.Contains("All") && item.RiskLevel is CleanerRiskLevel.Low or CleanerRiskLevel.Medium) ||
                    targets.Contains(item.Category) ||
                    targets.Contains(CleanerPresentation.ToCategoryText(item.Category)))
                .Where(item => item.IsSelectableAndEnabled && item.ExecutionMode != CleanerExecutionMode.None)
                .DistinctBy(item => item.ObjectId)
                .ToList();
            if (itemsToClean.Count == 0)
            {
                return "未匹配到需要清理的项目。传入的 categories_to_clean 参数未命中任何扫描结果。可用参数：'Safe', 'Review', 'All' 或具体的类别名称。";
            }

            CleanerCleanupPlanSummary cleanupPlan = CleanerCleanupPlanSummary.FromItems(itemsToClean);

            string idempotencyInput =
                $"{latestScan.CreatedAt:O}|{string.Join("|", itemsToClean.Select(item => item.ObjectId).OrderBy(id => id, StringComparer.Ordinal))}";
            string idempotencyKey = $"cleaner.execute:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyInput)))}";
            task = _taskCenter.Begin(
                "cleaner.execute",
                "AI 清理任务",
                $"等待确认：{cleanupPlan.ItemCount} 项，共 {CleanerSizeFormatter.Format(cleanupPlan.TotalBytes)}",
                idempotencyKey);
            if (task.IsDuplicate)
            {
                AITaskRecord? existing = _taskCenter.Get(task.TaskId);
                return existing?.IsActive == true
                    ? $"相同清理任务已经在执行或等待确认，任务编号：{task.TaskId}。"
                    : $"相同清理任务刚刚完成，为避免重复执行，本次未再次清理。任务编号：{task.TaskId}。";
            }

            _taskCenter.Report(task.TaskId, 5, "等待用户确认", $"将处理 {itemsToClean.Count} 个项目", AITaskStatus.AwaitingConfirmation);
            if (!await _operationPolicy.ConfirmAsync(
                requestConfirmation,
                "cleaner.execute",
                idempotencyKey,
                cleanupPlan.ConfirmationText + "\n\n是否继续？"))
            {
                _taskCenter.MarkCancelled(task.TaskId, "用户拒绝了清理确认");
                return "用户在安全确认弹窗中拒绝了本次清理操作。请告知用户清理已取消。";
            }

            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.AiCleanup, out operationLease))
            {
                _taskCenter.MarkCancelled(task.TaskId, "清理助手正在执行另一项磁盘任务");
                return "清理助手正在执行另一项扫描、清理或恢复任务，本次 AI 清理未执行。";
            }

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, task.Token);
            var progress = new Progress<CleanerExecutionProgress>(value =>
            {
                double percent = value.ProgressMax > 0 ? 10 + value.ProgressValue / value.ProgressMax * 90 : 10;
                _taskCenter.Report(task.TaskId, percent, value.StageTitle, value.Detail);
            });
            CleanerCleanupBatch batch = await _executionService.ExecuteAsync(
                itemsToClean,
                latestScan.Scope,
                progress,
                linkedCts.Token);
            await _auditService.RecordCleanupAsync(batch, 0);

            var failedEntries = batch.Entries
                .Where(entry => !string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var result = new
            {
                TotalProcessed = batch.Entries.Count,
                CleanedCount = batch.CompletedCount,
                FailedCount = batch.FailedCount,
                ReleasedSize = CleanerSizeFormatter.Format(batch.ReleasedBytes),
                RecoverableSize = CleanerSizeFormatter.Format(batch.RecoverableBytes),
                Outcome = batch.OutcomeText,
                FailedDetails = failedEntries.Select(entry => new
                {
                    Name = entry.ItemName,
                    Error = entry.ErrorMessage,
                    Reason = CleanerPresentation.ToFailureReasonText(entry.FailureReason)
                }).Take(5).ToList()
            };
            _taskCenter.Complete(task.TaskId, $"清理完成：成功 {batch.CompletedCount} 项，失败 {batch.FailedCount} 项，{batch.OutcomeText}。");
            return $"清理完成。结果：\n{JsonSerializer.Serialize(result, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}";
        }
        catch (OperationCanceledException)
        {
            if (task is { IsDuplicate: false }) _taskCenter.MarkCancelled(task.TaskId);
            if (cancellationToken.IsCancellationRequested) throw;
            return "清理任务已由用户从任务中心取消。";
        }
        catch (Exception ex)
        {
            if (task is { IsDuplicate: false }) _taskCenter.Fail(task.TaskId, _privacyService.RedactForRemoteModel(ex.Message));
            return $"清理执行失败: {ex.Message}";
        }
        finally
        {
            task?.Dispose();
            operationLease?.Dispose();
        }
    }

    private async Task<string> AnalyzeLatestCleanupLogAsync()
    {
        try
        {
            if (_stateStore == null)
            {
                return "清理历史服务当前不可用。";
            }

            CleanerCleanupBatch? latest = (await _stateStore.LoadHistoryAsync()).FirstOrDefault();
            if (latest == null)
            {
                return "尚未发现任何清理记录。";
            }

            var summary = new
            {
                latest.CreatedAt,
                Scope = latest.Scope.ToString(),
                latest.SelectedItemCount,
                latest.ProcessedBytes,
                latest.ReleasedBytes,
                latest.RecoverableBytes,
                latest.CompletedCount,
                latest.FailedCount,
                Entries = latest.Entries.Take(20).Select(entry => new
                {
                    entry.ItemName,
                    entry.Category,
                    entry.SizeBytes,
                    ExecutionMode = entry.ExecutionMode.ToString(),
                    entry.Status,
                    FailureReason = CleanerPresentation.ToFailureReasonText(entry.FailureReason),
                    entry.Restored
                })
            };
            string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            return $"[CLEANUP_SUMMARY] {_privacyService.RedactForRemoteModel(json)}";
        }
        catch (Exception ex)
        {
            return $"读取日志失败: {_privacyService.RedactForRemoteModel(ex.Message)}";
        }
    }

    private async Task<string> CreateCleanerRuleDraftAsync(
        string args,
        Func<string, Task<bool>>? requestConfirmation)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args);
            JsonElement root = document.RootElement;
            string name = root.GetProperty("name").GetString() ?? "AI 清理规则草稿";
            string path = root.GetProperty("path").GetString() ?? string.Empty;
            bool includeSubdirectories = !root.TryGetProperty("include_subdirectories", out JsonElement recursiveProperty) || recursiveProperty.GetBoolean();
            List<string> patterns = root.TryGetProperty("include_patterns", out JsonElement patternsProperty) && patternsProperty.ValueKind == JsonValueKind.Array
                ? patternsProperty.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList()
                : new List<string>();
            CleanerRuleDefinition draft = _ruleDraftService.BuildDraft(name, path, patterns, includeSubdirectories);
            string preview = $"""
                规则名称：{draft.Name}
                路径：{draft.Paths[0]}
                匹配：{(draft.IncludePatterns.Count == 0 ? "目录内容" : string.Join("、", draft.IncludePatterns))}
                风险：高风险、仅供查看
                执行方式：不会删除

                是否把这份草稿保存到本地规则草稿目录？
                """;
            if (!await _operationPolicy.ConfirmAsync(requestConfirmation, "cleaner.rule-draft.save", draft.Id, preview))
            {
                return "用户已取消保存规则草稿。";
            }
            string savedPath = await _ruleDraftService.SaveDraftAsync(draft);
            return $"规则草稿已保存：{_privacyService.DescribePathWithoutIdentity(savedPath)}。它不会自动生效，请在规则库中人工审核后再导入。";
        }
        catch (Exception ex)
        {
            return $"生成清理规则草稿失败：{_privacyService.RedactForRemoteModel(ex.Message)}";
        }
    }
}
