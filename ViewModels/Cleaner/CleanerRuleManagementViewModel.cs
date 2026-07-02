using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;
using BlueSapphire.Services;
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

        private CleanerRuleBundleStatus _ruleStatus = new();
        private CleanerTelemetryStatus _telemetryStatus = new();
        private CleanerProfileState _profileState = new();

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
        }

        public async Task InitializeAsync()
        {
            _ruleStatus = await _ruleService.GetStatusAsync();
            _telemetryStatus = await _telemetryService.LoadStatusAsync();
            _profileState = await _profileService.GetProfileAsync();
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

        public string RulePackSummaryText => $"内置 {_ruleStatus.BuiltInRuleCount} 条 · 本地停用 {_ruleStatus.LocalDisabledRuleCount} 条";
        public string RulePackDetailText => "规则库";
        public string RulePackSourceText => _ruleStatus.BundleSource;
        public string RulePackRefreshText => "最后更新：" + DateTime.Now.ToString("g");
        public string RulePackRemoteUriText => "https://example.com/rules";
        public string RulePackHintText => "最新规则";

        public string QualityGovernanceHeadlineText => "规则质量控制";
        public string QualityGovernanceDetailText => "质量分析";
        public string QualityGovernanceHintText => "无异常";
        public string QualityGovernanceActionText => "查看";

        public string RolloutSummaryText => "通道: " + _profileState.RolloutChannel;
        public string RolloutDetailText => "当前通道";
        public string RolloutHintText => "稳定";

        public string TelemetrySummaryText => "遥测";
        public string TelemetryDetailText => "启用状态: " + _telemetryStatus.Enabled;
        public string TelemetryEndpointText => _telemetryStatus.Endpoint;
        public string TelemetryLastUploadText => "上次上传：" + _telemetryStatus.LastUploadedAt?.ToString("g");

        public bool CanUploadTelemetryNow => _telemetryStatus.Enabled;

        public bool IsStableRolloutSelected => _profileState.RolloutChannel == "stable";
        public bool IsCanaryRolloutSelected => _profileState.RolloutChannel == "canary";
        public bool IsInternalRolloutSelected => _profileState.RolloutChannel == "internal";

        public bool CanChooseStableRollout => true;
        public bool CanChooseCanaryRollout => true;
        public bool CanChooseInternalRollout => true;

        [RelayCommand]
        private async Task RefreshRulePackFromRemote(string remoteUri)
        {
            try
            {
                var status = await _ruleService.RefreshFromRemoteAsync(remoteUri, CancellationToken.None);
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (Exception)
            {
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
            catch (Exception)
            {
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
            catch (Exception)
            {
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
            catch (Exception)
            {
            }
        }

        [RelayCommand]
        private async Task DisableRuleFromCleanupEntry(CleanerCleanupEntry entry)
        {
        }

        [RelayCommand]
        private async Task OpenRulePackDirectory()
        {
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
        }

        [RelayCommand]
        private async Task UploadTelemetryNow()
        {
            _telemetryStatus = await _telemetryService.UploadNowAsync();
            NotifyTelemetryPropertiesChanged();
        }

        [RelayCommand]
        private async Task ImportRulePack()
        {
            // 规则包导入需要视图层提供文件选择器交互。
            // 当前 VM 未持有视图引用，命令为预留桩。
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task RefreshRulePackFromUrl()
        {
            try
            {
                string remoteUri = _ruleStatus.RemoteUri;
                if (string.IsNullOrWhiteSpace(remoteUri))
                {
                    remoteUri = "https://example.com/rules";
                }
                var status = await _ruleService.RefreshFromRemoteAsync(remoteUri, CancellationToken.None);
                _ruleStatus = status;
                NotifyRulePackPropertiesChanged();
                NotifyRulesModified();
            }
            catch (Exception)
            {
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
            catch (Exception)
            {
            }
        }

        private void NotifyRulesModified()
        {
            RulesChanged?.Invoke(this, EventArgs.Empty);
            ScanInvalidated?.Invoke(this, EventArgs.Empty);
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
