using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class AIDiagnosticsService
    {
        private readonly CleanerAuditService _auditService;
        private readonly AIPrivacyService _privacyService;
        private readonly string _logPath;

        public AIDiagnosticsService(
            CleanerAuditService auditService,
            AIPrivacyService privacyService,
            string? rootPath = null)
        {
            _auditService = auditService;
            _privacyService = privacyService;
            string root = rootPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueSapphire");
            _logPath = Path.Combine(root, "Logs", "app.log");
        }

        public async Task<string> BuildDiagnosticSummaryAsync()
        {
            CleanerAuditSnapshot audit = await _auditService.LoadSnapshotAsync();
            List<string> recentLines = await ReadRecentLinesAsync(_logPath, 300);

            int accessDenied = CountMatches(recentLines, "access denied", "unauthorized", "权限不足", "拒绝访问");
            int locked = CountMatches(recentLines, "being used", "locked", "被占用", "占用进程");
            int network = CountMatches(recentLines, "timeout", "http", "network", "网络", "连接失败");
            int ruleErrors = CountMatches(recentLines, "CleanerRuleService", "规则包", "rule pack");
            int scanErrors = CountMatches(recentLines, "扫描失败", "ScanService] Error", "DeepScan] 抽样空间分析失败");

            var recommendations = new List<string>();
            if (accessDenied > 0)
            {
                recommendations.Add("权限问题较多：系统目录建议切换管理员模式后重试。");
            }
            if (locked > 0)
            {
                recommendations.Add("存在文件占用：先关闭对应应用，再重试失败项。");
            }
            if (network > 0)
            {
                recommendations.Add("检测到网络或超时异常：检查模型服务地址、代理和 API Key。");
            }
            if (ruleErrors > 0)
            {
                recommendations.Add("规则加载存在异常：可恢复内置规则包后重新扫描。");
            }
            if (scanErrors > 0)
            {
                recommendations.Add("扫描阶段出现跳过或失败：缩小磁盘范围，并查看任务中心时间线。");
            }
            if (recommendations.Count == 0)
            {
                recommendations.Add("最近日志中没有发现集中的权限、占用、网络或规则异常。");
            }

            string recentEvidence = string.Join(
                Environment.NewLine,
                recentLines
                    .Where(line => line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
                                   line.Contains("跳过", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(8)
                    .Select(line => $"- {_privacyService.RedactForRemoteModel(line)}"));

            return $"""
                诊断摘要：
                - 权限相关：{accessDenied}
                - 文件占用：{locked}
                - 网络或超时：{network}
                - 规则相关：{ruleErrors}
                - 扫描相关：{scanErrors}
                - 历史清理运行：{audit.TotalCleanupRuns}
                - 历史清理失败：{audit.TotalCleanupFailures}

                建议：
                {string.Join(Environment.NewLine, recommendations.Select(item => $"- {item}"))}

                最近脱敏证据：
                {(string.IsNullOrWhiteSpace(recentEvidence) ? "- 暂无错误级证据" : recentEvidence)}
                """;
        }

        private static async Task<List<string>> ReadRecentLinesAsync(string path, int maximumLines)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new List<string>();
                }
                return (await File.ReadAllLinesAsync(path)).TakeLast(maximumLines).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static int CountMatches(IEnumerable<string> lines, params string[] patterns)
        {
            return lines.Count(line => patterns.Any(pattern =>
                line.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
