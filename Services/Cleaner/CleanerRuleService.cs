using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BlueSapphire.Helpers;

namespace BlueSapphire.Services.Cleaner
{
    public sealed class CleanerRuleService
    {
        private static readonly JsonSerializerOptions RuleJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            MaxDepth = 32,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        private static readonly HttpClient SharedClient = NetworkSafety.CreateSafeHttpClient();
        private const int MaxRulePackBytes = 2 * 1024 * 1024;
        private const int MaxExternalRuleCount = 500;
        private static readonly Regex SafeRuleIdPattern = new(
            "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly CleanerStateStore _stateStore;
        private readonly CleanerProfileService _profileService;
        private readonly string _builtInRuleFilePath;
        private readonly HttpClient _httpClient;
        private readonly ILogger<CleanerRuleService>? _logger;

        private IReadOnlyList<CleanerRuleDefinition>? _cachedRules;
        private IReadOnlyList<CleanerRuleDefinition>? _cachedKnownRules;
        private CleanerRuleBundleStatus? _cachedStatus;

        public CleanerRuleService(
            CleanerStateStore stateStore,
            string? builtInRuleFilePath = null,
            HttpClient? httpClient = null,
            IHttpClientFactory? httpClientFactory = null,
            ILogger<CleanerRuleService>? logger = null)
            : this(stateStore, new CleanerProfileService(stateStore), builtInRuleFilePath, httpClient, httpClientFactory, logger)
        {
        }

        public CleanerRuleService(
            CleanerStateStore stateStore,
            CleanerProfileService profileService,
            string? builtInRuleFilePath = null,
            HttpClient? httpClient = null,
            IHttpClientFactory? httpClientFactory = null,
            ILogger<CleanerRuleService>? logger = null)
        {
            _stateStore = stateStore;
            _profileService = profileService;
            _builtInRuleFilePath = builtInRuleFilePath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "CleanerRules.json");
            _httpClient = httpClient ?? httpClientFactory?.CreateClient("ExternalSafe") ?? SharedClient;
            _logger = logger;
        }

        public async Task<IReadOnlyList<CleanerRuleDefinition>> GetRulesAsync()
        {
            await EnsureCatalogAsync();
            return _cachedRules ?? Array.Empty<CleanerRuleDefinition>();
        }

        public async Task<CleanerRuleBundleStatus> GetStatusAsync()
        {
            await EnsureCatalogAsync();
            return _cachedStatus ?? new CleanerRuleBundleStatus();
        }

        public async Task<IReadOnlyList<CleanerRuleDefinition>> GetKnownRulesAsync()
        {
            await EnsureCatalogAsync();
            return _cachedKnownRules ?? Array.Empty<CleanerRuleDefinition>();
        }

        public Task<CleanerRuleUpdateState> GetUpdateStateAsync()
        {
            return _stateStore.LoadRuleUpdateStateAsync();
        }

        public async Task<CleanerRuleBundleStatus> ImportRulePackAsync(string sourcePath)
        {
            FileInfo sourceInfo = new(sourcePath);
            if (!sourceInfo.Exists || sourceInfo.Length <= 0 || sourceInfo.Length > MaxRulePackBytes)
            {
                throw new InvalidOperationException("规则包不存在、为空或超过 2 MB 限制。");
            }

            CleanerRuleBundleDocument bundle = await LoadBundleDocumentAsync(sourcePath);
            Directory.CreateDirectory(_stateStore.RulePackDirectoryPath);
            string importTempPath = _stateStore.ImportedRulePackPath + ".tmp";
            try
            {
                File.Copy(sourcePath, importTempPath, true);
                File.Move(importTempPath, _stateStore.ImportedRulePackPath, true);
            }
            catch
            {
                try
                {
                    if (File.Exists(importTempPath)) File.Delete(importTempPath);
                }
                catch
                {
                    // 临时文件清理失败不掩盖导入原始异常。
                }
                throw;
            }

            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            updateState.LastRefreshedAt = DateTimeOffset.Now;
            updateState.LastBundleVersion = bundle.Version;
            updateState.LastBundleSource = ResolveBundleSource(bundle, "导入规则包");
            await _stateStore.SaveRuleUpdateStateAsync(updateState);

            InvalidateCache();
            return await GetStatusAsync();
        }

