using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerAuditService
    {
        private readonly CleanerStateStore _stateStore;

        public CleanerAuditService(CleanerStateStore stateStore)
        {
            _stateStore = stateStore;
        }

        public Task<CleanerAuditSnapshot> LoadSnapshotAsync()
        {
            return _stateStore.LoadAuditAsync();
        }

        public async Task RecordScanAsync(CleanerScanReport report)
        {
            CleanerAuditSnapshot snapshot = await _stateStore.LoadAuditAsync();
            snapshot.TotalScans++;
            snapshot.LastScanDurationMs = (long)report.Duration.TotalMilliseconds;
            snapshot.TotalScanDurationMs += snapshot.LastScanDurationMs;
            snapshot.LastScanItemCount = report.Items.Count;
            snapshot.RecentScans.Insert(0, BuildScanSnapshot(report));
            if (snapshot.RecentScans.Count > 12)
            {
                snapshot.RecentScans.RemoveRange(12, snapshot.RecentScans.Count - 12);
            }

            foreach (CleanerScanItem item in report.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.RuleId))
                {
                    snapshot.RuleHits.TryGetValue(item.RuleId, out int current);
                    snapshot.RuleHits[item.RuleId] = current + 1;
                }
            }

            await _stateStore.SaveAuditAsync(snapshot);
        }

        public async Task RecordCleanupAsync(CleanerCleanupBatch batch, int manualDeselections)
        {
            CleanerAuditSnapshot snapshot = await _stateStore.LoadAuditAsync();
            snapshot.TotalCleanupRuns++;
            snapshot.TotalReleasedBytes += batch.ReleasedBytes;
            snapshot.TotalCleanupFailures += batch.FailedCount;
            snapshot.TotalManualDeselections += Math.Max(0, manualDeselections);

            foreach (CleanerCleanupEntry entry in batch.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.RuleId)))
            {
                if (string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.RuleCleanupSuccesses.TryGetValue(entry.RuleId, out int successCount);
                    snapshot.RuleCleanupSuccesses[entry.RuleId] = successCount + 1;
                }
            }

            foreach (CleanerCleanupEntry entry in batch.Entries.Where(entry => entry.FailureReason != CleanerFailureReason.None))
            {
                string key = CleanerPresentation.ToFailureReasonText(entry.FailureReason);
                snapshot.FailureReasons.TryGetValue(key, out int current);
                snapshot.FailureReasons[key] = current + 1;

                if (!string.IsNullOrWhiteSpace(entry.RuleId))
                {
                    snapshot.RuleFailures.TryGetValue(entry.RuleId, out int failureCount);
                    snapshot.RuleFailures[entry.RuleId] = failureCount + 1;
                }
            }

            await _stateStore.SaveAuditAsync(snapshot);
        }

        public async Task RecordRestoreAsync(CleanerRestoreSummary summary)
        {
            CleanerAuditSnapshot snapshot = await _stateStore.LoadAuditAsync();
            snapshot.TotalRestoredItems += summary.RestoredCount;
            snapshot.TotalRestoredBytes += summary.RestoredBytes;
            await _stateStore.SaveAuditAsync(snapshot);
        }

        public async Task RecordRetryAsync(int retryAttempts, int retryRecoveredItems, IEnumerable<CleanerCleanupEntry> failedEntries)
        {
            CleanerAuditSnapshot snapshot = await _stateStore.LoadAuditAsync();
            snapshot.TotalRetryRuns += Math.Max(0, retryAttempts);
            snapshot.TotalRetryRecoveredItems += Math.Max(0, retryRecoveredItems);

            foreach (CleanerCleanupEntry entry in failedEntries.Where(entry => entry.FailureReason != CleanerFailureReason.None))
            {
                string key = CleanerPresentation.ToFailureReasonText(entry.FailureReason);
                snapshot.FailureReasons.TryGetValue(key, out int current);
                snapshot.FailureReasons[key] = current + 1;

                if (!string.IsNullOrWhiteSpace(entry.RuleId))
                {
                    snapshot.RuleFailures.TryGetValue(entry.RuleId, out int failureCount);
                    snapshot.RuleFailures[entry.RuleId] = failureCount + 1;
                }
            }

            await _stateStore.SaveAuditAsync(snapshot);
        }

        public async Task RecordDeselectionAsync(IEnumerable<CleanerScanItem> items)
        {
            CleanerAuditSnapshot snapshot = await _stateStore.LoadAuditAsync();
            foreach (CleanerScanItem item in items)
            {
                if (item.DefaultSelected && !item.IsSelected && item.IsSelectableAndEnabled && !string.IsNullOrWhiteSpace(item.RuleId))
                {
                    snapshot.RuleDeselections.TryGetValue(item.RuleId, out int current);
                    snapshot.RuleDeselections[item.RuleId] = current + 1;
                }
            }

            await _stateStore.SaveAuditAsync(snapshot);
        }

        public async Task<string> ExportDiagnosticReportAsync(
            IReadOnlyList<CleanerRuleDefinition> knownRules,
            CleanerRuleBundleStatus ruleStatus)
        {
            CleanerAuditSnapshot snapshot = await _stateStore.LoadAuditAsync();
            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            CleanerDiagnosticReport report = new()
            {
                GeneratedAt = DateTimeOffset.Now,
                RuleStatus = ruleStatus,
                RuleUpdateState = updateState,
                AuditSnapshot = snapshot,
                TopRuleIssues = BuildRuleQualityEntries(snapshot, knownRules, updateState.LocalDisabledRuleIds)
                    .OrderByDescending(entry => entry.IssueScore)
                    .ThenByDescending(entry => entry.FailureCount)
                    .ThenBy(entry => entry.RuleName, StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList()
            };

            string reportDirectory = Path.Combine(_stateStore.RootPath, "AuditReports");
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, $"cleaner-diagnostic-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            await using FileStream stream = File.Create(reportPath);
            await JsonSerializer.SerializeAsync(stream, report, options);
            return reportPath;
        }

        public static IReadOnlyList<CleanerRuleQualityEntry> BuildRuleQualityEntries(
            CleanerAuditSnapshot snapshot,
            IReadOnlyList<CleanerRuleDefinition> knownRules,
            IReadOnlyList<string>? localDisabledRuleIds = null)
        {
            Dictionary<string, string> ruleNameLookup = knownRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Name,
                    StringComparer.OrdinalIgnoreCase);

            HashSet<string> localDisabled = localDisabledRuleIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            HashSet<string> ruleIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (string ruleId in snapshot.RuleHits.Keys)
            {
                ruleIds.Add(ruleId);
            }
            foreach (string ruleId in snapshot.RuleCleanupSuccesses.Keys)
            {
                ruleIds.Add(ruleId);
            }
            foreach (string ruleId in snapshot.RuleFailures.Keys)
            {
                ruleIds.Add(ruleId);
            }
            foreach (string ruleId in snapshot.RuleDeselections.Keys)
            {
                ruleIds.Add(ruleId);
            }

            return ruleIds
                .Select(ruleId => new CleanerRuleQualityEntry
                {
                    RuleId = ruleId,
                    RuleName = ruleNameLookup.TryGetValue(ruleId, out string? ruleName) && !string.IsNullOrWhiteSpace(ruleName)
                        ? ruleName
                        : ruleId,
                    HitCount = snapshot.RuleHits.GetValueOrDefault(ruleId, 0),
                    CleanupSuccessCount = snapshot.RuleCleanupSuccesses.GetValueOrDefault(ruleId, 0),
                    FailureCount = snapshot.RuleFailures.GetValueOrDefault(ruleId, 0),
                    DeselectionCount = snapshot.RuleDeselections.GetValueOrDefault(ruleId, 0),
                    IsLocallyDisabled = localDisabled.Contains(ruleId)
                })
                .ToList();
        }

        private static CleanerScanSnapshot BuildScanSnapshot(CleanerScanReport report)
        {
            int safeCount = 0;
            int reviewCount = 0;
            int viewOnlyCount = 0;
            long safeBytes = 0;
            long reviewBytes = 0;
            long viewOnlyBytes = 0;
            long totalBytes = 0;

            foreach (CleanerScanItem item in report.Items)
            {
                totalBytes += item.SizeBytes;
                if (item.IsSafeBucket)
                {
                    safeCount++;
                    safeBytes += item.SizeBytes;
                }
                else if (item.IsReviewBucket)
                {
                    reviewCount++;
                    reviewBytes += item.SizeBytes;
                }
                else if (item.IsViewOnlyBucket)
                {
                    viewOnlyCount++;
                    viewOnlyBytes += item.SizeBytes;
                }
            }

            return new CleanerScanSnapshot
            {
                CreatedAt = report.CreatedAt,
                Scope = report.Scope,
                DriveRoots = report.AnalysisDriveRoots
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                DurationMs = (long)report.Duration.TotalMilliseconds,
                TotalItemCount = report.Items.Count,
                SafeItemCount = safeCount,
                ReviewItemCount = reviewCount,
                ViewOnlyItemCount = viewOnlyCount,
                TotalBytes = totalBytes,
                SafeBytes = safeBytes,
                ReviewBytes = reviewBytes,
                ViewOnlyBytes = viewOnlyBytes,
                UsedIncrementalReuse = report.UsedIncrementalReuse,
                ReusedItemCount = report.ReusedItemCount
            };
        }
    }
}
