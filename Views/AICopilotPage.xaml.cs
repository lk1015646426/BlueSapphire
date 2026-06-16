using System;
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
            var featureEnum = new System.Collections.Generic.List<string>();
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                foreach (var tool in mainWindow.Tools)
                {
                    featureEnum.Add(tool.Id);
                }
            }
            
            _messageHistory.Add(_toolsRegistry.GetSystemPrompt(featureEnum));

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

            await _toolsRegistry.RunAgentLoopAsync(
                _messageHistory, 
                featureEnum, 
                (msg) => 
                {
                    if (msg.Role == "assistant")
                    {
                        AddMessage("助理", msg.Content ?? "");
                    }
                    else if (msg.Role == "tool_progress")
                    {
                        AddMessage("系统", msg.Content ?? "");
                    }
                },
                ShowConfirmationDialogAsync);
        }

        private async Task<bool> ShowConfirmationDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "清理确认",
                Content = message,
                PrimaryButtonText = "确认清理",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
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