        public async Task<CleanerRuleBundleStatus> RefreshFromRemoteAsync(string remoteUri, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(remoteUri, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("远程规则链接必须使用 HTTPS。");
            }

            await NetworkSafety.ValidatePublicUriAsync(uri, requireHttps: true, cancellationToken);
            using HttpResponseMessage response = await NetworkSafety.GetFollowingSafeRedirectsAsync(
                _httpClient,
                uri,
                requireHttps: true,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxRulePackBytes)
            {
                throw new InvalidOperationException("远程规则包超过 2 MB 限制。");
            }

            string json = await NetworkSafety.ReadContentAsStringAsync(
                response.Content,
                MaxRulePackBytes,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("远程规则包为空。");
            }
            CleanerRuleBundleDocument bundle = ParseBundleDocument(json);

            Directory.CreateDirectory(_stateStore.RulePackDirectoryPath);
            string tempPath = _stateStore.ImportedRulePackPath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                File.Move(tempPath, _stateStore.ImportedRulePackPath, true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // 临时文件清理失败不掩盖下载原始异常。
                }
                throw;
            }

            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            updateState.RemoteUri = remoteUri;
            updateState.LastRefreshedAt = DateTimeOffset.Now;
            updateState.LastBundleVersion = bundle.Version;
            updateState.LastBundleSource = ResolveBundleSource(bundle, "远程规则包");
            await _stateStore.SaveRuleUpdateStateAsync(updateState);

            _logger?.LogInformation("[CleanerRuleService] 远程规则包刷新完成，版本: {Version}, 规则数: {Count}", bundle.Version, bundle.Rules.Count);
            InvalidateCache();
            return await GetStatusAsync();
        }

        public async Task<CleanerRuleBundleStatus> ClearExternalRulePackAsync()
        {
            if (File.Exists(_stateStore.ImportedRulePackPath))
            {
                File.Delete(_stateStore.ImportedRulePackPath);
            }

            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            updateState.LastBundleVersion = string.Empty;
            updateState.LastBundleSource = string.Empty;
            await _stateStore.SaveRuleUpdateStateAsync(updateState);

            InvalidateCache();
            return await GetStatusAsync();
        }

        public async Task<CleanerRuleBundleStatus> DisableRuleLocallyAsync(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
            {
                throw new InvalidOperationException("规则 ID 不能为空。");
            }

            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            if (!updateState.LocalDisabledRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
            {
                updateState.LocalDisabledRuleIds.Add(ruleId);
                updateState.LocalDisabledRuleIds = updateState.LocalDisabledRuleIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await _stateStore.SaveRuleUpdateStateAsync(updateState);
            }

            InvalidateCache();
            return await GetStatusAsync();
        }

        public async Task<CleanerRuleBundleStatus> EnableAllLocallyDisabledRulesAsync()
        {
            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            if (updateState.LocalDisabledRuleIds.Count > 0)
            {
                updateState.LocalDisabledRuleIds.Clear();
                await _stateStore.SaveRuleUpdateStateAsync(updateState);
            }

            InvalidateCache();
            return await GetStatusAsync();
        }

        private async Task EnsureCatalogAsync()
        {
            if (_cachedRules != null && _cachedStatus != null)
            {
                return;
            }

            CleanerRuleManifest builtInManifest = await LoadManifestAsync(_builtInRuleFilePath);
            CleanerRuleBundleDocument? externalBundle = await LoadExternalBundleAsync();
            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            CleanerProfileState profile = await _profileService.GetProfileAsync();
            List<string> localDisabledRuleIds = updateState.LocalDisabledRuleIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            IReadOnlyList<CleanerRuleDefinition> knownRules = MergeOverrides(
                builtInManifest.Rules,
                externalBundle?.Rules ?? new List<CleanerRuleDefinition>());

            HashSet<string> builtInRuleIds = builtInManifest.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .Select(rule => rule.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<string> externalDisabledRuleIds = (externalBundle?.DisabledRuleIds ?? new List<string>())
                .Where(id => !builtInRuleIds.Contains(id))
                .ToList();

            (IReadOnlyList<CleanerRuleDefinition> effectiveRules, int rolloutFilteredCount) = MergeRules(
                knownRules,
                externalDisabledRuleIds,
                localDisabledRuleIds,
                profile);

            _cachedKnownRules = knownRules;
            _cachedRules = effectiveRules;
            _cachedStatus = new CleanerRuleBundleStatus
            {
                BuiltInRuleCount = builtInManifest.Rules.Count,
                EffectiveRuleCount = effectiveRules.Count,
                ExternalRuleCount = externalBundle?.Rules.Count ?? 0,
                DisabledRuleCount = externalDisabledRuleIds.Count + localDisabledRuleIds.Count,
                RolloutFilteredRuleCount = rolloutFilteredCount,
                LocalDisabledRuleCount = localDisabledRuleIds.Count,
                HasExternalBundle = externalBundle != null,
                BundleVersion = externalBundle?.Version ?? updateState.LastBundleVersion,
                BundleSource = externalBundle != null
                    ? ResolveBundleSource(externalBundle, updateState.LastBundleSource)
                    : updateState.LastBundleSource,
                BundlePublishedAt = externalBundle?.PublishedAt,
                LastRefreshedAt = updateState.LastRefreshedAt,
                RemoteUri = updateState.RemoteUri,
                ActiveRolloutChannel = profile.RolloutChannel,
                DeviceBucket = profile.DeviceBucket
            };

            _logger?.LogInformation("[CleanerRuleService] 规则目录加载完成: 内置 {BuiltInCount}，生效 {EffectiveCount}，外部 {ExternalCount}，禁用 {DisabledCount}",
                _cachedStatus.BuiltInRuleCount, _cachedStatus.EffectiveRuleCount, _cachedStatus.ExternalRuleCount, _cachedStatus.DisabledRuleCount);
        }

        private async Task<CleanerRuleBundleDocument?> LoadExternalBundleAsync()
        {
            if (!File.Exists(_stateStore.ImportedRulePackPath))
            {
                return null;
            }

            try
            {
                return await LoadBundleDocumentAsync(_stateStore.ImportedRulePackPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[CleanerRuleService] 已忽略无效的外部规则包");
                return null;
            }
        }

        private static async Task<CleanerRuleManifest> LoadManifestAsync(string path)
        {
            if (!File.Exists(path))
            {
                return new CleanerRuleManifest();
            }

            await using FileStream stream = File.OpenRead(path);
            CleanerRuleManifest? manifest = await JsonSerializer.DeserializeAsync<CleanerRuleManifest>(stream, RuleJsonOptions);
            return manifest ?? new CleanerRuleManifest();
        }

        private static async Task<CleanerRuleBundleDocument> LoadBundleDocumentAsync(string path)
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length is <= 0 or > MaxRulePackBytes)
            {
                throw new InvalidOperationException("规则包不存在、为空或超过 2 MB 限制。");
            }
            string json = await File.ReadAllTextAsync(path);
            return ParseBundleDocument(json);
        }

        private static CleanerRuleBundleDocument ParseBundleDocument(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaxRulePackBytes)
            {
                throw new InvalidOperationException("规则包为空或超过 2 MB 限制。");
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("规则包格式无效。");
            }

            if (HasProperty(root, "rules"))
            {
                CleanerRuleBundleDocument? bundle = JsonSerializer.Deserialize<CleanerRuleBundleDocument>(json, RuleJsonOptions);
                return ValidateAndSandboxExternalBundle(bundle ?? new CleanerRuleBundleDocument());
            }

            CleanerRuleManifest? manifest = JsonSerializer.Deserialize<CleanerRuleManifest>(json, RuleJsonOptions);
            if (manifest == null)
            {
                throw new InvalidOperationException("无法解析规则包内容。");
            }

            return ValidateAndSandboxExternalBundle(new CleanerRuleBundleDocument
            {
                Source = "导入规则包",
                Rules = manifest.Rules
            });
        }

