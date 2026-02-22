using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;
using System.Reflection;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page
    {
        public string AppDisplayVersion { get; private set; } = "版本加载失败";
        public string AppBuildDate { get; private set; } = "日期加载失败";

        private int _versionTapCount = 0;
        private DispatcherTimer _clickResetTimer;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadVersionInfo();
            InitializeSettingsSafe();

            _clickResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _clickResetTimer.Tick += (s, e) =>
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
                    this.AppDisplayVersion = $"版本 {version.Major}.{version.Minor}.{version.Build}";
                }

                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    var buildDate = File.GetLastWriteTime(assembly.Location);
                    this.AppBuildDate = $"{buildDate:yyyy.MM.dd}";
                }
            }
            catch
            {
                this.AppBuildDate = "解析异常";
            }
        }

        private void InitializeSettingsSafe()
        {
            ParticleSwitch.Toggled -= ParticleSwitch_Toggled;

            bool targetState = AppSettings.Get<bool>("IsParticleEffectEnabled", true);

            if (App.CurrentWindow is MainWindow mw && AppSettings.Get<bool?>("IsParticleEffectEnabled", null) == null)
            {
                targetState = mw.IsParticleEffectEnabled;
            }

            ParticleSwitch.IsOn = targetState;
            ParticleSwitch.Toggled += ParticleSwitch_Toggled;
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
            }
            else
            {
                _versionTapCount = 0;
                _clickResetTimer.Stop();

                await TriggerCyberPulseEffect(VersionTextBlock);

                this.Frame.Navigate(typeof(Views.DevLogPage));
            }
        }

        private void TriggerMicroBounce(TextBlock target)
        {
            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var scaleXAnim = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

            var scaleYAnim = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleYAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

            storyboard.Children.Add(scaleXAnim);
            storyboard.Children.Add(scaleYAnim);

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

            var scaleXAnim = new DoubleAnimation
            {
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

            var scaleYAnim = new DoubleAnimation
            {
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleYAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

            storyboard.Children.Add(scaleXAnim);
            storyboard.Children.Add(scaleYAnim);

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