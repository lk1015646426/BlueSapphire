using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services
{
    public sealed class CleanerRiskEvaluator
    {
        private static readonly HashSet<string> UserDataExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".md",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".psd", ".mp3", ".wav", ".flac",
            ".zip", ".7z", ".rar", ".cs", ".sln", ".json", ".db", ".sqlite"
        };

        public CleanerRiskAssessment Evaluate(
            CleanerRuleDefinition? rule,
            string path,
            bool isLocked,
            DateTimeOffset modifyTime,
            long sizeBytes)
        {
            List<string> reasons = new();
            int score = 10;

            if (rule != null)
            {
                score += 45;
                reasons.Add("命中可信规则");

                if (rule.DefaultSelected)
                {
                    score += 5;
                }

                if (string.Equals(rule.OwnerApp, "BlueSapphire", StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                    reasons.Add("属于自家应用产物");
                }
            }

            string fullPath = SafeGetFullPath(path);
            if (IsSafeTemporaryLocation(fullPath))
            {
                score += 15;
                reasons.Add("位于临时或缓存目录");
            }

            if (IsSystemCoreLocation(fullPath))
            {
                score -= 35;
                reasons.Add("接近系统目录边界");
            }

            if (IsUserPersonalLocation(fullPath))
            {
                score -= 55;
                reasons.Add("接近用户个人数据目录");
            }

            if (ContainsOneDrive(fullPath))
            {
                score -= 20;
                reasons.Add("可能处于同步目录");
            }

            if (ContainsUserDataExtension(fullPath))
            {
                score -= 70;
                reasons.Add("包含常见用户文档扩展名");
            }

            if (modifyTime != DateTimeOffset.MinValue)
            {
                TimeSpan age = DateTimeOffset.Now - modifyTime;
                if (age >= TimeSpan.FromDays(30))
                {
                    score += 10;
                    reasons.Add("长期未修改");
                }
                else if (age >= TimeSpan.FromDays(7))
                {
                    score += 5;
                }
            }

            if (sizeBytes >= 1024L * 1024L * 1024L)
            {
                score -= 5;
                reasons.Add("体积较大，需要更谨慎");
            }

            if (isLocked)
            {
                score -= 35;
                reasons.Add("可能被进程占用");
            }

            if (rule?.ViewOnly == true || rule?.ExecutionMode == CleanerExecutionMode.None)
            {
                score = Math.Min(score, 35);
                reasons.Add("仅用于分析展示");
            }

            score = Math.Clamp(score, 0, 100);
            CleanerRiskLevel riskLevel = score switch
            {
                >= 80 => CleanerRiskLevel.Low,
                >= 50 => CleanerRiskLevel.Medium,
                _ => CleanerRiskLevel.High
            };

            string summary = riskLevel switch
            {
                CleanerRiskLevel.Low => "安全：删除后通常会自动重新生成",
                CleanerRiskLevel.Medium => "谨慎：建议确认后再清理",
                _ => "高风险：默认仅查看，不参与一键清理"
            };

            return new CleanerRiskAssessment
            {
                Score = score,
                RiskLevel = riskLevel,
                Summary = summary,
                Detail = reasons.Count == 0 ? "未收集到额外风险特征" : string.Join("，", reasons),
                CanSelect = riskLevel != CleanerRiskLevel.High && !isLocked
            };
        }

        private static string SafeGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static bool ContainsUserDataExtension(string path)
        {
            string extension = Path.GetExtension(path);
            return !string.IsNullOrWhiteSpace(extension) && UserDataExtensions.Contains(extension);
        }

        private static bool IsSafeTemporaryLocation(string path)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string temp = Path.GetTempPath();

            return path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\Windows\Temp\", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\CrashDumps\", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\Microsoft\Windows\Explorer\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSystemCoreLocation(string path)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            return path.StartsWith(Path.Combine(windows, "System32"), StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUserPersonalLocation(string path)
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            return roots.Any(root =>
                !string.IsNullOrWhiteSpace(root) &&
                path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsOneDrive(string path)
        {
            return path.Contains(@"\OneDrive\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
