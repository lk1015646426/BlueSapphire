using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class AIInsightService
    {
        private readonly AITaskCenterService _taskCenter;
        private readonly AISharedContextService _sharedContext;
        private readonly AIMemoryService _memoryService;
        private readonly CleanerAuditService _auditService;

        public AIInsightService(
            AITaskCenterService taskCenter,
            AISharedContextService sharedContext,
            AIMemoryService memoryService,
            CleanerAuditService auditService)
        {
            _taskCenter = taskCenter;
            _sharedContext = sharedContext;
            _memoryService = memoryService;
            _auditService = auditService;
        }

        public async Task<IReadOnlyList<string>> BuildNonIntrusiveSuggestionsAsync()
        {
            var suggestions = new List<string>();
            CleanerScanReport? scan = _sharedContext.GetCleanerScan(TimeSpan.FromDays(7));
            if (scan != null)
            {
                long safeBytes = scan.Items.Where(item => item.IsSafeBucket).Sum(item => item.SizeBytes);
                long viewOnlyBytes = scan.Items.Where(item => item.IsViewOnlyBucket).Sum(item => item.SizeBytes);
                if (safeBytes >= 1024L * 1024L * 1024L)
                {
                    suggestions.Add($"最近扫描有 {CleanerSizeFormatter.Format(safeBytes)} 低风险候选，可在确认后优先处理。");
                }
                if (viewOnlyBytes > safeBytes * 2)
                {
                    suggestions.Add("空间压力主要来自仅供查看的大文件，建议先做媒体归档或人工检查，而不是扩大自动清理范围。");
                }
            }

            IReadOnlyList<AITaskRecord> tasks = _taskCenter.GetSnapshot();
            int recentFailures = tasks.Count(task =>
                task.Status == AITaskStatus.Failed &&
                DateTimeOffset.Now - task.UpdatedAt < TimeSpan.FromDays(7));
            if (recentFailures >= 2)
            {
                suggestions.Add($"最近 7 天有 {recentFailures} 个任务失败，建议运行一次应用诊断。");
            }

            IReadOnlyList<AIMemoryEntry> memories = await _memoryService.GetEntriesAsync();
            int expired = memories.Count(entry => entry.IsExpired);
            if (expired > 0)
            {
                suggestions.Add($"有 {expired} 条长期记忆已经过期，可以在记忆管理中清理。");
            }

            CleanerAuditSnapshot audit = await _auditService.LoadSnapshotAsync();
            KeyValuePair<string, int> repeatedlyDeselected = audit.RuleDeselections
                .OrderByDescending(pair => pair.Value)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(repeatedlyDeselected.Key) &&
                repeatedlyDeselected.Value >= 3)
            {
                suggestions.Add(
                    $"规则“{repeatedlyDeselected.Key}”已被手动取消 {repeatedlyDeselected.Value} 次。可以询问用户是否将它改为默认不选；不要自动修改。");
            }

            if (suggestions.Count == 0)
            {
                suggestions.Add("当前没有需要主动提醒的异常，继续按现有保守策略使用即可。");
            }
            return suggestions.Take(5).ToList();
        }

        public string BuildCrossModulePlan(string objective, string? folderPath)
        {
            string folder = string.IsNullOrWhiteSpace(folderPath) ? "由用户选择的媒体目录" : folderPath;
            return $"""
                跨模块任务计划（仅预览，不执行）：
                1. 目标确认：{objective}
                2. 空间扫描：先读取最近清理扫描；结果过期则重新扫描。
                3. 媒体分析：在“{folder}”统计图片、格式、体积和完全重复候选。
                4. 风险分层：缓存由清理引擎判断；用户媒体始终作为人工确认项。
                5. 生成预览：分别列出清理候选、重复图片候选和按年月归档方案。
                6. 分步确认：清理、移入回收站、移动或重命名分别确认，互不捆绑授权。
                7. 执行与报告：记录任务时间线、成功项、失败项和恢复入口。
                """;
        }
    }
}
