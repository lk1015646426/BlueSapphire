using BlueSapphire.Controls;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
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
        private bool _reduceMotion;

        public CleanerAssistantViewModel ViewModel { get; }

        public CleanerAssistantPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            ViewModel = App.Current.Services.GetRequiredService<CleanerAssistantViewModel>();
            InitializeComponent();
            Loaded += CleanerAssistantPage_Loaded;
            Unloaded += CleanerAssistantPage_Unloaded;
        }

        private async void CleanerAssistantPage_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Scan.PropertyChanged -= Scan_PropertyChanged;
            ViewModel.Scan.PropertyChanged += Scan_PropertyChanged;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _reduceMotion = BlueSapphire.Helpers.AppSettings.Get("ReduceMotion", false);
            WeakReferenceMessenger.Default.Unregister<ToggleReducedMotionMessage>(this);
            WeakReferenceMessenger.Default.Register<ToggleReducedMotionMessage>(this, (_, message) =>
                DispatcherQueue.TryEnqueue(() => ApplyReducedMotion(message.Value)));

            if (_isInitialized)
            {
                UpdateDonutChart();
                UpdateCleanupMotionState();
                return;
            }

            _isInitialized = true;
            try
            {
                if (!_reduceMotion)
                {
                    CardEntrance.Begin();
                }
                else
                {
                    ScanContentPanel.Opacity = 1;
                    ScanContentTranslate.Y = 0;
                }
                await ViewModel.InitializeAsync(this);

                // Default to first settings section
                SettingsNav.SelectedIndex = 0;
                HistoryNav.SelectedIndex = 0;
                MainTabBar.SelectedIndex = 0;

                UpdateDonutChart();
                UpdateCleanupMotionState();
            }
            catch (Exception ex)
            {
                await ShowTipAsync("清理工具初始化失败", ex.Message);
            }
            finally
            {
                // Initialization completed
            }
        }

        private void CleanerAssistantPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Scan.PropertyChanged -= Scan_PropertyChanged;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            WeakReferenceMessenger.Default.Unregister<ToggleReducedMotionMessage>(this);
        }

        private void ApplyReducedMotion(bool reduceMotion)
        {
            _reduceMotion = reduceMotion;
            CleanupProgressRing.IsActive = ViewModel.IsCleanupRunning && !_reduceMotion;
            if (!_reduceMotion)
            {
                return;
            }

            CardEntrance.Stop();
            CleanupEnterStoryboard.Stop();
            CleanupOutcomeStoryboard.Stop();
            ScanContentPanel.Opacity = 1;
            ScanContentTranslate.Y = 0;
            CleanupExperience.Opacity = 1;
            CleanupCardTransform.TranslateY = 0;
            CleanupCardTransform.ScaleX = 1;
            CleanupCardTransform.ScaleY = 1;
            CleanupOutcomePanel.Opacity = 1;
            CleanupOutcomeTranslate.Y = 0;
            CleanupOutcomeIconScale.ScaleX = 1;
            CleanupOutcomeIconScale.ScaleY = 1;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.IsCleanupRunning))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    CleanupProgressRing.IsActive = ViewModel.IsCleanupRunning && !_reduceMotion;
                });
            }

            if (e.PropertyName == nameof(ViewModel.IsCleanupExperienceVisible) && ViewModel.IsCleanupExperienceVisible)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_reduceMotion)
                    {
                        CleanupExperience.Opacity = 1;
                        CleanupCardTransform.TranslateY = 0;
                        CleanupCardTransform.ScaleX = 1;
                        CleanupCardTransform.ScaleY = 1;
                    }
                    else
                    {
                        CleanupEnterStoryboard.Begin();
                    }
                });
            }

            if (e.PropertyName == nameof(ViewModel.IsCleanupOutcomeVisible) && ViewModel.IsCleanupOutcomeVisible)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    CleanupProgressRing.IsActive = false;
                    if (_reduceMotion)
                    {
                        CleanupOutcomePanel.Opacity = 1;
                        CleanupOutcomeTranslate.Y = 0;
                        CleanupOutcomeIconScale.ScaleX = 1;
                        CleanupOutcomeIconScale.ScaleY = 1;
                    }
                    else
                    {
                        CleanupOutcomeStoryboard.Begin();
                    }
                });
            }
        }

        private void UpdateCleanupMotionState()
        {
            CleanupProgressRing.IsActive = ViewModel.IsCleanupRunning && !_reduceMotion;
            if (!ViewModel.IsCleanupExperienceVisible)
            {
                return;
            }

            CleanupExperience.Opacity = 1;
            CleanupCardTransform.TranslateY = 0;
            CleanupCardTransform.ScaleX = 1;
            CleanupCardTransform.ScaleY = 1;
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
                ViewModel.Scan.SystemItemSpaceBytes,
                ViewModel.Scan.ViewOnlyItemSpaceBytes,
                ViewModel.Scan.TotalDetectedSpaceText);
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
            dialog.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderColor"];
            dialog.BorderThickness = new Thickness(1);
            dialog.CornerRadius = new CornerRadius(12);
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

        public async Task<bool> ShowCleanupConfirmationAsync(CleanerCleanupPlanSummary plan)
        {
            StackPanel content = new()
            {
                Spacing = 10,
                MaxWidth = 480
            };

            Grid summary = new() { ColumnSpacing = 16 };
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel summaryText = new() { Spacing = 2 };
            summaryText.Children.Add(new TextBlock
            {
                Text = "本次计划处理",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextMuted"]
            });
            summaryText.Children.Add(new TextBlock
            {
                Text = $"{plan.ItemCount} 项",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextMain"]
            });
            TextBlock total = new()
            {
                Text = CleanerSizeFormatter.Format(plan.TotalBytes),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentPrimary"],
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(total, 1);
            summary.Children.Add(summaryText);
            summary.Children.Add(total);
            content.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["PanelSurface"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Child = summary
            });

            if (plan.PermanentItemCount > 0)
            {
                AddCleanupPlanRow(content, "永久处理", $"{plan.PermanentItemCount} 项 · {CleanerSizeFormatter.Format(plan.PermanentBytes)} · 立即释放，不可恢复", "AccentReview", "AccentReviewBg");
            }
            if (plan.QuarantineItemCount > 0)
            {
                AddCleanupPlanRow(content, "隔离暂存", $"{plan.QuarantineItemCount} 项 · {CleanerSizeFormatter.Format(plan.QuarantineBytes)} · 可恢复，暂不释放空间", "AccentSafe", "AccentSafeBg");
            }
            if (plan.RecycleItemCount > 0)
            {
                AddCleanupPlanRow(content, "移入回收站", $"{plan.RecycleItemCount} 项 · {CleanerSizeFormatter.Format(plan.RecycleBytes)} · 清空回收站后释放", "AccentInspect", "AccentInspectBg");
            }
            if (plan.SystemItemCount > 0)
            {
                AddCleanupPlanRow(content, "Windows 专用处理", $"{plan.SystemItemCount} 项 · 当前占用 {CleanerSizeFormatter.Format(plan.SystemBytes)} · 以系统返回结果为准", "AccentPrimary", "AccentPrimaryBg");
            }

            if (plan.ReviewItemCount + plan.RequiresElevationItemCount + plan.LockedItemCount > 0)
            {
                string attention = string.Join("；", new[]
                {
                    plan.ReviewItemCount > 0 ? $"{plan.ReviewItemCount} 项需要确认" : string.Empty,
                    plan.RequiresElevationItemCount > 0 ? $"{plan.RequiresElevationItemCount} 项需要管理员权限" : string.Empty,
                    plan.LockedItemCount > 0 ? $"{plan.LockedItemCount} 项可能被占用" : string.Empty
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                AddCleanupPlanRow(content, "执行前注意", attention, "AccentReview", "AccentReviewBg");
            }

            ContentDialog dialog = new()
            {
                Title = "核对清理计划",
                Content = content,
                PrimaryButtonText = plan.HasIrreversibleItems ? "确认并执行" : "执行计划",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            ApplyDialogStyle(dialog);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<bool> ShowScanReminderConfirmationAsync()
        {
            ContentDialog dialog = new()
            {
                Title = "该检查磁盘空间了",
                Content = "现在只会开始快速扫描，不会删除任何内容。扫描完成后，你仍然可以逐项查看和决定。",
                PrimaryButtonText = "开始扫描",
                CloseButtonText = "稍后提醒",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            ApplyDialogStyle(dialog);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static void AddCleanupPlanRow(
            Panel target,
            string title,
            string detail,
            string accentResource,
            string backgroundResource)
        {
            Grid row = new() { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border dot = new()
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = (Brush)Application.Current.Resources[accentResource],
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 0, 0)
            };
            StackPanel copy = new() { Spacing = 1 };
            copy.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextMain"]
            });
            copy.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextSecondary"]
            });
            Grid.SetColumn(copy, 1);
            row.Children.Add(dot);
            row.Children.Add(copy);
            target.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources[backgroundResource],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Child = row
            });
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

        public async Task<bool> ShowPurgeQuarantineConfirmationAsync(int itemCount, long sizeBytes)
        {
            ContentDialog dialog = new()
            {
                Title = "永久清空隔离区",
                Content = $"将永久删除隔离区中的 {itemCount} 项，共 {CleanerSizeFormatter.Format(sizeBytes)}。\n\n这些内容删除后无法恢复，完成后才会真正释放对应磁盘空间。",
                PrimaryButtonText = "永久删除",
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
