using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class AIOfflineIntentService
    {
        private readonly AITaskCenterService _taskCenter;
        private readonly AISharedContextService _sharedContext;

        public AIOfflineIntentService(
            AITaskCenterService taskCenter,
            AISharedContextService sharedContext)
        {
            _taskCenter = taskCenter;
            _sharedContext = sharedContext;
        }

        public Task<(bool Handled, string Response)> TryHandleAsync(string input)
        {
            string text = (input ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return Task.FromResult((false, string.Empty));
            }

            if (ContainsAny(text, "快速扫描", "扫描垃圾", "检查缓存"))
            {
                Navigate("CleanerAssistant");
                WeakReferenceMessenger.Default.Send(new StartQuickScanMessage());
                return Task.FromResult((
                    true,
                    "当前大模型不可用，我已使用本地指令启动快速扫描。扫描和风险判断都在本机完成。"));
            }

            if (ContainsAny(text, "打开清理", "清理助手"))
            {
                Navigate("CleanerAssistant");
                return Task.FromResult((true, "已打开清理助手。"));
            }

            if (ContainsAny(text, "打开媒体", "媒体管家", "图片管理"))
            {
                Navigate("MediaManager");
                return Task.FromResult((true, "已打开媒体管家。"));
            }

            if (ContainsAny(text, "打开设置", "设置页面"))
            {
                Navigate("Settings");
                return Task.FromResult((true, "已打开设置。"));
            }

            if (ContainsAny(text, "任务中心", "后台任务", "任务状态"))
            {
                var tasks = _taskCenter.GetSnapshot();
                if (tasks.Count == 0)
                {
                    return Task.FromResult((true, "当前没有后台任务。"));
                }

                string summary = string.Join(
                    Environment.NewLine,
                    tasks.Take(8).Select(task =>
                        $"- {task.Title}：{task.StatusText}，{task.ProgressText}，{task.Summary}"));
                return Task.FromResult((true, $"当前任务：\n{summary}"));
            }

            if (ContainsAny(text, "最近扫描", "扫描结果", "能清理多少"))
            {
                CleanerScanReport? scan = _sharedContext.GetCleanerScan(TimeSpan.FromMinutes(30));
                if (scan == null)
                {
                    return Task.FromResult((true, "当前没有 30 分钟内的有效扫描结果。"));
                }

                long safeBytes = scan.Items
                    .Where(item => item.RiskLevel == CleanerRiskLevel.Low)
                    .Sum(item => item.SizeBytes);
                long reviewBytes = scan.Items
                    .Where(item => item.RiskLevel == CleanerRiskLevel.Medium)
                    .Sum(item => item.SizeBytes);
                return Task.FromResult((
                    true,
                    $"最近扫描发现：低风险 {CleanerSizeFormatter.Format(safeBytes)}，建议确认 {CleanerSizeFormatter.Format(reviewBytes)}。离线模式不会执行删除。"));
            }

            return Task.FromResult((
                false,
                "当前大模型不可用。本地模式支持打开功能、快速扫描、查看最近扫描摘要和后台任务。"));
        }

        private static bool ContainsAny(string text, params string[] candidates) =>
            candidates.Any(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase));

        private static void Navigate(string feature)
        {
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToTool(feature);
            }
        }
    }
}
