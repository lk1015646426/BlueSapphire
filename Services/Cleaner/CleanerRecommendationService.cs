using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueSapphire.Services.Cleaner
{
    public sealed class CleanerRecommendationService
    {
        public CleanerRecommendationSummary BuildSummary(
            CleanerAuditSnapshot auditSnapshot,
            IReadOnlyList<CleanerScanItem> currentItems,
            CleanerProfileState profileState,
            CleanerAutomationStatus automationStatus,
            CleanerRuleBundleStatus ruleStatus)
        {
            long safeBytes = currentItems.Where(item => item.IsSafeBucket).Sum(item => item.SizeBytes);
            long reviewBytes = currentItems.Where(item => item.IsReviewBucket).Sum(item => item.SizeBytes);
            long viewOnlyBytes = currentItems.Where(item => item.IsViewOnlyBucket).Sum(item => item.SizeBytes);
            int viewOnlyLargeCount = currentItems.Count(item =>
                string.Equals(item.Category, "unknown_large", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, "unknown_large_file", StringComparison.OrdinalIgnoreCase));

            List<CleanerRecommendationEntry> entries = new();

            if (safeBytes >= 2L * 1024 * 1024 * 1024 || currentItems.Count(item => item.IsSafeBucket) >= 8)
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "优先处理低风险释放空间",
                    Detail = $"当前可安全清理 {CleanerSizeFormatter.Format(safeBytes)}，适合先跑一次快速清理，低风险收益最高。",
                    ActionText = "先执行快速扫描或直接处理默认勾选的低风险项"
                });
            }

            if (reviewBytes >= Math.Max(768L * 1024 * 1024, safeBytes / 2))
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "建议先人工确认中风险项",
                    Detail = $"建议确认项累计 {CleanerSizeFormatter.Format(reviewBytes)}，空间收益已经接近主力释放区，值得逐项确认。",
                    ActionText = "优先检查“建议确认后清理”分组"
                });
            }

            if (viewOnlyBytes >= 2L * 1024 * 1024 * 1024 || viewOnlyLargeCount >= 3)
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "空间压力主要来自大目录和大文件",
                    Detail = $"仅供查看对象累计 {CleanerSizeFormatter.Format(viewOnlyBytes)}，这更像是空间治理问题，而不是缓存清理问题。",
                    ActionText = "优先查看大文件、大目录和下载型内容"
                });
            }

            int inUseFailures = auditSnapshot.FailureReasons.GetValueOrDefault("被占用", 0);
            int accessDeniedFailures = auditSnapshot.FailureReasons.GetValueOrDefault("权限不足", 0);
            int elevationFailures = auditSnapshot.FailureReasons.GetValueOrDefault("需要管理员模式", 0);
            if (inUseFailures + accessDeniedFailures + elevationFailures >= 3)
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "失败主要来自占用和权限链路",
                    Detail = $"历史上已有 {inUseFailures + accessDeniedFailures + elevationFailures} 次失败集中在占用或权限，执行前先关应用、必要时切到管理员模式更稳。",
                    ActionText = "先关闭占用程序，再处理系统级目录"
                });
            }

            if (!automationStatus.AutoLowRiskCleanupEnabled &&
                safeBytes >= 1L * 1024 * 1024 * 1024 &&
                auditSnapshot.TotalCleanupRuns >= 2 &&
                auditSnapshot.TotalCleanupFailures <= Math.Max(1, auditSnapshot.TotalCleanupRuns / 2))
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "可以启用自动低风险保洁",
                    Detail = "你的使用习惯更偏稳定清理，低风险项收益稳定且失败率不高，可以让它按周期自动处理。",
                    ActionText = "打开“定时保洁”里的自动低风险清理"
                });
            }

            if (ruleStatus.RolloutFilteredRuleCount > 0)
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "当前规则处于灰度治理状态",
                    Detail = $"当前通道为 {FormatRolloutChannel(profileState.RolloutChannel)}，有 {ruleStatus.RolloutFilteredRuleCount} 条规则仍在灰度之外。",
                    ActionText = "需要验证新规则时，可切到 Canary 或 Internal 通道"
                });
            }

            if (entries.Count == 0)
            {
                entries.Add(new CleanerRecommendationEntry
                {
                    Title = "当前清理策略比较均衡",
                    Detail = "没有出现特别集中的风险或空间热点，保持现在的保守策略即可。",
                    ActionText = "继续按当前分层结果执行扫描和清理"
                });
            }

            return new CleanerRecommendationSummary
            {
                Headline = entries[0].Title,
                Detail = $"当前候选空间：安全 {CleanerSizeFormatter.Format(safeBytes)} · 确认 {CleanerSizeFormatter.Format(reviewBytes)} · 查看 {CleanerSizeFormatter.Format(viewOnlyBytes)}。",
                PreferenceModelText = BuildPreferenceModelText(auditSnapshot, automationStatus, viewOnlyBytes, safeBytes),
                Entries = entries.Take(4).ToList()
            };
        }

        private static string BuildPreferenceModelText(
            CleanerAuditSnapshot auditSnapshot,
            CleanerAutomationStatus automationStatus,
            long viewOnlyBytes,
            long safeBytes)
        {
            if (auditSnapshot.TotalManualDeselections >= 10)
            {
                return "偏好模型：保守确认型。你更倾向于先看清影响，再决定是否清理。";
            }

            if (automationStatus.AutoLowRiskCleanupEnabled)
            {
                return "偏好模型：低风险自动保洁型。你接受把稳定、可恢复的对象交给周期化处理。";
            }

            if (viewOnlyBytes > safeBytes * 2)
            {
                return "偏好模型：空间分析优先型。你当前更需要的是找出大户，而不是删更多缓存。";
            }

            return "偏好模型：平衡治理型。你当前的操作习惯适合保持解释优先、逐步放开的策略。";
        }

        private static string FormatRolloutChannel(string rolloutChannel)
        {
            return CleanerProfileService.NormalizeChannel(rolloutChannel) switch
            {
                "canary" => "Canary",
                "internal" => "Internal",
                _ => "Stable"
            };
        }
    }
}
