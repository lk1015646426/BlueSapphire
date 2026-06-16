using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerTelemetryService
    {
        private static readonly HttpClient SharedClient = new HttpClient();

        private readonly CleanerStateStore _stateStore;
        private readonly CleanerAuditService _auditService;
        private readonly CleanerRuleService _ruleService;
        private readonly CleanerProfileService _profileService;
        private readonly HttpClient _httpClient;
        private readonly Func<Uri, string, CancellationToken, Task<CleanerTelemetryUploadResult>> _uploader;

        public CleanerTelemetryService(
            CleanerStateStore stateStore,
            CleanerAuditService auditService,
            CleanerRuleService ruleService,
            CleanerProfileService profileService)
            : this(stateStore, auditService, ruleService, profileService, null, null)
        {
        }

        public CleanerTelemetryService(
            CleanerStateStore stateStore,
            CleanerAuditService auditService,
            CleanerRuleService ruleService,
            CleanerProfileService profileService,
            HttpClient? httpClient = null,
            Func<Uri, string, CancellationToken, Task<CleanerTelemetryUploadResult>>? uploader = null)
        {
            _stateStore = stateStore;
            _auditService = auditService;
            _ruleService = ruleService;
            _profileService = profileService;
            _httpClient = httpClient ?? SharedClient;
            _uploader = uploader ?? UploadCoreAsync;
        }

        public async Task<CleanerTelemetryStatus> LoadStatusAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            CleanerProfileState profile = await _profileService.GetProfileAsync();
            return BuildStatus(preferences, profile);
        }

        public async Task<CleanerTelemetryStatus> SaveSettingsAsync(bool enabled, string? endpoint)
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            preferences.TelemetryEnabled = enabled;
            preferences.TelemetryEndpoint = NormalizeEndpoint(endpoint);
            await _stateStore.SavePreferencesAsync(preferences);

            CleanerProfileState profile = await _profileService.GetProfileAsync();
            return BuildStatus(preferences, profile);
        }

        public async Task<CleanerTelemetryStatus> UploadNowAsync(CancellationToken cancellationToken = default)
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            CleanerProfileState profile = await _profileService.GetProfileAsync();
            if (!preferences.TelemetryEnabled)
            {
                throw new InvalidOperationException("云端遥测当前未启用。");
            }

            if (!Uri.TryCreate(preferences.TelemetryEndpoint, UriKind.Absolute, out Uri? endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("请先配置有效的遥测上传地址。");
            }

            CleanerAuditSnapshot snapshot = await _auditService.LoadSnapshotAsync();
            CleanerRuleBundleStatus ruleStatus = await _ruleService.GetStatusAsync();
            string payload = BuildPayload(snapshot, ruleStatus, profile);

            try
            {
                CleanerTelemetryUploadResult result = await _uploader(endpoint, payload, cancellationToken);
                if (!result.Success)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Message) ? "遥测端点未接受当前上报。" : result.Message);
                }

                preferences.LastTelemetryUploadedAt = DateTimeOffset.Now;
                preferences.LastTelemetryStatus = string.IsNullOrWhiteSpace(result.Message)
                    ? "上传成功"
                    : $"上传成功 · {result.Message}";
                await _stateStore.SavePreferencesAsync(preferences);
            }
            catch (Exception ex)
            {
                preferences.LastTelemetryStatus = $"上传失败 · {ex.Message}";
                await _stateStore.SavePreferencesAsync(preferences);
                throw;
            }

            return BuildStatus(preferences, profile);
        }

        private static CleanerTelemetryStatus BuildStatus(CleanerPreferenceState preferences, CleanerProfileState profile)
        {
            return new CleanerTelemetryStatus
            {
                Enabled = preferences.TelemetryEnabled,
                Endpoint = NormalizeEndpoint(preferences.TelemetryEndpoint),
                LastUploadedAt = preferences.LastTelemetryUploadedAt,
                LastStatusText = string.IsNullOrWhiteSpace(preferences.LastTelemetryStatus)
                    ? "尚未上传"
                    : preferences.LastTelemetryStatus,
                RolloutChannel = profile.RolloutChannel,
                DeviceBucket = profile.DeviceBucket
            };
        }

        private async Task<CleanerTelemetryUploadResult> UploadCoreAsync(Uri endpoint, string payload, CancellationToken cancellationToken)
        {
            using StringContent content = new(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CleanerTelemetryUploadResult(false, $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
            }

            string message = string.IsNullOrWhiteSpace(responseBody)
                ? response.ReasonPhrase ?? "遥测已接收"
                : responseBody.Trim();
            return new CleanerTelemetryUploadResult(true, message);
        }

        private static string BuildPayload(
            CleanerAuditSnapshot snapshot,
            CleanerRuleBundleStatus ruleStatus,
            CleanerProfileState profile)
        {
            CleanerScanSnapshot? latestScan = snapshot.RecentScans
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();

            object payload = new
            {
                sentAt = DateTimeOffset.Now,
                appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                profile = new
                {
                    id = profile.DeviceProfileId,
                    rolloutChannel = profile.RolloutChannel,
                    deviceBucket = profile.DeviceBucket
                },
                ruleStatus = new
                {
                    builtInRuleCount = ruleStatus.BuiltInRuleCount,
                    effectiveRuleCount = ruleStatus.EffectiveRuleCount,
                    externalRuleCount = ruleStatus.ExternalRuleCount,
                    disabledRuleCount = ruleStatus.DisabledRuleCount,
                    localDisabledRuleCount = ruleStatus.LocalDisabledRuleCount,
                    rolloutFilteredRuleCount = ruleStatus.RolloutFilteredRuleCount
                },
                audit = new
                {
                    totalScans = snapshot.TotalScans,
                    totalCleanupRuns = snapshot.TotalCleanupRuns,
                    totalReleasedBytes = snapshot.TotalReleasedBytes,
                    totalCleanupFailures = snapshot.TotalCleanupFailures,
                    totalRestoredItems = snapshot.TotalRestoredItems,
                    totalRetryRuns = snapshot.TotalRetryRuns,
                    totalManualDeselections = snapshot.TotalManualDeselections,
                    topFailure = snapshot.TopFailureSummaryText,
                    recentScanCount = snapshot.RecentScans.Count,
                    latestScan = latestScan == null
                        ? null
                        : new
                        {
                            latestScan.ScopeText,
                            latestScan.TotalBytes,
                            latestScan.SafeBytes,
                            latestScan.ReviewBytes,
                            latestScan.ViewOnlyBytes,
                            latestScan.UsedIncrementalReuse,
                            latestScan.ReusedItemCount
                        },
                    topRuleFailures = snapshot.RuleFailures
                        .OrderByDescending(pair => pair.Value)
                        .Take(5)
                        .Select(pair => new { ruleId = pair.Key, failures = pair.Value })
                        .ToList(),
                    topRuleDeselections = snapshot.RuleDeselections
                        .OrderByDescending(pair => pair.Value)
                        .Take(5)
                        .Select(pair => new { ruleId = pair.Key, deselections = pair.Value })
                        .ToList(),
                    failureReasons = snapshot.FailureReasons
                        .OrderByDescending(pair => pair.Value)
                        .Take(5)
                        .Select(pair => new { reason = pair.Key, count = pair.Value })
                        .ToList()
                }
            };

            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(payload, options);
        }

        private static string NormalizeEndpoint(string? endpoint)
        {
            return string.IsNullOrWhiteSpace(endpoint)
                ? string.Empty
                : endpoint.Trim();
        }
    }

    public readonly record struct CleanerTelemetryUploadResult(bool Success, string Message);
}
