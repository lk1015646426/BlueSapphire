using BlueSapphire.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlueSapphire.Views
{
    public sealed partial class AICopilotPage : Page
    {
        private readonly DeepSeekAIService _aiService;
        private readonly AIToolsRegistry _toolsRegistry;
        public ObservableCollection<ChatBubble> Messages { get; set; } = new();

        private System.Collections.Generic.List<ChatMessage> _messageHistory = new();

        public AICopilotPage()
        {
            this.InitializeComponent();
            _aiService = App.Current.Services.GetRequiredService<DeepSeekAIService>();
            _toolsRegistry = App.Current.Services.GetRequiredService<AIToolsRegistry>();
            ChatList.ItemsSource = Messages;
            
            var systemPrompt = "你现在是“蓝宝石（BlueSapphire）”工具箱的智能助理。蓝宝石是一款 Windows 桌面效率软件，目前系统已安装的功能包括：\n";

            if (App.CurrentWindow is MainWindow mainWindow)
            {
                foreach (var tool in mainWindow.Tools)
                {
                    systemPrompt += $"- 【{tool.Title}】 (ID: {tool.Id})\n";
                }
            }

            try
            {
                var drives = string.Join(", ", System.IO.DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name));
                systemPrompt += $"\n【可用本地磁盘】系统当前就绪的磁盘有：{drives}。";
            }
            catch { }

            systemPrompt += "\n请使用自然、亲切的语气与用户交流。你可以根据上面的功能列表回答你能做什么。当用户需要执行操作时，请主动调用对应的 function call（工具）。\n【重要流程与排版规则】\n1. 执行清理前必须先与用户确认要扫描哪些磁盘（可全盘或指定），确认后调用 start_smart_cleanup。收到扫描结果后，**必须使用 Markdown 语法（如表格、加粗、Emoji、列表等）进行优美排版**，向用户清晰展示各项详细体积与数量，并且**作为电脑专家，用通俗易懂的语言简单解释这些垃圾文件（例如系统临时文件、应用缓存等）是用来做什么的，删除它们会有什么好处**，最后询问用户清理意向。\n2. 用户确认后调用 execute_cleanup。\n3. **极其重要：绝对不可伪造或虚构清理结果！** 必须严格读取 execute_cleanup 返回的 JSON 真实数据。如果真实数据显示释放了 0 B 或存在大量失败项，必须如实且美观地告知用户，并附上返回的失败原因供用户参考。整个过程不跳转界面，全在对话框完成！";

            // 注入系统级提示词，赋予 AI “认知”
            _messageHistory.Add(new ChatMessage 
            { 
                Role = "system", 
                Content = systemPrompt
            });

            AddMessage("助理", "你好！我是蓝宝石智能引擎。我已全自动感知了系统中安装的所有工具。你可以直接告诉我你想做什么。");
        }

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SendBtn_Click(this, new RoutedEventArgs());
            }
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            InputBox.Text = string.Empty;
            InputBox.IsEnabled = false;
            SendBtn.IsEnabled = false;

            AddMessage("用户", text);
            _messageHistory.Add(new ChatMessage { Role = "user", Content = text });

            // Show typing...
            var typingBubble = new ChatBubble { Content = "思考中...", IsUser = false };
            Messages.Add(typingBubble);
            _ = ScrollToBottomAsync();

            await ProcessMessageAsync();

            Messages.Remove(typingBubble);
            InputBox.IsEnabled = true;
            SendBtn.IsEnabled = true;
            InputBox.Focus(FocusState.Programmatic);
        }

        private async Task ProcessMessageAsync()
        {
            var featureEnum = new System.Collections.Generic.List<string>();
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                foreach (var tool in mainWindow.Tools)
                {
                    featureEnum.Add(tool.Id);
                }
            }
            featureEnum.Add("Settings");

            var tools = new System.Collections.Generic.List<ChatTool>
            {
                new ChatTool
                {
                    Function = new ChatFunction
                    {
                        Name = "start_smart_cleanup",
                        Description = "Starts the smart system cleanup process. If the user explicitly specifies the drives (e.g. 'all drives', 'C drive'), call this tool immediately. If they do NOT specify any drives, you MUST ask them which drives they want to scan before calling this.",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                scan_mode = new
                                {
                                    type = "string",
                                    description = "The mode of scanning. 'Quick' for fast scanning of common junk, 'Deep' for full disk deep scan of large files. Default is 'Deep' if specific drives are given."
                                },
                                drives_to_scan = new
                                {
                                    type = "array",
                                    items = new { type = "string" },
                                    description = "List of drive roots to scan, e.g. [\"C:\\\\\", \"D:\\\\\"]. Pass [\"All\"] to scan all available drives."
                                }
                            },
                            required = new[] { "scan_mode", "drives_to_scan" }
                        }
                    }
                },
                new ChatTool
                {
                    Function = new ChatFunction
                    {
                        Name = "execute_cleanup",
                        Description = "Executes the cleanup process to free up space. Use this ONLY AFTER the user has explicitly confirmed what to clean from the scan results.",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                categories_to_clean = new
                                {
                                    type = "array",
                                    items = new { type = "string" },
                                    description = "The categories or RiskLevels to clean, e.g. ['Safe'], ['Review'], or specific rule names."
                                }
                            },
                            required = new[] { "categories_to_clean" }
                        }
                    }
                },
                new ChatTool
                {
                    Function = new ChatFunction
                    {
                        Name = "analyze_latest_cleanup_log",
                        Description = "Reads the latest cleanup audit log and returns its JSON content. Use this to analyze what was cleaned up and explain it to the user."
                    }
                },
                new ChatTool
                {
                    Function = new ChatFunction
                    {
                        Name = "navigate_to_feature",
                        Description = "Navigates the UI to a specific feature page. Use this when the user asks to open a specific tool.",
                        Parameters = new { type = "object", properties = new { feature = new { type = "string", @enum = featureEnum.ToArray() } }, required = new[] { "feature" } }
                    }
                }
            };

            // Handle Function Calling
            int maxTurns = 5;
            while (maxTurns-- > 0)
            {
                // Call AI
                var aiMessage = await _aiService.SendChatAsync(_messageHistory, tools);
                _messageHistory.Add(aiMessage);

                if (aiMessage.ToolCalls != null)
                {
                    var toolCallsArray = aiMessage.ToolCalls.Value;
                    foreach (var call in toolCallsArray.EnumerateArray())
                    {
                        var function = call.GetProperty("function");
                        var name = function.GetProperty("name").GetString();
                        var args = function.GetProperty("arguments").GetString() ?? "{}";
                        var toolCallId = call.GetProperty("id").GetString();

                        string toolCallJson = $"[{{\"function\":{{\"name\":\"{name}\",\"arguments\":{System.Text.Json.JsonSerializer.Serialize(args)}}}}}]";
                        string toolResult = await _toolsRegistry.ExecuteToolCallAsync(toolCallJson);
                        
                        _messageHistory.Add(new ChatMessage { Role = "tool", ToolCallId = toolCallId, Content = toolResult });
                    }
                    // Loop again so AI can generate the text response
                }
                else
                {
                    AddMessage("助理", aiMessage.Content ?? "");
                    break;
                }
            }
            if (maxTurns <= 0)
            {
                AddMessage("助理", "后台任务执行次数已达上限，系统已中断连续操作。");
            }
        }

        private void AddMessage(string role, string content)
        {
            bool isUser = role == "用户";
            Messages.Add(new ChatBubble { Content = content, IsUser = isUser });
            _ = ScrollToBottomAsync();
        }

        private async Task ScrollToBottomAsync()
        {
            await Task.Delay(50);
            ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
        }
    }

    public class ChatBubble
    {
        public string Content { get; set; } = "";
        public bool IsUser { get; set; }

        public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public SolidColorBrush BackgroundBrush => IsUser 
            ? new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue) { Opacity = 0.8 } 
            : new SolidColorBrush(Microsoft.UI.Colors.DarkGray) { Opacity = 0.4 };
    }
}