        private static IReadOnlyList<CleanerRuleDefinition> MergeOverrides(
            IReadOnlyList<CleanerRuleDefinition> builtInRules,
            IReadOnlyList<CleanerRuleDefinition> externalRules)
        {
            HashSet<string> builtInIds = builtInRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .Select(rule => rule.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<CleanerRuleDefinition> acceptedExternalRules = externalRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .Where(rule => !builtInIds.Contains(rule.Id))
                .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            List<CleanerRuleDefinition> merged = builtInRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .ToList();
            merged.AddRange(acceptedExternalRules);
            return merged;
        }

        private static CleanerRuleBundleDocument ValidateAndSandboxExternalBundle(CleanerRuleBundleDocument bundle)
        {
            if (bundle.Rules.Count > MaxExternalRuleCount)
            {
                throw new InvalidOperationException($"外部规则数量不能超过 {MaxExternalRuleCount} 条。");
            }

            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            foreach (CleanerRuleDefinition rule in bundle.Rules)
            {
                if (!SafeRuleIdPattern.IsMatch(rule.Id) || !ids.Add(rule.Id))
                {
                    throw new InvalidOperationException($"外部规则 ID 无效或重复：{rule.Id}");
                }

                if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 120)
                {
                    throw new InvalidOperationException($"规则 {rule.Id} 缺少有效名称。");
                }

                if (rule.Paths.Count is 0 or > 32)
                {
                    throw new InvalidOperationException($"规则 {rule.Id} 的路径数量必须在 1 到 32 之间。");
                }

                foreach (string path in rule.Paths)
                {
                    ValidateExternalPath(rule.Id, path);
                }

                if (rule.IncludePatterns.Count > 32 ||
                    rule.IncludePatterns.Any(pattern =>
                        string.IsNullOrWhiteSpace(pattern) ||
                        pattern.Length > 120 ||
                        pattern.Contains("..", StringComparison.Ordinal) ||
                        pattern.Contains(Path.DirectorySeparatorChar) ||
                        pattern.Contains(Path.AltDirectorySeparatorChar)))
                {
                    throw new InvalidOperationException($"规则 {rule.Id} 包含无效的文件匹配模式。");
                }

                if (rule.MinAgeDays is < 0 or > 3650 ||
                    rule.MaxAgeDays is < 1 or > 3650 ||
                    (rule.MinAgeDays.HasValue && rule.MaxAgeDays.HasValue && rule.MinAgeDays.Value >= rule.MaxAgeDays.Value))
                {
                    throw new InvalidOperationException($"规则 {rule.Id} 包含无效文件年龄范围。");
                }

                if (rule.ProcessNames.Count > 16 || rule.ProcessNames.Any(name =>
                        string.IsNullOrWhiteSpace(name) ||
                        name.Length > 80 ||
                        name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                {
                    throw new InvalidOperationException($"规则 {rule.Id} 包含无效进程名称。");
                }

                // 未签名的第三方规则只能提供空间分析，不具备删除、提权或默认选中能力。
                rule.ExecutionMode = CleanerExecutionMode.None;
                rule.RiskLevel = CleanerRiskLevel.High;
                rule.DefaultSelected = false;
                rule.RequiresElevation = false;
                rule.ViewOnly = true;
                rule.BoundaryRoots.Clear();
                rule.OwnerApp = string.IsNullOrWhiteSpace(rule.OwnerApp)
                    ? "第三方规则"
                    : $"{rule.OwnerApp}（第三方规则）";
            }

            bundle.DisabledRuleIds = bundle.DisabledRuleIds
                .Where(id => SafeRuleIdPattern.IsMatch(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxExternalRuleCount)
                .ToList();
            bundle.Version = bundle.Version?.Trim() ?? string.Empty;
            bundle.Source = bundle.Source?.Trim() ?? string.Empty;
            return bundle;
        }

        private static void ValidateExternalPath(string ruleId, string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath) ||
                rawPath.Length > 260 ||
                rawPath.Contains("..", StringComparison.Ordinal) ||
                rawPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                rawPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                rawPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"规则 {ruleId} 包含不安全路径。");
            }

            string expanded = Environment.ExpandEnvironmentVariables(rawPath);
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(expanded);
            }
            catch
            {
                throw new InvalidOperationException($"规则 {ruleId} 包含无法解析的路径。");
            }

