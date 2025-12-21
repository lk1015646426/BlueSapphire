using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Helpers;
using BlueSapphire.Models; // 必须引用，用于使用 ToggleParticleMessage
using CommunityToolkit.Mvvm.Messaging; // 必须引用，用于发送消息
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
    }
}