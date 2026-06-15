using BlueSapphire.Interfaces;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace BlueSapphire
{
    public sealed partial class CleanerAssistantPage : Page, ICleanerAssistantViewInteraction
    {
        private bool _isInitialized;

        public CleanerAssistantViewModel ViewModel { get; }

        public CleanerAssistantPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<CleanerAssistantViewModel>();
            InitializeComponent();
            Loaded += CleanerAssistantPage_Loaded;
        }

        private async void CleanerAssistantPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            try
            {
                await ViewModel.InitializeAsync(this);
            }
            catch (Exception ex)
            {
                await ShowTipAsync("清理助手初始化失败", ex.Message);
            }
            finally
            {
                // Initialization completed
            }
        }


        public async Task ShowTipAsync(string title, string message)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        public async Task<bool> ShowCleanupConfirmationAsync(int count, string sizeText, bool includesReviewItems)
        {
            string hint = includesReviewItems
                ? "本次包含建议确认项，系统会优先使用隔离区以保留恢复能力。"
                : "本次主要是低风险项，系统会优先使用保守删除策略。";

            ContentDialog dialog = new()
            {
                Title = "确认执行清理",
                Content = $"本次将处理 {count} 项对象，预计释放 {sizeText}。\n\n{hint}",
                PrimaryButtonText = "开始清理",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<bool> ShowRestoreConfirmationAsync(string summaryText)
        {
            ContentDialog dialog = new()
            {
                Title = "恢复最近一次清理",
                Content = $"{summaryText}\n\n恢复时会优先回写原路径，若目标已存在，则自动改名保留。",
                PrimaryButtonText = "开始恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<bool> ShowRuleDisableConfirmationAsync(string ruleName, string ruleId)
        {
            ContentDialog dialog = new()
            {
                Title = "停用问题规则",
                Content = $"将本地停用规则“{ruleName}”。\n\n规则 ID：{ruleId}\n\n停用后，该规则不会再参与后续扫描和清理，你仍然可以在质量治理卡片里恢复它。",
                PrimaryButtonText = "确认停用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<string?> PickRulePackFileAsync()
        {
            FileOpenPicker picker = new();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        public async Task<string?> PromptRulePackUrlAsync(string? currentUrl)
        {
            TextBox input = new()
            {
                PlaceholderText = "https://example.com/cleaner-rules.json",
                Text = currentUrl ?? string.Empty,
                AcceptsReturn = false
            };

            ContentDialog dialog = new()
            {
                Title = "从链接刷新规则包",
                Content = input,
                PrimaryButtonText = "开始刷新",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? input.Text.Trim()
                : null;
        }

        public async Task<string?> PromptTelemetryEndpointAsync(string? currentUrl)
        {
            TextBox input = new()
            {
                PlaceholderText = "https://example.com/cleaner-telemetry",
                Text = currentUrl ?? string.Empty,
                AcceptsReturn = false
            };

            ContentDialog dialog = new()
            {
                Title = "配置云端遥测地址",
                Content = input,
                PrimaryButtonText = "保存地址",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? input.Text.Trim()
                : null;
        }
    }
}
