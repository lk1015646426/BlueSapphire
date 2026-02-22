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
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page
    {
        public string AppDisplayVersion { get; private set; } = "v?.?.? (Beta)";
        public string AppBuildDate { get; private set; } = "Unknown Date";

        // 用于记录点击次数的秘密计数器
        private int _versionTapCount = 0;
        // 防误触的连击重置计时器
        private DispatcherTimer _clickResetTimer;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadVersionInfo();
            InitializeSettingsSafe();

            // 初始化防误触的连击重置计时器 (800毫秒内不连击则清零)
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
                    this.AppDisplayVersion = $"v{version.Major}.{version.Minor}.{version.Build} (Beta)";
                }

                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    var buildDate = File.GetLastWriteTime(assembly.Location);
                    this.AppBuildDate = $"构建日期: {buildDate:yyyy-MM-dd HH:mm}";
                }
                else
                {
                    this.AppBuildDate = "构建日期: N/A";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading version info: {ex.Message}");
                this.AppBuildDate = "构建日期: 无法读取";
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

        // 【核心修复】使用 PointerPressed 替代 Tapped，彻底解决系统“吞连击”的问题
        private async void SecretVersion_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _versionTapCount++;

            // 重置计时器
            _clickResetTimer.Stop();
            _clickResetTimer.Start();

            if (_versionTapCount < 5)
            {
                // 前 4 次点击：触发干脆的“微回弹”动效，让每一次点击都有视觉响应
                TriggerMicroBounce(VersionTextBlock);
            }
            else
            {
                // 第 5 次点击：触发“赛博心跳”，重置计数器并跳转
                _versionTapCount = 0;
                _clickResetTimer.Stop();

                await TriggerCyberPulseEffect(VersionTextBlock);

                // 动画表现完毕后，优雅地导航到隐藏页面
                this.Frame.Navigate(typeof(Views.DevLogPage));
            }
        }

        // 新增：极速“微回弹”动效 (用于前 4 次点击的实时反馈)
        private void TriggerMicroBounce(TextBlock target)
        {
            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            // 仅仅极快地放大 8%，不改变颜色，营造机械按键般的干脆反馈
            var scaleXAnim = new DoubleAnimation
            {
                To = 1.08,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

            var scaleYAnim = new DoubleAnimation
            {
                To = 1.08,
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

        // 纯 C# 实现的极简 "赛博心跳" 动效 (用于第 5 次解锁成功)
        private async Task TriggerCyberPulseEffect(TextBlock target)
        {
            var originalBrush = target.Foreground;

            // 解锁瞬间高亮为青色
            target.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Cyan);

            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new SineEase { EasingMode = EasingMode.EaseInOut };

            // 解锁时放大 15%，并且时间稍长，带有一点阻尼感
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

        // 鼠标悬停变为小手
        private void SecretVersion_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }

        // 鼠标移出恢复箭头
        private void SecretVersion_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }
    }
}