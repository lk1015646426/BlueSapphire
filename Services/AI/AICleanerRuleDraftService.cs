using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BlueSapphire.Services.AI
{
    public sealed class AICleanerRuleDraftService
    {
        private readonly string _draftDirectory;

        public AICleanerRuleDraftService(string? rootPath = null)
        {
            string root = rootPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueSapphire");
            _draftDirectory = Path.Combine(root, "RuleDrafts");
        }

        public CleanerRuleDefinition BuildDraft(
            string name,
            string path,
            IReadOnlyList<string> includePatterns,
            bool includeSubdirectories)
        {
            string normalizedPath = NormalizeSafeDraftPath(path);
            List<string> patterns = includePatterns
                .Select(pattern => (pattern ?? string.Empty).Trim())
                .Where(pattern => pattern.Length is > 0 and <= 80)
                .Where(pattern => !pattern.Contains("..", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();

            return new CleanerRuleDefinition
            {
                Id = $"ai_draft_{Guid.NewGuid():N}",
                Name = string.IsNullOrWhiteSpace(name) ? "AI 清理规则草稿" : name.Trim()[..Math.Min(name.Trim().Length, 120)],
                Description = "由 AI 根据用户要求生成的只读规则草稿，导入前必须人工检查。",
                Category = "custom_draft",
                Scope = CleanerScanScope.Deep,
                ScanKind = patterns.Count > 0
                    ? CleanerScanKind.FilesByPattern
                    : CleanerScanKind.DirectoryContents,
                Paths = [normalizedPath],
                IncludePatterns = patterns,
                IncludeSubdirectories = includeSubdirectories,
                ExecutionMode = CleanerExecutionMode.None,
                RiskLevel = CleanerRiskLevel.High,
                DefaultSelected = false,
                OwnerApp = "用户自定义",
                WhyItConsumesSpace = "用户指定的目录可能包含可清理的构建产物、日志或缓存。",
                WhyItCanBeCleaned = "当前只是规则草稿，不会自动删除任何内容。",
                ImpactAfterCleanup = "必须在规则库中人工审核路径、匹配模式和影响后才能进一步启用。",
                RegenerationHint = "确认内容确实可再生成后，再创建隔离区版本的正式规则。",
                ViewOnly = true
            };
        }

        public async Task<string> SaveDraftAsync(CleanerRuleDefinition draft)
        {
            Directory.CreateDirectory(_draftDirectory);
            string path = Path.Combine(
                _draftDirectory,
                $"{DateTime.Now:yyyyMMdd-HHmmss}-{draft.Id}.json");
            var manifest = new CleanerRuleManifest { Rules = [draft] };
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, options));
            return path;
        }

        private static string NormalizeSafeDraftPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("规则路径不能为空。", nameof(path));
            }

            string normalized = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(normalized)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) ?? string.Empty;
            if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("不能为整个磁盘根目录生成清理规则。");
            }

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalized, windows, StringComparison.OrdinalIgnoreCase) ||
                windows.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("不能为 Windows 核心目录生成自定义清理规则。");
            }
            return normalized;
        }
    }
}
