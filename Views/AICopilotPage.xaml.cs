using BlueSapphire.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
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

            systemPrompt += "\n请使用自然、亲切的语气与用户交流。你可以根据上面的功能列表回答你能做什么。当用户需要执行操作时，请主动调用对应的 function call（工具）。";

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
                        Description = "Starts the smart system cleanup process. Use this when the user wants to clean up junk files or optimize system storage."
                    }
                },
                new ChatTool
                {
                    Function = new ChatFunction
                    {
                        Name = "execute_cleanup",
                        Description = "Executes the cleanup process to free up space. Use this when the user explicitly confirms or asks you to clean up the scanned items."
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

            // Call AI
            string responseText = await _aiService.SendChatAsync(_messageHistory, tools);

            // Handle Function Calling
            if (responseText.StartsWith("[TOOL_CALL]"))
            {
                string toolCallJson = responseText.Substring("[TOOL_CALL]".Length).Trim();
                string toolResult = await _toolsRegistry.ExecuteToolCallAsync(toolCallJson);
                
                _messageHistory.Add(new ChatMessage { Role = "assistant", Content = responseText }); // Ideally we'd pass the actual tool_calls object back, but for simplicity we skip full multi-turn tool history tracking if not supported natively. 
                // Wait, DeepSeek expects proper tool_calls format. For a simpler implementation, we just append the result as a user message or system message to continue the conversation.
                
                _messageHistory.Add(new ChatMessage { Role = "system", Content = $"函数执行结果: {toolResult}。请用自然语言将结果反馈给用户。" });

                // Call AI again to get the natural language response
                string finalResponse = await _aiService.SendChatAsync(_messageHistory, tools);
                _messageHistory.Add(new ChatMessage { Role = "assistant", Content = finalResponse });
                AddMessage("助理", finalResponse);
            }
            else
            {
                _messageHistory.Add(new ChatMessage { Role = "assistant", Content = responseText });
                AddMessage("助理", responseText);
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

