using BlueSapphire.Controls;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
            Unloaded += CleanerAssistantPage_Unloaded;
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
                if (!BlueSapphire.Helpers.AppSettings.Get("ReduceMotion", false))
                {
                    CardEntrance.Begin();
                }
                ViewModel.Scan.PropertyChanged += Scan_PropertyChanged;
                await ViewModel.InitializeAsync(this);

                // Default to first settings section
                SettingsNav.SelectedIndex = 0;
                HistoryNav.SelectedIndex = 0;
                MainTabBar.SelectedIndex = 0;

                UpdateDonutChart();
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

        private void CleanerAssistantPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Scan.PropertyChanged -= Scan_PropertyChanged;
            ViewModel.Shutdown();
            _isInitialized = false;
        }

        private void Scan_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(ViewModel.Scan.SafeSpaceRatio) or
                                        nameof(ViewModel.Scan.ReviewSpaceRatio) or
                                        nameof(ViewModel.Scan.ViewOnlySpaceRatio) or
                                        nameof(ViewModel.Scan.TotalCleanableSpaceBytes) or
                                        nameof(ViewModel.Scan.CurrentScanState) or
                                        nameof(ViewModel.Scan.HasResults)))
                return;

            DispatcherQueue.TryEnqueue(UpdateDonutChart);
        }

        private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListView lv) return;
            for (int i = 0; i < SettingsContentPanel.Children.Count; i++)
            {
                if (SettingsContentPanel.Children[i] is FrameworkElement fe)
                {
                    fe.Visibility = i == lv.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void HistoryNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListView lv) return;
            for (int i = 0; i < HistoryContentPanel.Children.Count; i++)
            {
                if (HistoryContentPanel.Children[i] is FrameworkElement fe)
                {
                    fe.Visibility = i == lv.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void MainTabBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListView lv) return;
            Tab1Content.Visibility = lv.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            Tab2Content.Visibility = lv.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            Tab3Content.Visibility = lv.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateDonutChart()
        {
            if (ViewModel.Scan.CurrentScanState == CleanerScanState.Idle)
            {
                DonutChart.SetDisplayMode(DonutChart.DonutDisplayMode.Idle);
                return;
            }

            if (ViewModel.Scan.CurrentScanState == CleanerScanState.Completed && !ViewModel.Scan.HasResults)
            {
                DonutChart.SetDisplayMode(DonutChart.DonutDisplayMode.NoResults);
                return;
            }

            DonutChart.SetDisplayMode(DonutChart.DonutDisplayMode.Data);
            DonutChart.Update(
                ViewModel.Scan.SafeItemSpaceBytes,
                ViewModel.Scan.ReviewItemSpaceBytes,
                ViewModel.Scan.ViewOnlyItemSpaceBytes,
                ViewModel.Scan.TotalCleanableSpaceText,
                ViewModel.Scan.SafeSpaceText,
                ViewModel.Scan.ReviewSpaceText,
                ViewModel.Scan.ViewOnlySpaceText);
        }

        private void ItemRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Models.CleanerScanItem item)
            {
                ViewModel.SelectedScanItem = item;
            }
        }

        private void ApplyDialogStyle(ContentDialog dialog)
        {
            dialog.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PanelSurfaceStrong"];
            dialog.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentInspect"];
            dialog.BorderThickness = new Thickness(1);
            dialog.CornerRadius = new CornerRadius(16);
            dialog.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMain"];
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
            ApplyDialogStyle(dialog);

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
            ApplyDialogStyle(dialog);

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
            ApplyDialogStyle(dialog);

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
            ApplyDialogStyle(dialog);

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
            ApplyDialogStyle(dialog);

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
            ApplyDialogStyle(dialog);

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? input.Text.Trim()
                : null;
        }
    }
}
