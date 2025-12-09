using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Helpers; // 【必须引用】引用 Helpers

// 注意：移除了 Windows.Storage

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            InitializeSettingsSafe();
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