using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;

namespace BlueSapphire.Services
{
    public class AIToolsRegistry
    {
        private readonly DeepSeekAIService _aiService;

        public AIToolsRegistry(DeepSeekAIService aiService)
        {
            _aiService = aiService;
        }

        public async Task<string> ExecuteToolCallAsync(string toolCallJson)
        {
            try
            {
                var doc = JsonDocument.Parse(toolCallJson);
                var calls = doc.RootElement.EnumerateArray();
                
                foreach (var call in calls)
                {
                    var function = call.GetProperty("function");
                    var name = function.GetProperty("name").GetString();
                    var args = function.GetProperty("arguments").GetString() ?? "{}";

                    if (name == "start_smart_cleanup")
                    {
                        return await StartSmartCleanupAsync(args);
                    }
                    else if (name == "analyze_latest_cleanup_log")
                    {
                        return await AnalyzeLatestCleanupLogAsync();
                    }
                    else if (name == "execute_cleanup")
                    {
                        return await ExecuteCleanupAsync(args);
                    }
                    else if (name == "navigate_to_feature")
                    {
                        return await NavigateToFeatureAsync(args);
                    }
                }
                return "未找到对应的指令。";
            }
            catch (Exception ex)
            {
                return $"执行指令失败: {ex.Message}";
            }
        }

        private async Task<string> StartSmartCleanupAsync(string args)
        {
            // First navigate to CleanerAssistant
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToTool("CleanerAssistantTool");
                
                // Wait for the UI and ViewModel to initialize
                await Task.Delay(500);

                // Send a message to ViewModel to start scan and await the result
                var result = await WeakReferenceMessenger.Default.Send(new StartQuickScanMessage());
                return $"扫描已完成。扫描结果：\n{result}";
            }
            return "无法获取主窗口句柄，导航失败。";
        }

        private async Task<string> ExecuteCleanupAsync(string args)
        {
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToTool("CleanerAssistantTool");
                await Task.Delay(500);

                var result = await WeakReferenceMessenger.Default.Send(new RunCleanupMessage());
                return $"清理已完成。清理结果：\n{result}";
            }
            return "无法获取主窗口句柄，导航失败。";
        }

        private async Task<string> AnalyzeLatestCleanupLogAsync()
        {
            try
            {
                var auditDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire", "Audits");
                if (!Directory.Exists(auditDir)) return "尚未发现任何清理记录。";

                var files = Directory.GetFiles(auditDir, "cleanup-*.json");
                if (files.Length == 0) return "尚未发现任何清理记录。";

                var latestFile = files.OrderByDescending(f => f).First();
                var json = await File.ReadAllTextAsync(latestFile);
                
                // Return a simplified version of the log to DeepSeek for analysis
                return $"[LOG_DATA] {json}";
            }
            catch (Exception ex)
            {
                return $"读取日志失败: {ex.Message}";
            }
        }

        private async Task<string> NavigateToFeatureAsync(string args)
        {
            var doc = JsonDocument.Parse(args);
            string feature = doc.RootElement.GetProperty("feature").GetString() ?? "";

            if (App.CurrentWindow is MainWindow mainWindow)
            {
                if (!string.IsNullOrEmpty(feature))
                {
                    mainWindow.NavigateToTool(feature);
                    return $"已为你跳转到 {feature} 界面。";
                }
            }
            return $"无法找到功能：{feature}。";
        }
    }

    public class StartQuickScanMessage : CommunityToolkit.Mvvm.Messaging.Messages.AsyncRequestMessage<string> { }
    public class RunCleanupMessage : CommunityToolkit.Mvvm.Messaging.Messages.AsyncRequestMessage<string> { }
}
namespace BlueSapphire.Services
{
    public class RunAutomaticLowRiskCleanupMessage : CommunityToolkit.Mvvm.Messaging.Messages.AsyncRequestMessage<string> { }
}
