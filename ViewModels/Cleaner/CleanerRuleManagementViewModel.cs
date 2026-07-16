using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BlueSapphire.ViewModels
{
    public sealed partial class CleanerRuleManagementViewModel : ObservableObject
    {
        private readonly CleanerRuleService _ruleService;
        private readonly CleanerTelemetryService _telemetryService;
        private readonly CleanerProfileService _profileService;
        private readonly CleanerStateStore _stateStore;
        private readonly NativeFileService _nativeFileService;
        private readonly CleanerSettingsViewModel _settings;
        private readonly CleanerAuditService _auditService;
        private ICleanerAssistantViewInteraction? _view;

        private CleanerRuleBundleStatus _ruleStatus = new();
        private CleanerTelemetryStatus _telemetryStatus = new();
        private CleanerProfileState _profileState = new();
        private readonly object _ruleRefreshSync = new();
        private CancellationTokenSource? _ruleRefreshCts;

        public event EventHandler? ScanInvalidated;
        public event EventHandler? RulesChanged;

        public CleanerRuleManagementViewModel(
            CleanerRuleService ruleService,
            CleanerTelemetryService telemetryService,
            CleanerProfileService profileService,
            CleanerStateStore stateStore,
            NativeFileService nativeFileService,
            CleanerSettingsViewModel settings,
            CleanerAuditService auditService)
        {
            _ruleService = ruleService;
            _telemetryService = telemetryService;
            _profileService = profileService;
            _stateStore = stateStore;
            _nativeFileService = nativeFileService;
            _settings = settings;
            _auditService = auditService;
            _settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CleanerSettingsViewModel.TelemetryEnabled))
                {
                    NotifyTelemetryPropertiesChanged();
                }
            };
        }

        public async Task InitializeAsync(ICleanerAssistantViewInteraction view)
        {
            _view = view;
            _ruleStatus = await _ruleService.GetStatusAsync();
            _telemetryStatus = await _telemetryService.LoadStatusAsync();
            _profileState = await _profileService.GetProfileAsync();
            _settings.UpdateFromTelemetryStatus(_telemetryStatus);
            NotifyRulePackPropertiesChanged();
            NotifyRolloutPropertiesChanged();
            NotifyTelemetryPropertiesChanged();
        }

        public async Task ReloadRuleStatusAsync()
        {
            _ruleStatus = await _ruleService.GetStatusAsync();
            NotifyRulePackPropertiesChanged();
            NotifyRulesModified();
        }

        public CleanerRuleBundleStatus RuleStatus => _ruleStatus;
        public CleanerProfileState ProfileState => _profileState;
        public bool HasExternalRulePack => _ruleStatus.HasExternalBundle;
        public bool HasLocallyDisabledRules => _ruleStatus.LocalDisabledRuleCount > 0;

        public string RulePackSummaryText => $"内置 {_ruleStatus.BuiltInRuleCount} 条 · 第三方 {_ruleStatus.ExternalRuleCount} 条 · 生效 {_ruleStatus.EffectiveRuleCount} 条";
        public string RulePackDetailText => _ruleStatus.HasExternalBundle ? "内置规则 + 第三方只读分析规则" : "当前仅使用内置规则";
        public string RulePackSourceText => string.IsNullOrWhiteSpace(_ruleStatus.BundleSource) ? "内置规则库" : _ruleStatus.BundleSource;
        public string RulePackRefreshText => _ruleStatus.LastRefreshedAt == null
            ? "尚未导入或刷新第三方规则"
            : $"最后更新：{_ruleStatus.LastRefreshedAt.Value.LocalDateTime:g}";
        public string RulePackRemoteUriText => string.IsNullOrWhiteSpace(_ruleStatus.RemoteUri) ? "未配置远程地址" : _ruleStatus.RemoteUri;
        public string RulePackHintText => _ruleStatus.HasExternalBundle
            ? "第三方规则已限制为只读分析，不能覆盖内置规则或执行删除"
            : "第三方规则导入后会在受限模式下运行";

        public string QualityGovernanceHeadlineText => "规则质量控制";
        public string QualityGovernanceDetailText => "质量分析";
        public string QualityGovernanceHintText => "无异常";
        public string QualityGovernanceActionText => "查看";

        public string RolloutSummaryText => "通道: " + _profileState.RolloutChannel;
        public string RolloutDetailText => "当前通道";
        public string RolloutHintText => "稳定";

        public string TelemetrySummaryText => _settings.TelemetryEnabled ? "已启用" : "已关闭";
        public string TelemetryDetailText => _settings.TelemetryEnabled
            ? "仅上传清理数量、空间、失败原因等摘要，不上传文件名和路径。"
            : "遥测默认关闭，不会发送任何摘要。";
        public string TelemetryEndpointText => string.IsNullOrWhiteSpace(_telemetryStatus.Endpoint)
            ? "尚未配置 HTTPS 上传地址"
            : _telemetryStatus.Endpoint;
        public string TelemetryLastUploadText => _telemetryStatus.LastUploadedAt == null
            ? _telemetryStatus.LastStatusText
            : $"上次上传：{_telemetryStatus.LastUploadedAt.Value.LocalDateTime:g} · {_telemetryStatus.LastStatusText}";

        public bool CanUploadTelemetryNow =>
            _settings.TelemetryEnabled &&
            !string.IsNullOrWhiteSpace(_telemetryStatus.Endpoint);

        public bool IsStableRolloutSelected => _profileState.RolloutChannel == "stable";
        public bool IsCanaryRolloutSelected => _profileState.RolloutChannel == "canary";
        public bool IsInternalRolloutSelected => _profileState.RolloutChannel == "internal";

        public bool CanChooseStableRollout => true;
        public bool CanChooseCanaryRollout => true;
        public bool CanChooseInternalRollout => true;

        [RelayCommand]
        private async Task RefreshRulePackFromRemote(string remoteUri)
        {
            CancellationTokenSource cts = BeginRuleRefresh();
            try
            {
                var status = await _ruleService.RefreshFromRemoteAsync(remoteUri, cts.Token);
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (OperationCanceledException)
            {
                // 用户重复刷新或离开页面时，静默结束旧请求。
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("刷新规则失败", ex.Message);
            }
            finally
            {
                EndRuleRefresh(cts);
            }
        }

        [RelayCommand]
        private async Task RestoreBuiltInRulePack()
        {
            try
            {
                var status = await _ruleService.ClearExternalRulePackAsync();
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("恢复内置规则失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task ExportDiagnosticReport()
        {
            try
            {
                string reportPath = await _auditService.ExportDiagnosticReportAsync(await _ruleService.GetKnownRulesAsync(), _ruleStatus);
                await _nativeFileService.RevealInExplorerAsync(reportPath);
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("导出诊断报告失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task EnableAllLocallyDisabledRules()
        {
            try
            {
                var status = await _ruleService.EnableAllLocallyDisabledRulesAsync();
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("恢复规则失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task DisableRuleFromCleanupEntry(CleanerCleanupEntry entry)
        {
            if (_view == null || entry == null || string.IsNullOrWhiteSpace(entry.RuleId))
            {
                return;
            }

            if (!await _view.ShowRuleDisableConfirmationAsync(entry.ItemName, entry.RuleId))
            {
                return;
            }

            try
            {
                _ruleStatus = await _ruleService.DisableRuleLocallyAsync(entry.RuleId);
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
                if (_view != null)
                {
                    await _view.ShowTipAsync("规则已停用", $"规则 {entry.RuleId} 已在本机停用。");
                }
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("停用规则失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task OpenRulePackDirectory()
        {
            try
            {
                Directory.CreateDirectory(_stateStore.RulePackDirectoryPath);
                if (!await _nativeFileService.OpenFolderAsync(_stateStore.RulePackDirectoryPath) && _view != null)
                {
                    await _view.ShowTipAsync("无法打开目录", _stateStore.RulePackDirectoryPath);
                }
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("无法打开目录", ex.Message);
            }
        }

        [RelayCommand]
        private async Task SetStableRolloutChannel() => await ChangeRolloutChannelAsync("stable");

        [RelayCommand]
        private async Task SetCanaryRolloutChannel() => await ChangeRolloutChannelAsync("canary");

        [RelayCommand]
        private async Task SetInternalRolloutChannel() => await ChangeRolloutChannelAsync("internal");

        private async Task ChangeRolloutChannelAsync(string channel)
        {
            _profileState = await _profileService.SetRolloutChannelAsync(channel);
            NotifyRolloutPropertiesChanged();
        }

        [RelayCommand]
        private async Task ConfigureTelemetryEndpoint()
        {
            if (_view == null) return;

            string? endpoint = await _view.PromptTelemetryEndpointAsync(_telemetryStatus.Endpoint);
            if (endpoint == null) return;

            try
            {
                _telemetryStatus = await _telemetryService.SaveSettingsAsync(
                    _settings.TelemetryEnabled,
                    endpoint);
                _settings.UpdateFromTelemetryStatus(_telemetryStatus);
                NotifyTelemetryPropertiesChanged();
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("遥测配置失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task UploadTelemetryNow()
        {
            try
            {
                _telemetryStatus = await _telemetryService.UploadNowAsync();
                NotifyTelemetryPropertiesChanged();
                if (_view != null) await _view.ShowTipAsync("上传完成", _telemetryStatus.LastStatusText);
            }
            catch (Exception ex)
            {
                _telemetryStatus = await _telemetryService.LoadStatusAsync();
                NotifyTelemetryPropertiesChanged();
                if (_view != null) await _view.ShowTipAsync("上传失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task ImportRulePack()
        {
            if (_view == null) return;
            string? path = await _view.PickRulePackFileAsync();
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                _ruleStatus = await _ruleService.ImportRulePackAsync(path);
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
                if (_view != null)
                {
                    await _view.ShowTipAsync("规则包已导入", "第三方规则已加载为只读分析规则，不能执行删除。");
                }
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("导入规则包失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task RefreshRulePackFromUrl()
        {
            if (_view == null) return;
            CancellationTokenSource? cts = null;
            try
            {
                string? remoteUri = await _view.PromptRulePackUrlAsync(_ruleStatus.RemoteUri);
                if (string.IsNullOrWhiteSpace(remoteUri)) return;
                cts = BeginRuleRefresh();
                var status = await _ruleService.RefreshFromRemoteAsync(remoteUri, cts.Token);
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (OperationCanceledException)
            {
                // 用户重复刷新或离开页面时，静默结束旧请求。
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("刷新规则失败", ex.Message);
            }
            finally
            {
                if (cts != null)
                {
                    EndRuleRefresh(cts);
                }
            }
        }

        [RelayCommand]
        private async Task ClearExternalRulePack()
        {
            try
            {
                var status = await _ruleService.ClearExternalRulePackAsync();
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("恢复内置规则失败", ex.Message);
            }
        }

        private void NotifyRulesModified()
        {
            RulesChanged?.Invoke(this, EventArgs.Empty);
            ScanInvalidated?.Invoke(this, EventArgs.Empty);
        }

        public void Shutdown()
        {
            lock (_ruleRefreshSync)
            {
                _ruleRefreshCts?.Cancel();
                _ruleRefreshCts = null;
            }
            _view = null;
        }

        private CancellationTokenSource BeginRuleRefresh()
        {
            CancellationTokenSource next = new();
            lock (_ruleRefreshSync)
            {
                _ruleRefreshCts?.Cancel();
                _ruleRefreshCts = next;
            }
            return next;
        }

        private void EndRuleRefresh(CancellationTokenSource cts)
        {
            lock (_ruleRefreshSync)
            {
                if (ReferenceEquals(_ruleRefreshCts, cts))
                {
                    _ruleRefreshCts = null;
                }
            }
            cts.Dispose();
        }

        private void NotifyRulePackPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasExternalRulePack));
            OnPropertyChanged(nameof(HasLocallyDisabledRules));
            OnPropertyChanged(nameof(RulePackSummaryText));
            OnPropertyChanged(nameof(RulePackDetailText));
            OnPropertyChanged(nameof(RulePackSourceText));
            OnPropertyChanged(nameof(RulePackRefreshText));
            OnPropertyChanged(nameof(RulePackRemoteUriText));
            OnPropertyChanged(nameof(RulePackHintText));
            OnPropertyChanged(nameof(QualityGovernanceHeadlineText));
            OnPropertyChanged(nameof(QualityGovernanceDetailText));
            OnPropertyChanged(nameof(QualityGovernanceHintText));
            OnPropertyChanged(nameof(QualityGovernanceActionText));
        }

        private void NotifyRolloutPropertiesChanged()
        {
            OnPropertyChanged(nameof(IsStableRolloutSelected));
            OnPropertyChanged(nameof(IsCanaryRolloutSelected));
            OnPropertyChanged(nameof(IsInternalRolloutSelected));
            OnPropertyChanged(nameof(CanChooseStableRollout));
            OnPropertyChanged(nameof(CanChooseCanaryRollout));
            OnPropertyChanged(nameof(CanChooseInternalRollout));
            OnPropertyChanged(nameof(RolloutSummaryText));
            OnPropertyChanged(nameof(RolloutDetailText));
            OnPropertyChanged(nameof(RolloutHintText));
        }

        private void NotifyTelemetryPropertiesChanged()
        {
            OnPropertyChanged(nameof(CanUploadTelemetryNow));
            OnPropertyChanged(nameof(TelemetrySummaryText));
            OnPropertyChanged(nameof(TelemetryDetailText));
            OnPropertyChanged(nameof(TelemetryEndpointText));
            OnPropertyChanged(nameof(TelemetryLastUploadText));
        }
    }
}
