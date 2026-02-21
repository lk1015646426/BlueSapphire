using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;
using System.Reflection;
using System;
using System.Diagnostics;
using System.IO;

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page
    {
        public string AppDisplayVersion { get; private set; } = "v?.?.? (Beta)";
        public string AppBuildDate { get; private set; } = "Unknown Date";

        // 用于记录点击次数的秘密计数器
        private int _versionTapCount = 0;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadVersionInfo();
            InitializeSettingsSafe();
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

            // [修复] 使用 App.CurrentWindow 替代 MainWindow.Instance
            // 安全地尝试将当前窗口转换为 MainWindow 类型
            if (App.CurrentWindow is MainWindow mw && AppSettings.Get<bool?>("IsParticleEffectEnabled", null) == null)
            {
                // 直接读取属性
                targetState = mw.IsParticleEffectEnabled;
            }

            ParticleSwitch.IsOn = targetState;
            ParticleSwitch.Toggled += ParticleSwitch_Toggled;
        }

        private void ParticleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ParticleSwitch.IsOn;

            // [修复] 不再直接调用方法，而是发送解耦消息
            // MainWindow 里的 Messenger 会接收到这个消息并处理
            WeakReferenceMessenger.Default.Send(new ToggleParticleMessage(isEnabled));

            AppSettings.Save("IsParticleEffectEnabled", isEnabled);
        }

        // 处理连续点击解锁的彩蛋事件
        private void SecretVersion_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _versionTapCount++;

            // 播放点击动效
            VersionClickStoryboard.Begin();

            // 在界面上临时改变文字，营造黑客感
            if (_versionTapCount > 0 && _versionTapCount < 5)
            {
                VersionTextBlock.Text = $"DECRYPTING... [{_versionTapCount}/5]";
            }

            // 连按 5 次触发彩蛋，进入赛博极客控制台
            if (_versionTapCount >= 5)
            {
                _versionTapCount = 0; // 重置计数器
                VersionTextBlock.Text = "ACCESS GRANTED"; // 解锁成功提示

                // 短暂延迟后进入极客页面，让用户看清“解锁成功”
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    // 导航到隐藏的 DevLogPage
                    this.Frame.Navigate(typeof(Views.DevLogPage));
                };
                timer.Start();
            }
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