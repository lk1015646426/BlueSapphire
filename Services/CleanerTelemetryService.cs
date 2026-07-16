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
using Microsoft.Extensions.Logging;
using BlueSapphire.Helpers;

namespace BlueSapphire.Services
{
    public sealed class CleanerTelemetryService
    {
        private static readonly HttpClient SharedClient = NetworkSafety.CreateSafeHttpClient();

        private readonly CleanerStateStore _stateStore;
        private readonly CleanerAuditService _auditService;
        private readonly CleanerRuleService _ruleService;
        private readonly CleanerProfileService _profileService;
        private readonly HttpClient _httpClient;
        private readonly Func<Uri, string, CancellationToken, Task<CleanerTelemetryUploadResult>> _uploader;
        private readonly ILogger<CleanerTelemetryService>? _logger;

        public CleanerTelemetryService(
            CleanerStateStore stateStore,
            CleanerAuditService auditService,
            CleanerRuleService ruleService,
            CleanerProfileService profileService,
            HttpClient? httpClient = null,
            Func<Uri, string, CancellationToken, Task<CleanerTelemetryUploadResult>>? uploader = null,
            IHttpClientFactory? httpClientFactory = null,
            ILogger<CleanerTelemetryService>? logger = null)
        {
            _stateStore = stateStore;
            _auditService = auditService;
            _ruleService = ruleService;
            _profileService = profileService;
            _httpClient = httpClient ?? httpClientFactory?.CreateClient("ExternalSafe") ?? SharedClient;
            _uploader = uploader ?? UploadCoreAsync;
            _logger = logger;
        }

        public CleanerTelemetryService(
            CleanerStateStore stateStore,
            CleanerAuditService auditService,
            CleanerRuleService ruleService,
            CleanerProfileService profileService)
            : this(stateStore, auditService, ruleService, profileService, null, null, null, null)
        {
        }

        public async Task<CleanerTelemetryStatus> LoadStatusAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            CleanerProfileState profile = await _profileService.GetProfileAsync();
            return BuildStatus(preferences, profile);
        }

        public async Task<CleanerTelemetryStatus> SaveSettingsAsync(bool enabled, string? endpoint)
        {
            string normalizedEndpoint = NormalizeEndpoint(endpoint);
            if (!string.IsNullOrWhiteSpace(normalizedEndpoint) &&
                (!Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out Uri? uri) ||
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("遥测上传地址必须使用 HTTPS。");
            }

            CleanerPreferenceState preferences = await _stateStore.UpdatePreferencesAsync(state =>
            {
                state.TelemetryEnabled = enabled;
                state.TelemetryEndpoint = normalizedEndpoint;
            });

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
                endpoint.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("请先配置有效的 HTTPS 遥测上传地址。");
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

                DateTimeOffset uploadedAt = DateTimeOffset.Now;
                string statusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "上传成功"
                    : $"上传成功 · {result.Message}";
                preferences = await _stateStore.UpdatePreferencesAsync(state =>
                {
                    state.LastTelemetryUploadedAt = uploadedAt;
                    state.LastTelemetryStatus = statusMessage;
                });
                _logger?.LogInformation("[CleanerTelemetryService] 遥测数据上传成功至 {Endpoint}，状态: {Status}", endpoint, preferences.LastTelemetryStatus);
            }
            catch (Exception ex)
            {
                string statusMessage = $"上传失败 · {ex.Message}";
                preferences = await _stateStore.UpdatePreferencesAsync(state =>
                {
                    state.LastTelemetryStatus = statusMessage;
                });
                _logger?.LogError(ex, "[CleanerTelemetryService] 遥测数据上传失败至 {Endpoint}", endpoint);
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
            await NetworkSafety.ValidatePublicUriAsync(endpoint, requireHttps: true, cancellationToken);
            using StringContent content = new(payload, Encoding.UTF8, "application/json");
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                return new CleanerTelemetryUploadResult(false, "为避免向未验证地址转发遥测，已拒绝服务器重定向。");
            }
            string responseBody;
            try
            {
                responseBody = await NetworkSafety.ReadContentAsStringAsync(
                    response.Content,
                    64 * 1024,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                responseBody = "响应内容超过 64 KB 限制";
            }
            if (!response.IsSuccessStatusCode)
            {
                return new CleanerTelemetryUploadResult(false, $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
            }

            string message = string.IsNullOrWhiteSpace(responseBody)
                ? response.ReasonPhrase ?? "遥测已接收"
                : responseBody.Trim()[..Math.Min(responseBody.Trim().Length, 1000)];
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