            string[] allowedRoots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.GetTempPath()
            };

            if (!allowedRoots.Any(root => IsSameOrDescendant(fullPath, root)))
            {
                throw new InvalidOperationException(
                    $"规则 {ruleId} 的路径超出第三方规则允许分析的缓存目录范围。");
            }
        }

        private static bool IsSameOrDescendant(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(
                       normalizedRoot + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static (IReadOnlyList<CleanerRuleDefinition> Rules, int RolloutFilteredCount) MergeRules(
            IReadOnlyList<CleanerRuleDefinition> knownRules,
            IReadOnlyList<string> externalDisabledRuleIds,
            IReadOnlyList<string> localDisabledRuleIds,
            CleanerProfileState profile)
        {
            HashSet<string> disabled = externalDisabledRuleIds
                .Concat(localDisabledRuleIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<CleanerRuleDefinition> merged = new();
            int rolloutFilteredCount = 0;
            foreach (CleanerRuleDefinition rule in knownRules)
            {
                if (string.IsNullOrWhiteSpace(rule.Id) || disabled.Contains(rule.Id))
                {
                    continue;
                }

                if (!IsRuleInRollout(rule, profile))
                {
                    rolloutFilteredCount++;
                    continue;
                }

                merged.Add(rule);
            }

            return (merged, rolloutFilteredCount);
        }

        private static string ResolveBundleSource(CleanerRuleBundleDocument bundle, string fallback)
        {
            return string.IsNullOrWhiteSpace(bundle.Source) ? fallback : bundle.Source;
        }

        private void InvalidateCache()
        {
            _cachedRules = null;
            _cachedKnownRules = null;
            _cachedStatus = null;
        }

        private static bool IsRuleInRollout(CleanerRuleDefinition rule, CleanerProfileState profile)
        {
            List<string> channels = rule.RolloutChannels
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Select(CleanerProfileService.NormalizeChannel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (channels.Count > 0 && !channels.Contains(profile.RolloutChannel, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            int rolloutPercentage = Math.Clamp(rule.RolloutPercentage, 0, 100);
            if (rolloutPercentage == 0)
            {
                return false;
            }

            return profile.DeviceBucket < rolloutPercentage;
        }

        private static bool HasProperty(JsonElement element, string propertyName)
        {
            return element.EnumerateObject().Any(property =>
                string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
