using BlueSapphire.Helpers;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using BlueSapphire.Services;

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page
    {
        public string AppDisplayVersion { get; private set; } = "版本信息读取失败";
        public string AppBuildDate { get; private set; } = "构建日期读取失败";

        private int _versionTapCount;
        private readonly DispatcherTimer _clickResetTimer;

        public SettingsPage()
        {
            InitializeComponent();
            LoadVersionInfo();
            InitializeSettingsSafe();
            Bindings.Update();

            _clickResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _clickResetTimer.Tick += (_, _) =>
            {
                _versionTapCount = 0;
                _clickResetTimer.Stop();
            };
        }

        private void LoadVersionInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                if (version != null)
                {
                    AppDisplayVersion = $"版本 {version.Major}.{version.Minor}.{version.Build}";
                }

                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    AppBuildDate = File.GetLastWriteTime(assembly.Location).ToString("yyyy.MM.dd");
                }
            }
            catch
            {
                AppBuildDate = "读取异常";
            }
        }

        private void InitializeSettingsSafe()
        {
            ParticleSwitch.Toggled -= ParticleSwitch_Toggled;

            bool targetState = AppSettings.Get("IsParticleEffectEnabled", true);
            if (App.CurrentWindow is MainWindow window && AppSettings.Get<bool?>("IsParticleEffectEnabled", null) == null)
            {
                targetState = window.IsParticleEffectEnabled;
            }

            ParticleSwitch.IsOn = targetState;
            ParticleSwitch.Toggled += ParticleSwitch_Toggled;

            ApiProviderComboBox.SelectionChanged -= ApiProviderComboBox_SelectionChanged;
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
            ApiProviderComboBox.SelectedIndex = provider == "SiliconFlow" ? 1 : 0;
            ApiProviderComboBox.SelectionChanged += ApiProviderComboBox_SelectionChanged;

            DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
            DeepSeekApiKeyBox.Password = AppSettings.GetSecret($"DeepSeekApiKey_{provider}") ?? AppSettings.GetSecret("DeepSeekApiKey") ?? string.Empty;
            DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;

            ApiModelComboBox.SelectionChanged -= ApiModelComboBox_SelectionChanged;
            string savedModel = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat"));
            if (!string.IsNullOrEmpty(savedModel))
            {
                ApiModelComboBox.Items.Add(new ComboBoxItem { Content = savedModel, Tag = savedModel });
                ApiModelComboBox.SelectedIndex = 0;
            }
            ApiModelComboBox.SelectionChanged += ApiModelComboBox_SelectionChanged;
        }

        private void DeepSeekApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
            AppSettings.SaveSecret($"DeepSeekApiKey_{provider}", DeepSeekApiKeyBox.Password);
            
            // 为了兼容性，也保存一份到默认的 key 中（如果之前没有特定提供商配置，方便回滚）
            AppSettings.SaveSecret("DeepSeekApiKey", DeepSeekApiKeyBox.Password);
        }

        private void ApiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                string oldProvider = AppSettings.Get("DeepSeekApiProvider", "Official");
                if (oldProvider != tag)
                {
                    AppSettings.Save("DeepSeekApiProvider", tag);
                    
                    DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
                    DeepSeekApiKeyBox.Password = AppSettings.GetSecret($"DeepSeekApiKey_{tag}") ?? AppSettings.GetSecret("DeepSeekApiKey") ?? string.Empty;
                    DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;

                    if (ApiModelComboBox != null)
                    {
                        ApiModelComboBox.Items.Clear();
                        ApiModelComboBox.PlaceholderText = "供应商已切换，请重新获取模型";
                    }
                }
            }
        }

        private void ApiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiModelComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
                AppSettings.Save($"DeepSeekApiModel_{provider}", tag);
                AppSettings.Save("DeepSeekApiModel", tag); // For compatibility
            }
        }

        private async void RefreshModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            await FetchModelsAsync();
        }

        private async void ApiModelComboBox_DropDownOpened(object sender, object e)
        {
            if (ApiModelComboBox.Items.Count <= 1)
            {
                await FetchModelsAsync();
            }
        }

        private async Task FetchModelsAsync()
        {
            try
            {
                RefreshModelsBtn.IsEnabled = false;
                ApiModelComboBox.PlaceholderText = "正在获取模型...";
                
                string currentSelection = (ApiModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                ApiModelComboBox.Items.Clear();

                // 强制保存最新的 API Key 到对应提供商
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
                AppSettings.SaveSecret($"DeepSeekApiKey_{provider}", DeepSeekApiKeyBox.Password);

                var aiService = App.Current.Services.GetRequiredService<DeepSeekAIService>();
                var result = await aiService.GetAvailableModelsAsync();

                if (result.Models.Count > 0)
                {
                    string savedModel = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", ""));
                    int selectedIndex = -1;
                    for (int i = 0; i < result.Models.Count; i++)
                    {
                        ApiModelComboBox.Items.Add(new ComboBoxItem { Content = result.Models[i], Tag = result.Models[i] });
                        if (result.Models[i] == savedModel || result.Models[i] == currentSelection) selectedIndex = i;
                    }
                    if (selectedIndex >= 0) ApiModelComboBox.SelectedIndex = selectedIndex;
                    else ApiModelComboBox.SelectedIndex = 0;
                    
                    ApiModelComboBox.PlaceholderText = "请选择连接模型";
                }
                else
                {
                    // 将真实的报错信息显示给用户
                    ApiModelComboBox.PlaceholderText = !string.IsNullOrEmpty(result.Error) 
                        ? result.Error 
                        : "获取失败，请检查 API Key";
                }
            }
            finally
            {
                RefreshModelsBtn.IsEnabled = true;
            }
        }

        private void ParticleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ParticleSwitch.IsOn;
            WeakReferenceMessenger.Default.Send(new ToggleParticleMessage(isEnabled));
            AppSettings.Save("IsParticleEffectEnabled", isEnabled);
        }

        private async void SecretVersion_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _versionTapCount++;
            _clickResetTimer.Stop();
            _clickResetTimer.Start();

            if (_versionTapCount < 5)
            {
                TriggerMicroBounce(VersionTextBlock);
                return;
            }

            _versionTapCount = 0;
            _clickResetTimer.Stop();

            await TriggerCyberPulseEffect(VersionTextBlock);
            Frame.Navigate(typeof(Views.DevLogPage));
        }

        private void TriggerMicroBounce(TextBlock target)
        {
            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var scaleXAnimation = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

            var scaleYAnimation = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleYAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();
        }

        private async Task TriggerCyberPulseEffect(TextBlock target)
        {
            var originalBrush = target.Foreground;
            target.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);

            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new SineEase { EasingMode = EasingMode.EaseInOut };

            var scaleXAnimation = new DoubleAnimation
            {
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

            var scaleYAnimation = new DoubleAnimation
            {
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleYAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();

            await Task.Delay(300);
            target.Foreground = originalBrush;
        }

        private void SecretVersion_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }

        private void SecretVersion_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }
    }
}

