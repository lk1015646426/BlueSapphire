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

            DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
            DeepSeekApiKeyBox.Password = AppSettings.GetSecret("DeepSeekApiKey") ?? string.Empty;
            DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;
        }

        private void DeepSeekApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            AppSettings.SaveSecret("DeepSeekApiKey", DeepSeekApiKeyBox.Password);
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

