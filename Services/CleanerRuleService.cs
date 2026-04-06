using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerRuleService
    {
        private static readonly JsonSerializerOptions RuleJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        private readonly CleanerStateStore _stateStore;
        private readonly CleanerProfileService _profileService;
        private readonly string _builtInRuleFilePath;
        private readonly HttpClient _httpClient;

        private IReadOnlyList<CleanerRuleDefinition>? _cachedRules;
        private IReadOnlyList<CleanerRuleDefinition>? _cachedKnownRules;
        private CleanerRuleBundleStatus? _cachedStatus;

        public CleanerRuleService(
            CleanerStateStore stateStore,
            string? builtInRuleFilePath = null,
            HttpClient? httpClient = null)
            : this(stateStore, new CleanerProfileService(stateStore), builtInRuleFilePath, httpClient)
        {
        }

        public CleanerRuleService(
            CleanerStateStore stateStore,
            CleanerProfileService profileService,
            string? builtInRuleFilePath = null,
            HttpClient? httpClient = null)
        {
            _stateStore = stateStore;
            _profileService = profileService;
            _builtInRuleFilePath = builtInRuleFilePath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "CleanerRules.json");
            _httpClient = httpClient ?? new HttpClient();
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
            CleanerRuleBundleDocument bundle = await LoadBundleDocumentAsync(sourcePath);
            Directory.CreateDirectory(_stateStore.RulePackDirectoryPath);
            File.Copy(sourcePath, _stateStore.ImportedRulePackPath, true);

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
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("规则链接必须是有效的 http 或 https 地址。");
            }

            using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            CleanerRuleBundleDocument bundle = ParseBundleDocument(json);

            Directory.CreateDirectory(_stateStore.RulePackDirectoryPath);
            string tempPath = _stateStore.ImportedRulePackPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _stateStore.ImportedRulePackPath, true);

            CleanerRuleUpdateState updateState = await _stateStore.LoadRuleUpdateStateAsync();
            updateState.RemoteUri = remoteUri;
            updateState.LastRefreshedAt = DateTimeOffset.Now;
            updateState.LastBundleVersion = bundle.Version;
            updateState.LastBundleSource = ResolveBundleSource(bundle, "远程规则包");
            await _stateStore.SaveRuleUpdateStateAsync(updateState);

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

            (IReadOnlyList<CleanerRuleDefinition> effectiveRules, int rolloutFilteredCount) = MergeRules(
                knownRules,
                externalBundle?.DisabledRuleIds ?? new List<string>(),
                localDisabledRuleIds,
                profile);

            _cachedKnownRules = knownRules;
            _cachedRules = effectiveRules;
            _cachedStatus = new CleanerRuleBundleStatus
            {
                BuiltInRuleCount = builtInManifest.Rules.Count,
                EffectiveRuleCount = effectiveRules.Count,
                ExternalRuleCount = externalBundle?.Rules.Count ?? 0,
                DisabledRuleCount = (externalBundle?.DisabledRuleIds.Count ?? 0) + localDisabledRuleIds.Count,
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
        }

        private async Task<CleanerRuleBundleDocument?> LoadExternalBundleAsync()
        {
            if (!File.Exists(_stateStore.ImportedRulePackPath))
            {
                return null;
            }

            return await LoadBundleDocumentAsync(_stateStore.ImportedRulePackPath);
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
            string json = await File.ReadAllTextAsync(path);
            return ParseBundleDocument(json);
        }

        private static CleanerRuleBundleDocument ParseBundleDocument(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("规则包格式无效。");
            }

            if (HasProperty(root, "rules"))
            {
                CleanerRuleBundleDocument? bundle = JsonSerializer.Deserialize<CleanerRuleBundleDocument>(json, RuleJsonOptions);
                return bundle ?? new CleanerRuleBundleDocument();
            }

            CleanerRuleManifest? manifest = JsonSerializer.Deserialize<CleanerRuleManifest>(json, RuleJsonOptions);
            if (manifest == null)
            {
                throw new InvalidOperationException("无法解析规则包内容。");
            }

            return new CleanerRuleBundleDocument
            {
                Source = "导入规则包",
                Rules = manifest.Rules
            };
        }

        private static IReadOnlyList<CleanerRuleDefinition> MergeOverrides(
            IReadOnlyList<CleanerRuleDefinition> builtInRules,
            IReadOnlyList<CleanerRuleDefinition> externalRules)
        {
            Dictionary<string, CleanerRuleDefinition> externalLookup = externalRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            List<CleanerRuleDefinition> merged = new();
            foreach (CleanerRuleDefinition builtIn in builtInRules)
            {
                if (string.IsNullOrWhiteSpace(builtIn.Id))
                {
                    continue;
                }

                if (externalLookup.TryGetValue(builtIn.Id, out CleanerRuleDefinition? replacement))
                {
                    merged.Add(replacement);
                    externalLookup.Remove(builtIn.Id);
                }
                else
                {
                    merged.Add(builtIn);
                }
            }

            merged.AddRange(externalLookup.Values);
            return merged;
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
