using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Helpers;
using System.Reflection; // 【新增引用】
using System;
using System.Diagnostics; // 【新增引用】
using System.IO;

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page
    {
        // 【新增属性 1】用于 XAML 绑定显示的版本号
        public string AppDisplayVersion { get; private set; } = "v?.?.? (Beta)";

        // 【新增属性 2】用于 XAML 绑定的构建日期
        public string AppBuildDate { get; private set; } = "Unknown Date";

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadVersionInfo(); // 【调用新增方法】
            InitializeSettingsSafe();
        }

        // --- 新增：加载版本信息逻辑 (实现动态同步) ---
        private void LoadVersionInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                // 1. 获取版本号 (vMajor.Minor.Build)
                if (version != null)
                {
                    this.AppDisplayVersion = $"v{version.Major}.{version.Minor}.{version.Build} (Beta)";
                }

                // 2. 获取构建日期 (使用文件写入时间作为近似构建日期)
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

        // --- 安全初始化逻辑 ---
        private void InitializeSettingsSafe()
        {
            // 1. 卸载事件
            ParticleSwitch.Toggled -= ParticleSwitch_Toggled;

            // 2. 确定状态 (优先查本地文件，没有则查内存，默认为 true)
            // AppSettings.Get 会处理所有判空逻辑
            bool targetState = AppSettings.Get<bool>("IsParticleEffectEnabled", true);

            // 如果本地文件没有，但内存里有实例 (比如刚启动且没存过)，则同步内存状态
            // 这一步是为了防止文件被误删后状态不同步
            if (MainWindow.Instance != null && AppSettings.Get<bool?>("IsParticleEffectEnabled", null) == null)
            {
                targetState = MainWindow.Instance.IsParticleEffectEnabled;
            }

            // 3. 安全赋值
            ParticleSwitch.IsOn = targetState;

            // 4. 重新挂载事件
            ParticleSwitch.Toggled += ParticleSwitch_Toggled;
        }

        private void ParticleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ParticleSwitch.IsOn;

            // 更新主窗口
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.UpdateParticleSetting(isEnabled);
            }

            // 更新本地存储 (写入 json 文件)
            AppSettings.Save("IsParticleEffectEnabled", isEnabled);
        }
    }
}