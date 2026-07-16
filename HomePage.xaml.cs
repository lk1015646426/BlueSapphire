using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Reflection;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using BlueSapphire.Services;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;

namespace BlueSapphire
{
    public sealed partial class HomePage : Page
    {
        private bool _isLoaded;
        private readonly string[] HourlyGreetings =
        {
            "深夜引擎仍在运转，保持节奏就好。",
            "还在加班的话，记得给自己留一点空档。",
            "你写下的每一行代码，都在决定系统的边界。",
            "现在适合收尾，而不是继续堆复杂度。",
            "天快亮了，先把关键路径理顺。",
            "新的周期开始了，先做最重要的一件事。",
            "上午适合处理需要判断力的工作。",
            "系统稳定运行中，可以继续推进主线任务。",
            "状态已经拉起来了，保持专注。",
            "开始进入高效区间，把复杂问题拆开。",
            "进度不错，别忘了校验边界条件。",
            "接近中午，适合做一次短暂整理。",
            "午间时段，先收束再扩展。",
            "下午适合清理债务和补测试。",
            "继续推进，但不要放过隐性耦合。",
            "如果卡住了，先缩小问题范围。",
            "傍晚到了，适合做一轮回顾。",
            "快到下班时间了，把收尾做完整。",
            "抬头看一眼进度，再决定下一步。",
            "一天的工作快闭环了，别让细节漏掉。",
            "夜间模式启动，适合处理安静但重要的事情。",
            "晚一些没关系，方向要对。",
            "夜色很深了，系统依旧平稳。",
            "今天快结束了，把最后一件事做好。"
        };

        public HomePage()
        {
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<ToggleReducedMotionMessage>(
                this,
                (_, message) => DispatcherQueue.TryEnqueue(() =>
                    ApplyReducedMotion(message.Value)));
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            UpdateHourlyGreeting();
            await UpdateStatusAsync();
            ApplyReducedMotion(AppSettings.Get("ReduceMotion", false));
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            CoreIdleAnimation.Stop();
            EntranceStoryboard.Stop();
        }

        private void ApplyReducedMotion(bool reduceMotion)
        {
            if (!_isLoaded) return;
            if (reduceMotion)
            {
                CoreIdleAnimation.Stop();
                EntranceStoryboard.Stop();
                ContentStack.Opacity = 1;
                ContentTranslate.Y = 0;
                CoreScale.ScaleX = 1;
                CoreScale.ScaleY = 1;
                CoreTranslate.Y = 0;
                return;
            }

            CoreIdleAnimation.Begin();
            EntranceStoryboard.Begin();
        }

        private async Task UpdateStatusAsync()
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            string? apiKey = AppSettings.GetSecret($"DeepSeekApiKey_{provider}");
            if (string.IsNullOrWhiteSpace(apiKey) &&
                string.Equals(provider, "Official", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = AppSettings.GetSecret("DeepSeekApiKey");
            }
            AIStatusText.Text = string.IsNullOrWhiteSpace(apiKey)
                ? "AI 服务：尚未配置 API 密钥"
                : $"AI 服务：已配置{(provider == "SiliconFlow" ? "硅基流动" : "DeepSeek 官方")}接口";

            try
            {
                CleanerAuditSnapshot snapshot = await App.Current.Services
                    .GetRequiredService<CleanerAuditService>()
                    .LoadSnapshotAsync();
                CleanerStatusText.Text = snapshot.TotalScans == 0
                    ? "清理助手：尚无扫描记录"
                    : $"清理助手：{snapshot.ScanSummaryText} · 累计释放 {CleanerSizeFormatter.Format(snapshot.TotalReleasedBytes)}";
            }
            catch
            {
                CleanerStatusText.Text = "清理助手：状态暂不可用";
            }

            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                .Split('+')[0]
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                ?? "未知";
            ReleaseStatusText.Text = $"版本：{version} · .NET 8 · Windows x64";
        }

        private void OpenTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string toolId } &&
                App.CurrentWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToTool(toolId);
            }
        }

        private void UpdateHourlyGreeting()
        {
            int hour = DateTime.Now.Hour;
            if (hour >= 0 && hour < HourlyGreetings.Length)
            {
                GreetingText.Text = HourlyGreetings[hour];
            }
        }
    }
}
